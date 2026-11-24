using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Saraff.IoC;

namespace Saraff.ThreadingModel.Tests {

    public sealed class ScanEventArgs : EventArgs {
        public ScanEventArgs(int pages) {
            this.Pages = pages;
        }

        public int Pages { get; }
    }

    public interface IScanner {
        string Name { get; }

        int ThreadId { get; }

        ApartmentState Apartment { get; }

        int CompletedSubscribers { get; }

        int Zoom { get; set; }

        int Scan(int pages);

        T Echo<T>(T value);

        void Sleep(int milliseconds);


        void Update(ref int value, out int doubled);

        void Fail();

        void RaiseCompleted(int pages);

        void RaiseWithNullSender();

        event EventHandler<ScanEventArgs> Completed;

        event Action<int> Progress;

        event Func<int, int> Transform;
    }

    public class Scanner : MarshalByRefObject, IScanner {
        private int _active;
        private int _maxActive;

        public event EventHandler<ScanEventArgs> Completed;

        public event Action<int> Progress;

        public event Func<int, int> Transform;

        public string Name {
            get {
                return "scanner";
            }
        }

        public int ThreadId {
            get {
                return Thread.CurrentThread.ManagedThreadId;
            }
        }

        public ApartmentState Apartment {
            get {
                return Thread.CurrentThread.GetApartmentState();
            }
        }

        public int CompletedSubscribers {
            get {
                var handler = this.Completed;
                return handler == null ? 0 : handler.GetInvocationList().Length;
            }
        }

        public int MaxActive {
            get {
                return this._maxActive;
            }
        }

        private int _zoom;

        public int Zoom {
            get {
                return this._zoom;
            }
            set {
                this._zoom = value;
            }
        }

        public T Echo<T>(T value) {
            return value;
        }

        public void Sleep(int milliseconds) {
            Thread.Sleep(milliseconds);
        }

        public int Scan(int pages) {
            var active = Interlocked.Increment(ref this._active);
            if(active > this._maxActive) {
                this._maxActive = active;
            }
            try {
                Thread.Sleep(20);
                this.Progress?.Invoke(pages);
                var result = this.Transform == null ? pages : this.Transform(pages);
                this.Completed?.Invoke(this, new ScanEventArgs(pages));
                return result;
            } finally {
                Interlocked.Decrement(ref this._active);
            }
        }

        public void Update(ref int value, out int doubled) {
            value++;
            doubled = value * 2;
        }

        public void Fail() {
            throw new InvalidOperationException("failure");
        }

        public void RaiseCompleted(int pages) {
            this.Completed?.Invoke(this, new ScanEventArgs(pages));
        }

        public void RaiseWithNullSender() {
            this.Completed?.Invoke(null, new ScanEventArgs(1));
        }
    }

    public interface IBroken {
        void Ping();
    }

    public sealed class BrokenCtor : IBroken {
        public BrokenCtor() {
            throw new InvalidOperationException("ctor failure");
        }

        public void Ping() {
        }
    }

    public interface IFirst {
    }

    public interface ISecond {
    }

    public sealed class TwoInterfaces : IFirst, ISecond {
    }

    public sealed class RecordingOperation {
        public RecordingOperation(int pages, int threadId) {
            this.Pages = pages;
            this.ThreadId = threadId;
        }

        public int Pages { get; }

        public int ThreadId { get; }
    }

    public static class Recorder {
        private static readonly ConcurrentQueue<RecordingOperation> Operations = new ConcurrentQueue<RecordingOperation>();

        public static void Reset() {
            while(Operations.TryDequeue(out _)) {
            }
        }

        public static void Record(int pages, int threadId) {
            Operations.Enqueue(new RecordingOperation(pages, threadId));
        }

        public static RecordingOperation[] Snapshot() {
            return Operations.ToArray();
        }
    }

    public sealed class RecordingContext : SynchronizationContext {
        private int _sendCount;
        private int _postCount;

        public int SendCount {
            get {
                return Volatile.Read(ref this._sendCount);
            }
        }

        public int PostCount {
            get {
                return Volatile.Read(ref this._postCount);
            }
        }

        public override void Send(SendOrPostCallback d, object state) {
            Interlocked.Increment(ref this._sendCount);
            d(state);
        }

        public override void Post(SendOrPostCallback d, object state) {
            Interlocked.Increment(ref this._postCount);
            d(state);
        }
    }

    public static class Wait {
        public static bool Until(Func<bool> condition, int milliseconds = 5000) {
            var stopwatch = Stopwatch.StartNew();
            while(!condition()) {
                if(stopwatch.ElapsedMilliseconds > milliseconds) {
                    return false;
                }
                Thread.Sleep(10);
            }
            return true;
        }
    }

    public sealed class ScopedState : IDisposable {
        public static int DisposedCount;

        public void Dispose() {
            Interlocked.Increment(ref DisposedCount);
        }

        public static void Reset() {
            Interlocked.Exchange(ref DisposedCount, 0);
        }
    }

    public sealed class ScanMessage {
        public ScanMessage(int pages) {
            this.Pages = pages;
        }

        public int Pages { get; }
    }

    public sealed class SlowMessage {
    }

    public sealed class UnhandledMessage {
    }

    public class BaseMessage {
    }

    public sealed class DerivedMessage : BaseMessage {
    }

    public sealed class BaseMessageHandler : IQueueMessageHandler<BaseMessage> {
        private static int _received;

        public static int Received {
            get {
                return _received;
            }

            set {
                _received = value;
            }
        }

        public Task HandleAsync(BaseMessage message, CancellationToken cancellationToken) {
            Interlocked.Increment(ref _received);
            return Task.CompletedTask;
        }
    }

    public sealed class RecordingScanHandler : IQueueMessageHandler<ScanMessage> {
        [ServiceRequired]
        public RecordingScanHandler(ScopedState state) {
            this.State = state;
        }

        public ScopedState State { get; }

        public Task HandleAsync(ScanMessage message, CancellationToken cancellationToken) {
            Recorder.Record(message.Pages, Thread.CurrentThread.ManagedThreadId);
            return Task.CompletedTask;
        }
    }

    public sealed class SlowHandler : IQueueMessageHandler<SlowMessage> {
        public async Task HandleAsync(SlowMessage message, CancellationToken cancellationToken) {
            await Task.Delay(10000, cancellationToken);
        }
    }

    public sealed class ThrowingHandler : IQueueMessageHandler<UnhandledMessage> {
        public Task HandleAsync(UnhandledMessage message, CancellationToken cancellationToken) {
            throw new InvalidOperationException("handler failure");
        }
    }

    public delegate void RefEventHandler(ref int value);

    public interface IRefEventSource {
        event RefEventHandler Changed;

        void Raise();
    }

    public interface IPing {
        string Value {
            get;
        }
    }

    public sealed class Ping : IPing {
        private readonly string _value;

        public Ping(string value) {
            this._value = value;
        }

        public string Value {
            get {
                return this._value;
            }
        }
    }

    public sealed class DerivedMessageHandler : IQueueMessageHandler<DerivedMessage> {
        private static int _received;

        public static int Received {
            get {
                return _received;
            }

            set {
                _received = value;
            }
        }

        public Task HandleAsync(DerivedMessage message, CancellationToken cancellationToken) {
            Interlocked.Increment(ref _received);
            return Task.CompletedTask;
        }
    }

    public sealed class ThrowingErrorHandler : IQueueMessageErrorHandler {
        public Task HandleAsync(object message, Exception error, CancellationToken cancellationToken) {
            throw new InvalidOperationException("error handler failure");
        }
    }

    public sealed class FirstErrorHandler : IQueueMessageErrorHandler {
        public static int Count;

        public Task HandleAsync(object message, Exception error, CancellationToken cancellationToken) {
            Interlocked.Increment(ref Count);
            return Task.CompletedTask;
        }
    }

    public sealed class SecondErrorHandler : IQueueMessageErrorHandler {
        public static int Count;

        public Task HandleAsync(object message, Exception error, CancellationToken cancellationToken) {
            Interlocked.Increment(ref Count);
            return Task.CompletedTask;
        }
    }


    public sealed class RefEventSource : IRefEventSource {
        public event RefEventHandler Changed;

        public void Raise() {
            var value = 0;
            this.Changed?.Invoke(ref value);
        }
    }

    public sealed class RecordingErrorHandler : IQueueMessageErrorHandler {
        private readonly ConcurrentQueue<Exception> _errors = new ConcurrentQueue<Exception>();

        public Exception[] Errors {
            get {
                return this._errors.ToArray();
            }
        }

        public Task HandleAsync(object message, Exception error, CancellationToken cancellationToken) {
            this._errors.Enqueue(error);
            return Task.CompletedTask;
        }
    }
}
