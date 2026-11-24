using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Saraff.ThreadingModel.Tests {

    public sealed class StressTests {

        [Fact]
        public void ParallelSends_AreAllExecuted() {
            using(var dispatcher = new SingleThreadedDispatcher(new SingleThreadedDispatcherOptions { ThreadName = "stress" })) {
                var counter = 0;
                Parallel.For(0, 8, _ => {
                    for(var i = 0; i < 100; i++) {
                        dispatcher.Send(() => Interlocked.Increment(ref counter));
                    }
                });
                Assert.Equal(800, counter);
            }
        }

        [Fact]
        public void ParallelProxyCalls_AreSerialized() {
            var target = new Scanner();
            using(var proxy = new SingleThreadedProxy(target)) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                Parallel.For(0, 4, _ => {
                    for(var i = 0; i < 25; i++) {
                        scanner.Scan(1);
                    }
                });
                Assert.Equal(1, target.MaxActive);
            }
        }

        [Fact]
        public void Events_UnderLoad_AreDelivered() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                var count = 0;
                scanner.Completed += (sender, args) => Interlocked.Increment(ref count);
                Parallel.For(0, 4, _ => {
                    for(var i = 0; i < 25; i++) {
                        scanner.RaiseCompleted(i);
                    }
                });
                Assert.Equal(100, count);
            }
        }
    }
}
