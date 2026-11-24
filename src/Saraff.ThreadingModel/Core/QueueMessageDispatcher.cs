using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Saraff.IoC;
using Saraff.ThreadingModel.Messaging;

namespace Saraff.ThreadingModel.Core {

    internal sealed class QueueMessageDispatcher {
        private readonly SingleThreadedDispatcher _dispatcher;
        private readonly IServiceProvider _services;
        private readonly SingleThreadedDispatcherOptions _options;
        private QueueMessageHandlerRegistry? _registry;

        public QueueMessageDispatcher(SingleThreadedDispatcher dispatcher, IServiceProvider services) {
            this._dispatcher = dispatcher;
            this._services = services;
            this._options = dispatcher.Options;
        }

        public void Process(object message, CancellationToken cancellationToken) {
            if(cancellationToken.IsCancellationRequested) {
                return;
            }
            var registry = this.Registry;
            var registrations = registry == null ? Array.Empty<QueueMessageHandlerRegistration>() : registry.GetRegistrations(message.GetType());
            if(registrations.Count == 0) {
                if(!this._options.IgnoreUnhandledMessages) {
                    this.ReportError(message, new InvalidOperationException("Не зарегистрирован обработчик для сообщения " + message.GetType().FullName + "."));
                }
                return;
            }
            IServiceScope? scope = null;
            try {
                if(this._services is IServiceContainer container) {
                    scope = container.CreateScope();
                }
                var provider = scope == null ? this._services : scope.ServiceProvider;
                foreach(var registration in registrations) {
                    if(cancellationToken.IsCancellationRequested) {
                        return;
                    }
                    var handler = ResolveHandler(provider, registration);
                    if(handler == null) {
                        throw new InvalidOperationException("Сервис " + registration.ServiceType.FullName + " не зарегистрирован в контейнере.");
                    }
                    var task = MessageHandlerInvoker.Invoke(registration.ServiceType, handler, message, cancellationToken);
                    this._dispatcher.AwaitWithPump(task, this._options.HandlerTimeout);
                }
            } catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
            } catch(Exception ex) {
                this.ReportError(message, ex);
                if(!this._options.ContinueOnHandlerError) {
                    throw;
                }
            } finally {
                scope?.Dispose();
            }
        }

        private QueueMessageHandlerRegistry? Registry {
            get {
                if(this._registry == null) {
                    this._registry = this._services.GetService(typeof(QueueMessageHandlerRegistry)) as QueueMessageHandlerRegistry;
                }
                return this._registry;
            }
        }

        private static object? ResolveHandler(IServiceProvider provider, QueueMessageHandlerRegistration registration) {
            if(registration.Name == null) {
                return provider.GetService(registration.ServiceType);
            }
            if(provider is IServiceContainer container) {
                return container.ResolveNamed(registration.ServiceType, registration.Name);
            }
            return provider.GetService(registration.ServiceType);
        }

        private void ReportError(object message, Exception error) {
            var handled = false;
            var handlers = this._services.GetService(typeof(IEnumerable<IQueueMessageErrorHandler>)) as IEnumerable<IQueueMessageErrorHandler>;
            if(handlers != null) {
                foreach(var handler in handlers) {
                    handled = true;
                    try {
                        this._dispatcher.AwaitWithPump(handler.HandleAsync(message, error, CancellationToken.None), this._options.HandlerTimeout);
                    } catch(Exception ex) {
                        this._dispatcher.ReportUnhandled(ex);
                    }
                }
            }
            if(!handled) {
                this._dispatcher.ReportUnhandled(error);
            }
        }
    }

    internal static class MessageHandlerInvoker {
        private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, Func<object, object, CancellationToken, Task>>> Invokers = new ConcurrentDictionary<Type, ConcurrentDictionary<Type, Func<object, object, CancellationToken, Task>>>();

        public static Task Invoke(Type serviceType, object handler, object message, CancellationToken cancellationToken) {
            var messageType = serviceType.GetGenericArguments()[0];
            var byService = Invokers.GetOrAdd(serviceType, _ => new ConcurrentDictionary<Type, Func<object, object, CancellationToken, Task>>());
            var invoker = byService.GetOrAdd(messageType, _ => Create(serviceType, messageType));
            return invoker(handler, message, cancellationToken);
        }

        private static Func<object, object, CancellationToken, Task> Create(Type serviceType, Type messageType) {
            var handlerParameter = Expression.Parameter(typeof(object), "handler");
            var messageParameter = Expression.Parameter(typeof(object), "message");
            var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
            var method = serviceType.GetMethod("HandleAsync");
            if(method == null) {
                throw new InvalidOperationException("Метод HandleAsync не найден в " + serviceType.FullName + ".");
            }
            var call = Expression.Call(
                Expression.Convert(handlerParameter, serviceType),
                method,
                Expression.Convert(messageParameter, messageType),
                cancellationTokenParameter);
            return Expression.Lambda<Func<object, object, CancellationToken, Task>>(call, handlerParameter, messageParameter, cancellationTokenParameter).Compile();
        }
    }
}
