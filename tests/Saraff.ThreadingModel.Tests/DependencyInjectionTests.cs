using System;
using System.Threading;
using System.Threading.Tasks;
using Saraff.IoC;
using Saraff.ThreadingModel.DependencyInjection;
using Xunit;

namespace Saraff.ThreadingModel.Tests {

    public sealed class DependencyInjectionTests {

        [Fact]
        public void NamedDispatchers_AreDistinct() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("first");
                container.RegisterSingleThreadedDispatcher("second");
                var first = container.ResolveNamed<SingleThreadedDispatcher>("first");
                var second = container.ResolveNamed<SingleThreadedDispatcher>("second");
                Assert.NotSame(first, second);
                Assert.NotEqual(first.ThreadId, second.ThreadId);
            }
        }

        [Fact]
        public void Proxy_UsesNamedDispatcher() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("scanner");
                container.RegisterSingleThreadedProxy<IScanner, Scanner>("scanner");
                var scanner = container.Resolve<IScanner>();
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("scanner");
                Assert.Equal(dispatcher.ThreadId, scanner.ThreadId);
            }
        }

        [Fact]
        public void Proxy_WithoutName_UsesDefaultDispatcher() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher();
                container.RegisterSingleThreadedProxy<IScanner, Scanner>();
                var scanner = container.Resolve<IScanner>();
                var dispatcher = container.Resolve<SingleThreadedDispatcher>();
                Assert.Equal(dispatcher.ThreadId, scanner.ThreadId);
            }
        }

        [Fact]
        public void Proxy_IsSingleton_ByDefault() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher();
                container.RegisterSingleThreadedProxy<IScanner, Scanner>();
                Assert.Same(container.Resolve<IScanner>(), container.Resolve<IScanner>());
            }
        }

        [Fact]
        public void ContainerDispose_DisposesDispatcher() {
            var container = new ServiceContainer();
            container.RegisterSingleThreadedDispatcher("scanner");
            var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("scanner");
            container.Dispose();
            Assert.Throws<ObjectDisposedException>(() => dispatcher.Send(() => { }));
        }

        [Fact]
        public void UnknownNamedDispatcher_IsRejected() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("known");
                container.RegisterSingleThreadedProxy<IScanner, Scanner>("unknown");
                Assert.Throws<InvalidOperationException>(() => container.Resolve<IScanner>());
            }
        }

        [Fact]
        public void TransientHandler_IsCreatedPerMessage() {
            Recorder.Reset();
            ScopedState.Reset();
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("queue");
                container.Register<ScopedState, ScopedState>(ServiceLifetime.Scoped);
                container.RegisterQueueMessageHandler<ScanMessage, RecordingScanHandler>();
                var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("queue");
                Assert.True(Task.WaitAll(new[] {
                    dispatcher.PostAsync(new ScanMessage(1)),
                    dispatcher.PostAsync(new ScanMessage(2))
                }, TimeSpan.FromSeconds(15)));
                Assert.Equal(2, Recorder.Snapshot().Length);
                Assert.Equal(2, ScopedState.DisposedCount);
            }
        }

        [Fact]
        public void Proxy_TransientLifetime_CreatesNewInstances() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher();
                container.RegisterSingleThreadedProxy<IScanner, Scanner>(null, ServiceLifetime.Transient);
                Assert.NotSame(container.Resolve<IScanner>(), container.Resolve<IScanner>());
            }
        }

        [Fact]
        public void Proxy_WithConstructorArguments() {
            using(var container = new ServiceContainer()) {
                container.RegisterSingleThreadedDispatcher("ping");
                container.RegisterSingleThreadedProxy<IPing, Ping>("ping", ServiceLifetime.Singleton, "hello");
                Assert.Equal("hello", container.Resolve<IPing>().Value);
            }
        }

        [Fact]
        public void Proxy_DisposedWithContainer() {
            var container = new ServiceContainer();
            container.RegisterSingleThreadedDispatcher("scanner");
            container.RegisterSingleThreadedProxy<IScanner, Scanner>("scanner");
            var scanner = container.Resolve<IScanner>();
            container.Dispose();
            Assert.Throws<ObjectDisposedException>(() => scanner.Name);
        }
    }
}
