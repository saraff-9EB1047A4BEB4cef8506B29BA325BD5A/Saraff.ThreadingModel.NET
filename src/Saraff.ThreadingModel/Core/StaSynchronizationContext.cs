using System;
using System.Threading;

namespace Saraff.ThreadingModel.Core {

    internal sealed class StaSynchronizationContext : SynchronizationContext {
        private readonly SingleThreadedDispatcher _dispatcher;

        public StaSynchronizationContext(SingleThreadedDispatcher dispatcher) {
            this._dispatcher = dispatcher;
        }

        public override void Post(SendOrPostCallback d, object? state) {
            if(d == null) {
                throw new ArgumentNullException(nameof(d));
            }
            this._dispatcher.PostContinuation(d, state);
        }

        public override void Send(SendOrPostCallback d, object? state) {
            if(d == null) {
                throw new ArgumentNullException(nameof(d));
            }
            if(this._dispatcher.IsDispatcherThread) {
                d(state);
                return;
            }
            this._dispatcher.Invoke(() => {
                d(state);
                return true;
            });
        }

        public override SynchronizationContext CreateCopy() {
            return this;
        }
    }
}
