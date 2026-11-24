using System;
using System.Threading;
using System.Threading.Tasks;
using Saraff.IoC;
using Saraff.ThreadingModel.DependencyInjection;
using Xunit;

namespace Saraff.ThreadingModel.Tests {

    public sealed class QueueTests {

        [Fact]
        public void Handler_ReceivesMessage_OnDispatcherThread() {
            Recorder.Reset();
            ScopedState.Reset();
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue");
                container.Register<ScopedState, ScopedState>(ServiceLifetime.Scoped);
                container.RegisterQueueMessageHandler<ScanMessage, RecordingScanHandler>();
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                Assert.True(dispatcher.PostAsync(new ScanMessage(4)).Wait(TimeSpan.FromSeconds(15)));
                var operations = Recorder.Snapshot();
                Assert.Single(operations);
                Assert.Equal(4, operations[0].Pages);
                Assert.Equal(dispatcher.ThreadId, operations[0].ThreadId);
                Assert.Equal(1, ScopedState.DisposedCount);
            }
        }

        [Fact]
        public void Messages_AreProcessedInOrder() {
            Recorder.Reset();
            ScopedState.Reset();
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue");
                container.Register<ScopedState, ScopedState>(ServiceLifetime.Scoped);
                container.RegisterQueueMessageHandler<ScanMessage, RecordingScanHandler>();
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                var tasks = new Task[] {
                    dispatcher.PostAsync(new ScanMessage(1)),
                    dispatcher.PostAsync(new ScanMessage(2)),
                    dispatcher.PostAsync(new ScanMessage(3))
                };
                Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(15)));
                var operations = Recorder.Snapshot();
                Assert.Equal(3, operations.Length);
                Assert.Equal(1, operations[0].Pages);
                Assert.Equal(2, operations[1].Pages);
                Assert.Equal(3, operations[2].Pages);
            }
        }

        [Fact]
        public void MessageWithoutHandler_IsReported() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue");
                var errors = new RecordingErrorHandler();
                container.RegisterInstance<IQueueMessageErrorHandler>(errors);
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                dispatcher.Post(new UnhandledMessage());
                Assert.True(Wait.Until(() => errors.Errors.Length == 1));
                Assert.IsType<InvalidOperationException>(errors.Errors[0]);
            }
        }

        [Fact]
        public void MessageWithoutHandler_CanBeIgnored() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue", options => options.IgnoreUnhandledMessages = true);
                var errors = new RecordingErrorHandler();
                container.RegisterInstance<IQueueMessageErrorHandler>(errors);
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                Assert.True(dispatcher.PostAsync(new UnhandledMessage()).Wait(TimeSpan.FromSeconds(15)));
                Assert.Empty(errors.Errors);
            }
        }

        [Fact]
        public void HandlerError_IsReported_AndQueueContinues() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue");
                var errors = new RecordingErrorHandler();
                container.RegisterInstance<IQueueMessageErrorHandler>(errors);
                container.RegisterQueueMessageHandler<UnhandledMessage, ThrowingHandler>();
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                Assert.True(dispatcher.PostAsync(new UnhandledMessage()).Wait(TimeSpan.FromSeconds(15)));
                dispatcher.Post(new UnhandledMessage());
                Assert.True(Wait.Until(() => errors.Errors.Length == 2));
                Assert.All(errors.Errors, error => Assert.Equal("handler failure", error.Message));
            }
        }

        [Fact]
        public void HandlerTimeout_IsReported() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue", options => options.HandlerTimeout = TimeSpan.FromMilliseconds(100));
                var errors = new RecordingErrorHandler();
                container.RegisterInstance<IQueueMessageErrorHandler>(errors);
                container.RegisterQueueMessageHandler<SlowMessage, SlowHandler>();
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                dispatcher.Post(new SlowMessage());
                Assert.True(Wait.Until(() => errors.Errors.Length == 1));
                Assert.IsType<TimeoutException>(errors.Errors[0]);
            }
        }

        [Fact]
        public void Post_WithoutServiceProvider_Throws() {
            using(var dispatcher = new SingleThreadedDispatcher()) {
                Assert.Throws<InvalidOperationException>(() => dispatcher.Post(new ScanMessage(1)));
            }
        }

        [Fact]
        public void Post_AfterDispose_Throws() {
            var dispatcher = new SingleThreadedDispatcher();
            dispatcher.Dispose();
            Assert.Throws<ObjectDisposedException>(() => dispatcher.Post(new ScanMessage(1)));
        }

        [Fact]
        public void BaseMessageHandler_ReceivesDerivedMessage() {
            BaseMessageHandler.Received = 0;
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue");
                container.RegisterQueueMessageHandler<BaseMessage, BaseMessageHandler>(ServiceLifetime.Singleton);
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                Assert.True(dispatcher.PostAsync(new DerivedMessage()).Wait(TimeSpan.FromSeconds(15)));
                Assert.Equal(1, BaseMessageHandler.Received);
            }
        }

        [Fact]
        public void ExactAndBaseHandlers_AreBothInvoked() {
            BaseMessageHandler.Received = 0;
            DerivedMessageHandler.Received = 0;
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue");
                container.RegisterQueueMessageHandler<BaseMessage, BaseMessageHandler>(ServiceLifetime.Singleton);
                container.RegisterQueueMessageHandler<DerivedMessage, DerivedMessageHandler>(ServiceLifetime.Singleton);
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                Assert.True(dispatcher.PostAsync(new DerivedMessage()).Wait(TimeSpan.FromSeconds(15)));
                Assert.Equal(1, BaseMessageHandler.Received);
                Assert.Equal(1, DerivedMessageHandler.Received);
            }
        }

        [Fact]
        public void FatalHandlerError_RaisesUnhandledException() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue", options => options.ContinueOnHandlerError = false);
                container.RegisterQueueMessageHandler<UnhandledMessage, ThrowingHandler>();
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                Exception captured = null;
                dispatcher.UnhandledException += error => captured = error;
                var task = dispatcher.PostAsync(new UnhandledMessage());
                Assert.True(Wait.Until(() => task.IsCompleted));
                Assert.True(task.IsFaulted);
                Assert.True(Wait.Until(() => captured != null));
                Assert.Equal("handler failure", captured.Message);
            }
        }

        [Fact]
        public void ErrorHandlerFailure_IsReported() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue");
                container.RegisterInstance<IQueueMessageErrorHandler>(new ThrowingErrorHandler());
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                Exception captured = null;
                dispatcher.UnhandledException += error => captured = error;
                dispatcher.Post(new UnhandledMessage());
                Assert.True(Wait.Until(() => captured != null));
                Assert.Equal("error handler failure", captured.Message);
            }
        }

        [Fact]
        public void MultipleErrorHandlers_AreAllInvoked() {
            FirstErrorHandler.Count = 0;
            SecondErrorHandler.Count = 0;
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue");
                container.RegisterQueueErrorHandler<FirstErrorHandler>(ServiceLifetime.Singleton);
                container.RegisterQueueErrorHandler<SecondErrorHandler>(ServiceLifetime.Singleton);
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                dispatcher.Post(new UnhandledMessage());
                Assert.True(Wait.Until(() => FirstErrorHandler.Count == 1 && SecondErrorHandler.Count == 1), "first=" + FirstErrorHandler.Count + ", second=" + SecondErrorHandler.Count);
            }
        }

        [Fact]
        public void PostAsync_WithCancelledToken_IsCancelled() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue");
                container.Register<ScopedState, ScopedState>(ServiceLifetime.Scoped);
                container.RegisterQueueMessageHandler<ScanMessage, RecordingScanHandler>();
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                var task = dispatcher.PostAsync(new ScanMessage(1), new CancellationToken(true));
                Assert.ThrowsAny<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            }
        }
    }
}
