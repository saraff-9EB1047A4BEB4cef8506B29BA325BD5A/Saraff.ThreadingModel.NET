using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Saraff.IoC;
using Saraff.ThreadingModel.Messaging;

namespace Saraff.ThreadingModel.DependencyInjection {

    /// <summary>
    /// Регистрация компонентов Saraff.ThreadingModel в контейнере Saraff.IoC.
    /// Registration helpers for Saraff.IoC.
    /// </summary>
    public static class SaraffIoCExtensions {
        private static readonly ConditionalWeakTable<IServiceRegistry, QueueMessageHandlerRegistry> Registries = new ConditionalWeakTable<IServiceRegistry, QueueMessageHandlerRegistry>();

        /// <summary>
        /// Регистрирует диспетчер (singleton, по умолчанию без имени). Registers a dispatcher as a named singleton.
        /// </summary>
        /// <param name="registry">Реестр сервисов. Service registry.</param>
        /// <param name="name">Имя регистрации. Registration name.</param>
        /// <param name="configure">Настройка диспетчера. Dispatcher configuration.</param>
        public static void RegisterSingleThreadedDispatcher(this IServiceRegistry registry, string? name = null, Action<SingleThreadedDispatcherOptions>? configure = null) {
            if(registry == null) {
                throw new ArgumentNullException(nameof(registry));
            }
            var options = new SingleThreadedDispatcherOptions();
            configure?.Invoke(options);
            registry.Register<SingleThreadedDispatcher>(provider => new SingleThreadedDispatcher(options, provider), ServiceLifetime.Singleton, name);
            GetOrCreateHandlerRegistry(registry);
        }

        /// <summary>
        /// Регистрирует обработчик сообщений очереди. Registers a queue message handler.
        /// </summary>
        /// <typeparam name="TMessage">Тип сообщения. Message type.</typeparam>
        /// <typeparam name="THandler">Тип обработчика. Handler type.</typeparam>
        /// <param name="registry">Реестр сервисов. Service registry.</param>
        /// <param name="lifetime">Время жизни. Lifetime.</param>
        public static void RegisterQueueMessageHandler<TMessage, THandler>(this IServiceRegistry registry, ServiceLifetime lifetime = ServiceLifetime.Transient)
            where THandler : class, IQueueMessageHandler<TMessage> {
            if(registry == null) {
                throw new ArgumentNullException(nameof(registry));
            }
            var serviceType = typeof(IQueueMessageHandler<TMessage>);
            var name = GetRegistrationName(typeof(THandler));
            registry.Register(serviceType, typeof(THandler), lifetime, name);
            GetOrCreateHandlerRegistry(registry).Register(typeof(TMessage), serviceType, name);
        }

        /// <summary>
        /// Регистрирует обработчик ошибок очереди. Registers a queue error handler.
        /// </summary>
        /// <typeparam name="THandler">Тип обработчика. Handler type.</typeparam>
        /// <param name="registry">Реестр сервисов. Service registry.</param>
        /// <param name="lifetime">Время жизни. Lifetime.</param>
        public static void RegisterQueueErrorHandler<THandler>(this IServiceRegistry registry, ServiceLifetime lifetime = ServiceLifetime.Transient)
            where THandler : class, IQueueMessageErrorHandler {
            if(registry == null) {
                throw new ArgumentNullException(nameof(registry));
            }
            registry.Register<IQueueMessageErrorHandler, THandler>(lifetime, GetRegistrationName(typeof(THandler)));
        }

        /// <summary>
        /// Регистрирует прокси, привязанный к именованному диспетчеру. Registers a proxy bound to the named dispatcher.
        /// </summary>
        /// <typeparam name="TService">Тип сервиса (интерфейс). Service type (interface).</typeparam>
        /// <typeparam name="TImplementation">Тип реализации. Implementation type.</typeparam>
        /// <param name="registry">Реестр сервисов. Service registry.</param>
        /// <param name="dispatcherName">Имя диспетчера. Dispatcher name.</param>
        /// <param name="lifetime">Время жизни. Lifetime.</param>
        /// <param name="args">Аргументы конструктора. Constructor arguments.</param>
        public static void RegisterSingleThreadedProxy<TService, TImplementation>(this IServiceRegistry registry, string? dispatcherName = null, ServiceLifetime lifetime = ServiceLifetime.Singleton, params object[] args)
            where TService : class
            where TImplementation : class, TService {
            if(registry == null) {
                throw new ArgumentNullException(nameof(registry));
            }
            registry.Register<TService>(provider => {
                var dispatcher = ResolveDispatcher(provider, dispatcherName);
                var proxy = new SingleThreadedProxy(typeof(TService), typeof(TImplementation), dispatcher, args);
                return (TService)proxy.GetTransparentProxy<TService>();
            }, lifetime);
        }

        private static SingleThreadedDispatcher ResolveDispatcher(IServiceProvider provider, string? name) {
            if(string.IsNullOrEmpty(name)) {
                var dispatcher = provider.GetService(typeof(SingleThreadedDispatcher)) as SingleThreadedDispatcher;
                if(dispatcher == null) {
                    throw new InvalidOperationException("Диспетчер не зарегистрирован. Вызовите RegisterSingleThreadedDispatcher.");
                }
                return dispatcher;
            }
            if(provider is IServiceContainer container) {
                var named = container.ResolveNamed(typeof(SingleThreadedDispatcher), name) as SingleThreadedDispatcher;
                if(named == null) {
                    throw new InvalidOperationException("Диспетчер с именем '" + name + "' не зарегистрирован.");
                }
                return named;
            }
            throw new InvalidOperationException("Именованный диспетчер требует IServiceContainer.");
        }

        private static string GetRegistrationName(Type type) {
            return type.FullName ?? type.Name;
        }

        private static QueueMessageHandlerRegistry GetOrCreateHandlerRegistry(IServiceRegistry registry) {
            return Registries.GetValue(registry, current => {
                if(current is IServiceContainer container && container.IsRegistered(typeof(QueueMessageHandlerRegistry))) {
                    return (QueueMessageHandlerRegistry)container.Resolve(typeof(QueueMessageHandlerRegistry))!;
                }
                var created = new QueueMessageHandlerRegistry();
                current.RegisterInstance(created);
                return created;
            });
        }
    }
}
