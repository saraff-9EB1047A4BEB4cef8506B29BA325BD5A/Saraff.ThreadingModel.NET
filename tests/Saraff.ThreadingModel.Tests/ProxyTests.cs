using System;
using System.Threading;
using Xunit;

namespace Saraff.ThreadingModel.Tests {

    public sealed class ProxyTests {

        [Fact]
        public void Calls_AreMarshalledToStaThread() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                Assert.Equal("scanner", scanner.Name);
                Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, scanner.ThreadId);
                Assert.Equal(ApartmentState.STA, scanner.Apartment);
            }
        }

        [Fact]
        public void Calls_FromMultipleThreads_AreSerialized() {
            var target = new Scanner();
            using(var proxy = new SingleThreadedProxy(target)) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                var threads = new Thread[4];
                for(var i = 0; i < threads.Length; i++) {
                    threads[i] = new Thread(() => {
                        for(var j = 0; j < 25; j++) {
                            scanner.Scan(1);
                        }
                    });
                    threads[i].Start();
                }
                foreach(var thread in threads) {
                    Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
                }
                Assert.Equal(1, target.MaxActive);
            }
        }

        [Fact]
        public void RefOut_Parameters_AreMarshalled() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                var value = 10;
                scanner.Update(ref value, out var doubled);
                Assert.Equal(11, value);
                Assert.Equal(22, doubled);
            }
        }

        [Fact]
        public void Exception_IsPropagated() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                var error = Assert.Throws<InvalidOperationException>(() => scanner.Fail());
                Assert.Equal("failure", error.Message);
            }
        }

        [Fact]
        public void ConstructorException_IsPropagated_WithoutHanging() {
            using(var proxy = new SingleThreadedProxy(typeof(IBroken), typeof(BrokenCtor))) {
                var broken = proxy.GetTransparentProxy<IBroken>();
                var error = Assert.Throws<InvalidOperationException>(() => broken.Ping());
                Assert.Equal("ctor failure", error.Message);
            }
        }

        [Fact]
        public void Dispose_RejectsCalls() {
            var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner));
            var scanner = proxy.GetTransparentProxy<IScanner>();
            Assert.Equal("scanner", scanner.Name);
            proxy.Dispose();
            Assert.Throws<ObjectDisposedException>(() => scanner.Name);
        }

        [Fact]
        public void PropertiesAndGenericMethods_AreProxied() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                scanner.Zoom = 5;
                Assert.Equal(5, scanner.Zoom);
                Assert.Equal(123, scanner.Echo(123));
                Assert.Equal("text", scanner.Echo("text"));
            }
        }

        [Fact]
        public void ConstructorArguments_AreUsed() {
            using(var proxy = new SingleThreadedProxy(typeof(IPing), typeof(Ping), null, "pong")) {
                var ping = proxy.GetTransparentProxy<IPing>();
                Assert.Equal("pong", ping.Value);
            }
        }

        [Fact]
        public void SharedDispatcher_IsNotDisposedWithProxy() {
            using(var dispatcher = new SingleThreadedDispatcher())
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner), dispatcher)) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                Assert.Equal(dispatcher.ThreadId, scanner.ThreadId);
                proxy.Dispose();
                Assert.Equal(1, dispatcher.Invoke(() => 1));
            }
        }

        [Fact]
        public void InvokeTimeout_IsApplied() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                proxy.InvokeTimeout = TimeSpan.FromMilliseconds(50);
                Assert.Throws<TimeoutException>(() => scanner.Sleep(500));
                proxy.InvokeTimeout = TimeSpan.FromSeconds(10);
                Assert.Equal("scanner", scanner.Name);
            }
        }

        [Fact]
        public void RemoveUnsubscribedHandler_IsPassThrough() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                scanner.Completed -= (sender, args) => { };
                Assert.Equal(0, scanner.CompletedSubscribers);
            }
        }

#if NET8_0_OR_GREATER

        [Fact]
        public void InterfaceAmbiguity_RequiresExplicitServiceType() {
            Assert.Throws<NotSupportedException>(() => new SingleThreadedProxy(typeof(TwoInterfaces)));
        }

#else

        [Fact]
        public void NonMarshalByRefType_IsRejectedByRealProxy() {
            Assert.Throws<ArgumentException>(() => new SingleThreadedProxy(typeof(TwoInterfaces)));
        }

#endif
    }
}
