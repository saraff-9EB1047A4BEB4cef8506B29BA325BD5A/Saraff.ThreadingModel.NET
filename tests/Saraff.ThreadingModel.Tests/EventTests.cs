using System;
using System.Collections.Concurrent;
using System.Threading;
using Xunit;

namespace Saraff.ThreadingModel.Tests {

    public sealed class EventTests {

        [Fact]
        public void AddRemove_AreForwardedToTarget() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                EventHandler<ScanEventArgs> first = (sender, args) => { };
                EventHandler<ScanEventArgs> second = (sender, args) => { };
                scanner.Completed += first;
                scanner.Completed += second;
                Assert.Equal(1, scanner.CompletedSubscribers);
                scanner.Completed -= first;
                Assert.Equal(1, scanner.CompletedSubscribers);
                scanner.Completed -= second;
                Assert.Equal(0, scanner.CompletedSubscribers);
            }
        }

        [Fact]
        public void Event_IsForwardedToSubscriber() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                var pages = 0;
                scanner.Completed += (sender, args) => pages = args.Pages;
                scanner.Scan(7);
                Assert.Equal(7, pages);
            }
        }

        [Fact]
        public void Event_NullSender_IsAccepted() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                var raised = false;
                scanner.Completed += (sender, args) => raised = sender == null;
                scanner.RaiseWithNullSender();
                Assert.True(raised);
            }
        }

        [Fact]
        public void Event_NonVoidDelegate_ReturnsValue() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                scanner.Transform += value => value * 2;
                Assert.Equal(12, scanner.Scan(6));
            }
        }

        [Fact]
        public void Event_SubscriberContext_IsUsed() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                var context = new RecordingContext();
                var previous = SynchronizationContext.Current;
                try {
                    SynchronizationContext.SetSynchronizationContext(context);
                    scanner.Completed += (sender, args) => { };
                } finally {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
                scanner.RaiseCompleted(1);
                Assert.True(context.SendCount >= 1);
            }
        }

        [Fact]
        public void Event_HandlerException_IsPropagated() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                scanner.Completed += (sender, args) => throw new InvalidOperationException("handler failure");
                var error = Assert.Throws<InvalidOperationException>(() => scanner.RaiseCompleted(1));
                Assert.Equal("handler failure", error.Message);
            }
        }

        [Fact]
        public void Event_Handler_CanCallProxyAgain() {
            using(var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
                var scanner = proxy.GetTransparentProxy<IScanner>();
                var reentered = 0;
                scanner.Completed += (sender, args) => {
                    if(args.Pages == 1) {
                        reentered = scanner.Scan(5);
                    }
                };
                scanner.RaiseCompleted(1);
                Assert.Equal(5, reentered);
            }
        }

        [Fact]
        public void Event_WithRefParameter_IsNotSupported() {
            using(var proxy = new SingleThreadedProxy(typeof(IRefEventSource), typeof(RefEventSource))) {
                var source = proxy.GetTransparentProxy<IRefEventSource>();
                Assert.Throws<NotSupportedException>(() => source.Changed += (ref int value) => { });
            }
        }

        [Fact]
        public void Events_AfterProxyDispose_AreIgnored() {
            var target = new Scanner();
            var proxy = new SingleThreadedProxy(target);
            var scanner = proxy.GetTransparentProxy<IScanner>();
            var count = 0;
            scanner.Completed += (sender, args) => count++;
            scanner.RaiseCompleted(1);
            Assert.Equal(1, count);
            proxy.Dispose();
            target.RaiseCompleted(2);
            Assert.Equal(1, count);
        }
    }
}
