using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Saraff.ThreadingModel.Core;

namespace Saraff.ThreadingModel {

    /// <summary>
    /// Диспетчер, исполняющий работу в выделенном потоке (по умолчанию STA) с откачкой сообщений Win32.
    /// A dispatcher that runs work on a dedicated thread (STA by default) and pumps Win32 messages.
    /// </summary>
    public sealed class SingleThreadedDispatcher : IDisposable
#if NET8_0_OR_GREATER
        , IAsyncDisposable
#endif
    {
        [ThreadStatic]
        private static SingleThreadedDispatcher? _current;

        private readonly object _sync = new object();
        private readonly ConcurrentQueue<WorkItem> _queue = new ConcurrentQueue<WorkItem>();
        private readonly ConcurrentQueue<ContinuationWorkItem> _continuations = new ConcurrentQueue<ContinuationWorkItem>();
        private readonly AutoResetEvent _queueEvent = new AutoResetEvent(false);
        private readonly AutoResetEvent _continuationEvent = new AutoResetEvent(false);
        private readonly ManualResetEvent _shutdownEvent = new ManualResetEvent(false);
        private readonly ManualResetEventSlim _startedEvent = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim _stoppedEvent = new ManualResetEventSlim(false);
        private readonly CancellationTokenSource _shutdownSource = new CancellationTokenSource();
        private readonly SingleThreadedDispatcherOptions _options;
        private readonly StaSynchronizationContext _synchronizationContext;
        private readonly QueueMessageDispatcher? _messages;
        private readonly Thread _thread;
        private StaMessagePump? _pump;
        private int _threadId;
        private volatile bool _stopping;
        private volatile bool _disposed;

        /// <summary>
        /// Создаёт диспетчер и запускает служебный поток. Creates a dispatcher and starts its thread.
        /// </summary>
        /// <param name="options">Настройки диспетчера. Dispatcher options.</param>
        /// <param name="services">Провайдер сервисов для обработчиков очереди. Service provider for queue handlers.</param>
        public SingleThreadedDispatcher(SingleThreadedDispatcherOptions? options = null, IServiceProvider? services = null) {
            this._options = options ?? new SingleThreadedDispatcherOptions();
            if(this._options.MaxQueueLength < 0) {
                throw new ArgumentOutOfRangeException(nameof(options), "MaxQueueLength не может быть отрицательным.");
            }
            this._synchronizationContext = new StaSynchronizationContext(this);
            this._messages = services == null ? null : new QueueMessageDispatcher(this, services);
            this._thread = new Thread(this.Run) {
                IsBackground = true,
                Name = this._options.ThreadName ?? "Saraff.ThreadingModel dispatcher"
            };
            if(this._options.ApartmentState != ApartmentState.Unknown) {
                this._thread.SetApartmentState(this._options.ApartmentState);
            }
            this._thread.Start();
            if(!this._startedEvent.Wait(this._options.StartupTimeout)) {
                throw new TimeoutException("Служебный поток диспетчера не запустился за " + this._options.StartupTimeout + ".");
            }
        }

        /// <summary>
        /// Контекст синхронизации служебного потока. Synchronization context of the dispatcher thread.
        /// </summary>
        public SynchronizationContext SynchronizationContext {
            get {
                return this._synchronizationContext;
            }
        }

        /// <summary>
        /// Идентификатор служебного потока. Managed identifier of the dispatcher thread.
        /// </summary>
        public int ThreadId {
            get {
                return this._threadId;
            }
        }

        /// <summary>
        /// Признак того, что текущий поток является служебным. Whether the current thread is the dispatcher thread.
        /// </summary>
        public bool IsDispatcherThread {
            get {
                return Thread.CurrentThread.ManagedThreadId == this._threadId;
            }
        }

        /// <summary>
        /// Диспетчер текущего служебного потока. The dispatcher of the current dispatcher thread.
        /// </summary>
        public static SingleThreadedDispatcher? Current {
            get {
                return _current;
            }
        }

        /// <summary>
        /// Возникает при необработанной ошибке служебного потока или очереди. Raised on an unhandled dispatcher or queue error.
        /// </summary>
        public event Action<Exception>? UnhandledException;

        /// <summary>
        /// Синхронно исполняет действие в служебном потоке. Runs an action on the dispatcher thread and waits for completion.
        /// </summary>
        /// <param name="action">Действие. Action to run.</param>
        /// <param name="timeout">Таймаут ожидания. Wait timeout.</param>
        public void Send(Action action, TimeSpan? timeout = null) {
            if(action == null) {
                throw new ArgumentNullException(nameof(action));
            }
            this.ThrowIfDisposed();
            if(this.IsDispatcherThread) {
                action();
                return;
            }
            var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.Enqueue(new ActionWorkItem(action, completion, this.ReportUnhandled));
            WaitCompletion(completion.Task, timeout ?? this._options.InvokeTimeout);
        }

        /// <summary>
        /// Синхронно исполняет функцию в служебном потоке. Runs a function on the dispatcher thread and returns its result.
        /// </summary>
        /// <typeparam name="T">Тип результата. Result type.</typeparam>
        /// <param name="func">Функция. Function to run.</param>
        /// <param name="timeout">Таймаут ожидания. Wait timeout.</param>
        /// <returns>Результат функции. Function result.</returns>
        public T Invoke<T>(Func<T> func, TimeSpan? timeout = null) {
            if(func == null) {
                throw new ArgumentNullException(nameof(func));
            }
            this.ThrowIfDisposed();
            if(this.IsDispatcherThread) {
                return func();
            }
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.Enqueue(new FuncWorkItem<T>(func, completion, CancellationToken.None));
            WaitCompletion(completion.Task, timeout ?? this._options.InvokeTimeout);
            return completion.Task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Ставит действие в очередь без ожидания результата. Queues an action without waiting for completion.
        /// </summary>
        /// <param name="action">Действие. Action to queue.</param>
        public void Post(Action action) {
            if(action == null) {
                throw new ArgumentNullException(nameof(action));
            }
            this.ThrowIfDisposed();
            this.Enqueue(new ActionWorkItem(action, null, this.ReportUnhandled));
        }

        /// <summary>
        /// Асинхронно исполняет функцию в служебном потоке. Runs a function on the dispatcher thread asynchronously.
        /// </summary>
        /// <typeparam name="T">Тип результата. Result type.</typeparam>
        /// <param name="func">Функция. Function to run.</param>
        /// <param name="cancellationToken">Токен отмены. Cancellation token.</param>
        /// <returns>Задача с результатом. Task with the result.</returns>
        public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken = default(CancellationToken)) {
            if(func == null) {
                throw new ArgumentNullException(nameof(func));
            }
            this.ThrowIfDisposed();
            if(this.IsDispatcherThread) {
                try {
                    return Task.FromResult(func());
                } catch(OperationCanceledException ex) when(ex.CancellationToken == cancellationToken) {
                    return Task.FromCanceled<T>(cancellationToken);
                } catch(Exception ex) {
                    return Task.FromException<T>(ex);
                }
            }
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try {
                this.Enqueue(new FuncWorkItem<T>(func, completion, cancellationToken));
            } catch(Exception ex) {
                completion.TrySetException(ex);
            }
            return completion.Task;
        }

        /// <summary>
        /// Ставит сообщение в очередь. Queues a message for registered handlers.
        /// </summary>
        /// <typeparam name="TMessage">Тип сообщения. Message type.</typeparam>
        /// <param name="message">Сообщение. Message.</param>
        public void Post<TMessage>(TMessage message) where TMessage : notnull {
            if(message == null) {
                throw new ArgumentNullException(nameof(message));
            }
            this.ThrowIfDisposed();
            var messages = this._messages ?? throw new InvalidOperationException("Диспетчер создан без IServiceProvider: очередь сообщений недоступна.");
            this.Enqueue(new MessageWorkItem(messages, message, this._shutdownSource.Token, null, this.ReportUnhandled));
        }

        /// <summary>
        /// Ставит сообщение в очередь и возвращает задачу завершения обработки. Queues a message and returns a completion task.
        /// </summary>
        /// <typeparam name="TMessage">Тип сообщения. Message type.</typeparam>
        /// <param name="message">Сообщение. Message.</param>
        /// <param name="cancellationToken">Токен отмены. Cancellation token.</param>
        /// <returns>Задача завершения обработки. Completion task.</returns>
        public Task PostAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default(CancellationToken)) where TMessage : notnull {
            if(message == null) {
                throw new ArgumentNullException(nameof(message));
            }
            this.ThrowIfDisposed();
            var messages = this._messages ?? throw new InvalidOperationException("Диспетчер создан без IServiceProvider: очередь сообщений недоступна.");
            var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            try {
                this.Enqueue(new MessageWorkItem(messages, message, cancellationToken, completion, this.ReportUnhandled));
            } catch(Exception ex) {
                completion.TrySetException(ex);
            }
            return completion.Task;
        }

        /// <summary>
        /// Останавливает служебный поток. Stops the dispatcher thread.
        /// </summary>
        public void Dispose() {
            lock(this._sync) {
                if(this._disposed) {
                    return;
                }
                this._disposed = true;
                this._stopping = true;
            }
            try {
                this._shutdownSource.Cancel();
            } catch(ObjectDisposedException) {
            }
            this._shutdownEvent.Set();
            this._queueEvent.Set();
            this._continuationEvent.Set();
            if(!this.IsDispatcherThread) {
                this._stoppedEvent.Wait(this._options.DisposeTimeout);
            }
        }

#if NET8_0_OR_GREATER

        /// <summary>
        /// Останавливает служебный поток. Stops the dispatcher thread.
        /// </summary>
        /// <returns>Завершённая задача. Completed task.</returns>
        public ValueTask DisposeAsync() {
            this.Dispose();
            return default(ValueTask);
        }

#endif

        internal SingleThreadedDispatcherOptions Options {
            get {
                return this._options;
            }
        }

        internal void PostContinuation(SendOrPostCallback callback, object? state) {
            if(this._disposed || this._stopping) {
                return;
            }
            this._continuations.Enqueue(new ContinuationWorkItem(callback, state, this.ReportUnhandled));
            this._continuationEvent.Set();
        }

        internal void AwaitWithPump(Task task, TimeSpan? timeout) {
            if(task == null) {
                throw new ArgumentNullException(nameof(task));
            }
            if(task.IsCompleted) {
                task.GetAwaiter().GetResult();
                return;
            }
            if(!this.IsDispatcherThread) {
                WaitCompletion(task, timeout);
                return;
            }
            var taskHandle = ((IAsyncResult)task).AsyncWaitHandle;
            var handles = new WaitHandle[] { this._continuationEvent, taskHandle, this._shutdownEvent };
            var stopwatch = timeout.HasValue ? Stopwatch.StartNew() : null;
            while(!task.IsCompleted) {
                while(this._continuations.TryDequeue(out var continuation)) {
                    continuation.Execute();
                }
                if(task.IsCompleted) {
                    break;
                }
                if(this._stopping) {
                    break;
                }
                TimeSpan? remaining = null;
                if(timeout.HasValue && stopwatch != null) {
                    remaining = timeout.Value - stopwatch.Elapsed;
                    if(remaining.Value <= TimeSpan.Zero) {
                        throw new TimeoutException("Обработчик сообщения не завершился за " + timeout.Value + ".");
                    }
                }
                if(this._pump != null) {
                    this._pump.Wait(handles, remaining ?? Timeout.InfiniteTimeSpan);
                } else {
                    WaitHandle.WaitAny(handles, remaining ?? Timeout.InfiniteTimeSpan);
                }
            }
            if(this._stopping && !task.IsCompleted) {
                throw new OperationCanceledException(this._shutdownSource.Token);
            }
            task.GetAwaiter().GetResult();
        }

        internal void ReportUnhandled(Exception error) {
            var handler = this.UnhandledException;
            if(handler == null) {
                return;
            }
            try {
                handler(error);
            } catch(Exception) {
            }
        }

        private static void WaitCompletion(Task task, TimeSpan? timeout) {
            var asyncResult = (IAsyncResult)task;
            if(timeout.HasValue) {
                if(!asyncResult.AsyncWaitHandle.WaitOne(timeout.Value)) {
                    throw new TimeoutException("Операция не завершилась за " + timeout.Value + ".");
                }
            } else {
                asyncResult.AsyncWaitHandle.WaitOne();
            }
            task.GetAwaiter().GetResult();
        }

        private void Enqueue(WorkItem item) {
            lock(this._sync) {
                if(this._disposed || this._stopping) {
                    throw new ObjectDisposedException(nameof(SingleThreadedDispatcher));
                }
                if(this._options.MaxQueueLength > 0 && this._queue.Count >= this._options.MaxQueueLength) {
                    throw new InvalidOperationException("Очередь диспетчера переполнена (лимит " + this._options.MaxQueueLength + ").");
                }
                this._queue.Enqueue(item);
            }
            this._queueEvent.Set();
        }

        private void ThrowIfDisposed() {
            if(this._disposed || this._stopping) {
                throw new ObjectDisposedException(nameof(SingleThreadedDispatcher));
            }
        }

        private void Run() {
            this._threadId = Thread.CurrentThread.ManagedThreadId;
            _current = this;
            SynchronizationContext.SetSynchronizationContext(this._synchronizationContext);
            try {
                if(this._options.UseMessagePump) {
                    this._pump = new StaMessagePump();
                }
            } catch(Exception ex) {
                this._pump = null;
                this.ReportUnhandled(ex);
            }
            this._startedEvent.Set();
            var waitHandles = new WaitHandle[] { this._queueEvent, this._continuationEvent, this._shutdownEvent };
            try {
                while(true) {
                    var worked = false;
                    while(this._queue.TryDequeue(out var item)) {
                        Execute(item);
                        worked = true;
                    }
                    while(this._continuations.TryDequeue(out var continuation)) {
                        continuation.Execute();
                        worked = true;
                    }
                    PumpMessages();
                    if(this._stopping && this._queue.IsEmpty && this._continuations.IsEmpty) {
                        break;
                    }
                    if(worked) {
                        continue;
                    }
                    try {
                        if(this._pump != null) {
                            this._pump.Wait(waitHandles, Timeout.InfiniteTimeSpan);
                        } else {
                            WaitHandle.WaitAny(waitHandles);
                        }
                    } catch(ObjectDisposedException) {
                        break;
                    }
                }
            } finally {
                this.Drain();
                SynchronizationContext.SetSynchronizationContext(null);
                _current = null;
                this._stoppedEvent.Set();
            }
        }

        private void Execute(WorkItem item) {
            try {
                item.Execute();
            } catch(Exception ex) {
                this.ReportUnhandled(ex);
            }
        }

        private void PumpMessages() {
            var pump = this._pump;
            if(pump != null) {
                pump.PumpPending();
            }
        }

        private void Drain() {
            var error = new ObjectDisposedException(nameof(SingleThreadedDispatcher));
            while(this._queue.TryDequeue(out var item)) {
                item.Abort(error);
            }
            while(this._continuations.TryDequeue(out var continuation)) {
                continuation.Abort(error);
            }
        }
    }
}
