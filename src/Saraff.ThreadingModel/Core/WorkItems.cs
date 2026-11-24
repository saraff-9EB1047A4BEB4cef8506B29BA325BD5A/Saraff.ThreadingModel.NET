using System;
using System.Threading;
using System.Threading.Tasks;

namespace Saraff.ThreadingModel.Core {

    internal abstract class WorkItem {

        public abstract void Execute();

        public abstract void Abort(Exception error);
    }

    internal sealed class ActionWorkItem : WorkItem {
        private readonly Action _action;
        private readonly TaskCompletionSource<object?>? _completion;
        private readonly Action<Exception> _report;

        public ActionWorkItem(Action action, TaskCompletionSource<object?>? completion, Action<Exception> report) {
            this._action = action;
            this._completion = completion;
            this._report = report;
        }

        public override void Execute() {
            try {
                this._action();
                this._completion?.TrySetResult(null);
            } catch(Exception ex) {
                if(this._completion != null) {
                    this._completion.TrySetException(ex);
                } else {
                    this._report(ex);
                }
            }
        }

        public override void Abort(Exception error) {
            this._completion?.TrySetException(error);
        }
    }

    internal sealed class FuncWorkItem<T> : WorkItem {
        private readonly Func<T> _func;
        private readonly TaskCompletionSource<T> _completion;
        private readonly CancellationToken _cancellationToken;

        public FuncWorkItem(Func<T> func, TaskCompletionSource<T> completion, CancellationToken cancellationToken) {
            this._func = func;
            this._completion = completion;
            this._cancellationToken = cancellationToken;
        }

        public override void Execute() {
            if(this._cancellationToken.IsCancellationRequested) {
                this._completion.TrySetCanceled(this._cancellationToken);
                return;
            }
            try {
                this._completion.TrySetResult(this._func());
            } catch(OperationCanceledException ex) when(ex.CancellationToken == this._cancellationToken) {
                this._completion.TrySetCanceled(this._cancellationToken);
            } catch(Exception ex) {
                this._completion.TrySetException(ex);
            }
        }

        public override void Abort(Exception error) {
            this._completion.TrySetException(error);
        }
    }

    internal sealed class ContinuationWorkItem : WorkItem {
        private readonly SendOrPostCallback _callback;
        private readonly object? _state;
        private readonly Action<Exception> _report;

        public ContinuationWorkItem(SendOrPostCallback callback, object? state, Action<Exception> report) {
            this._callback = callback;
            this._state = state;
            this._report = report;
        }

        public override void Execute() {
            try {
                this._callback(this._state);
            } catch(Exception ex) {
                this._report(ex);
            }
        }

        public override void Abort(Exception error) {
        }
    }

    internal sealed class MessageWorkItem : WorkItem {
        private readonly QueueMessageDispatcher _messages;
        private readonly object _message;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<object?>? _completion;
        private readonly Action<Exception> _report;

        public MessageWorkItem(QueueMessageDispatcher messages, object message, CancellationToken cancellationToken, TaskCompletionSource<object?>? completion, Action<Exception> report) {
            this._messages = messages;
            this._message = message;
            this._cancellationToken = cancellationToken;
            this._completion = completion;
            this._report = report;
        }

        public override void Execute() {
            if(this._cancellationToken.IsCancellationRequested) {
                this._completion?.TrySetCanceled(this._cancellationToken);
                return;
            }
            try {
                this._messages.Process(this._message, this._cancellationToken);
                this._completion?.TrySetResult(null);
            } catch(OperationCanceledException) when(this._cancellationToken.IsCancellationRequested) {
                this._completion?.TrySetCanceled(this._cancellationToken);
            } catch(Exception ex) {
                if(this._completion != null) {
                    this._completion.TrySetException(ex);
                } else {
                    this._report(ex);
                }
            }
        }

        public override void Abort(Exception error) {
            this._completion?.TrySetException(error);
        }
    }
}
