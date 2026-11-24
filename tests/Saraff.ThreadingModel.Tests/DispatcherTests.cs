using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Saraff.ThreadingModel.Tests {

    public sealed class DispatcherTests {

        [Fact]
        public void Send_RunsOnStaThread() {
            using(var dispatcher = new SingleThreadedDispatcher()) {
                var threadId = 0;
                var apartment = ApartmentState.Unknown;
                var isDispatcher = false;
                var current = default(SingleThreadedDispatcher);
                dispatcher.Send(() => {
                    threadId = Thread.CurrentThread.ManagedThreadId;
                    apartment = Thread.CurrentThread.GetApartmentState();
                    isDispatcher = dispatcher.IsDispatcherThread;
                    current = SingleThreadedDispatcher.Current;
                });
                Assert.Equal(dispatcher.ThreadId, threadId);
                Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, threadId);
                Assert.Equal(ApartmentState.STA, apartment);
                Assert.True(isDispatcher);
                Assert.Same(dispatcher, current);
            }
        }

        [Fact]
        public void Send_FromDispatcherThread_ExecutesInline() {
            using(var dispatcher = new SingleThreadedDispatcher()) {
                var threadId = 0;
                dispatcher.Send(() => dispatcher.Send(() => threadId = Thread.CurrentThread.ManagedThreadId));
                Assert.Equal(dispatcher.ThreadId, threadId);
            }
        }

        [Fact]
        public void Send_PropagatesException() {
            using(var dispatcher = new SingleThreadedDispatcher()) {
                var error = Assert.Throws<InvalidOperationException>(() => dispatcher.Send(() => throw new InvalidOperationException("boom")));
                Assert.Equal("boom", error.Message);
            }
        }

        [Fact]
        public void Send_Timeout_DoesNotBreakDispatcher() {
            using(var dispatcher = new SingleThreadedDispatcher()) {
                using(var gate = new ManualResetEventSlim(false)) {
                    Assert.Throws<TimeoutException>(() => dispatcher.Send(() => gate.Wait(TimeSpan.FromSeconds(10)), TimeSpan.FromMilliseconds(50)));
                    gate.Set();
                }
                Assert.Equal(42, dispatcher.Invoke(() => 42, TimeSpan.FromSeconds(10)));
            }
        }

        [Fact]
        public void InvokeAsync_ReturnsValue() {
            using(var dispatcher = new SingleThreadedDispatcher()) {
                var task = dispatcher.InvokeAsync(() => 42);
                Assert.Equal(42, task.Result);
                Assert.Equal(dispatcher.ThreadId, dispatcher.Invoke(() => Thread.CurrentThread.ManagedThreadId));
            }
        }

        [Fact]
        public void Post_ReportsUnhandledException() {
            using(var dispatcher = new SingleThreadedDispatcher()) {
                Exception captured = null;
                dispatcher.UnhandledException += error => captured = error;
                dispatcher.Post(() => throw new InvalidOperationException("post failure"));
                Assert.True(Wait.Until(() => captured != null));
                Assert.IsType<InvalidOperationException>(captured);
            }
        }

        [Fact]
        public void UseMessagePump_False_Works() {
            using(var dispatcher = new SingleThreadedDispatcher(new SingleThreadedDispatcherOptions { UseMessagePump = false })) {
                Assert.Equal(7, dispatcher.Invoke(() => 7));
                Assert.Equal(ApartmentState.STA, dispatcher.Invoke(() => Thread.CurrentThread.GetApartmentState()));
            }
        }

        [Fact]
        public void Dispose_StopsThread_AndRejectsCalls() {
            var dispatcher = new SingleThreadedDispatcher();
            var threadId = dispatcher.ThreadId;
            dispatcher.Dispose();
            Assert.Throws<ObjectDisposedException>(() => dispatcher.Send(() => { }));
            dispatcher.Dispose();
            Assert.NotEqual(0, threadId);
        }

        [Fact]
        public void Dispose_FromDispatcherThread_DoesNotDeadlock() {
            var dispatcher = new SingleThreadedDispatcher();
            dispatcher.Send(() => dispatcher.Dispose());
            Assert.Throws<ObjectDisposedException>(() => dispatcher.Send(() => { }));
        }

        [Fact]
        public void Send_FromAnotherThread_BlocksUntilCompleted() {
            using(var dispatcher = new SingleThreadedDispatcher()) {
                var completed = false;
                var thread = new Thread(() => {
                    dispatcher.Send(() => completed = true);
                    Assert.True(completed);
                });
                thread.Start();
                Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
            }
        }

        [Fact]
        public void NegativeQueueLimit_IsRejected() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SingleThreadedDispatcher(new SingleThreadedDispatcherOptions { MaxQueueLength = -1 }));
        }

        [Fact]
        public void QueueOverflow_Throws() {
            using(var dispatcher = new SingleThreadedDispatcher(new SingleThreadedDispatcherOptions { MaxQueueLength = 1 })) {
                using(var gate = new ManualResetEventSlim(false))
                using(var started = new ManualResetEventSlim(false)) {
                    var worker = new Thread(() => dispatcher.Send(() => {
                        started.Set();
                        gate.Wait(TimeSpan.FromSeconds(10));
                    }, TimeSpan.FromSeconds(20)));
                    worker.Start();
                    Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
                    dispatcher.Post(() => { });
                    Assert.Throws<InvalidOperationException>(() => dispatcher.Post(() => { }));
                    gate.Set();
                    Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
                }
            }
        }

        [Fact]
        public async Task InvokeAsync_Exception_IsPropagated() {
            using(var dispatcher = new SingleThreadedDispatcher()) {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.InvokeAsync<int>(() => throw new InvalidOperationException("async failure")));
                Assert.Equal("async failure", error.Message);
            }
        }

        [Fact]
        public void InvokeAsync_CancelledBeforeExecution_IsCancelled() {
            using(var dispatcher = new SingleThreadedDispatcher()) {
                using(var gate = new ManualResetEventSlim(false))
                using(var started = new ManualResetEventSlim(false))
                using(var source = new CancellationTokenSource()) {
                    var worker = new Thread(() => dispatcher.Send(() => {
                        started.Set();
                        gate.Wait(TimeSpan.FromSeconds(10));
                    }, TimeSpan.FromSeconds(20)));
                    worker.Start();
                    Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
                    var task = dispatcher.InvokeAsync(() => 1, source.Token);
                    source.Cancel();
                    gate.Set();
                    Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
                    Assert.ThrowsAny<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                }
            }
        }

        [Fact]
        public void InvokeAsync_OnDispatcherThread_CompletesInline() {
            using(var dispatcher = new SingleThreadedDispatcher()) {
                dispatcher.Send(() => {
                    var task = dispatcher.InvokeAsync(() => 5);
                    Assert.True(task.IsCompleted);
                    Assert.Equal(5, task.Result);
                });
            }
        }

#if NET8_0_OR_GREATER

        [Fact]
        public async Task DisposeAsync_StopsThread() {
            var dispatcher = new SingleThreadedDispatcher();
            await dispatcher.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => dispatcher.Send(() => { }));
        }

#endif
    }
}
