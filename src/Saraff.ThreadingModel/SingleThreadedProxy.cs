using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using Saraff.ThreadingModel.Core;
#if NETFRAMEWORK
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
#endif

namespace Saraff.ThreadingModel {

    /// <summary>
    /// Прокси, который маршализует все вызовы в <see cref="SingleThreadedDispatcher"/> и перенаправляет события подписчикам.
    /// A proxy that marshals every call to a <see cref="SingleThreadedDispatcher"/> and forwards events to subscribers.
    /// </summary>
    public sealed class SingleThreadedProxy
#if NETFRAMEWORK
        : RealProxy, IDisposable
#else
        : IDisposable
#endif
    {

        private readonly object _eventsSync = new object();
        private readonly Dictionary<string, EventAdapter> _events = new Dictionary<string, EventAdapter>();
        private Type _implementationType = typeof(object);
        private object?[] _activationArgs = Array.Empty<object>();
        private SingleThreadedDispatcher _dispatcher = null!;
        private bool _ownsDispatcher;
#if NET8_0_OR_GREATER
        private object _transparentProxy = null!;
#endif
        private object? _instance;
        private bool _disposed;

        /// <summary>
        /// Создаёт прокси для существующего экземпляра. Creates a proxy for an existing instance.
        /// </summary>
        /// <param name="instance">Целевой объект. Target instance.</param>
        public SingleThreadedProxy(object instance)
#if NETFRAMEWORK
            : base(GetRemoteInstanceType(instance))
#endif
        {
            var instanceType = GetRemoteInstanceType(instance);
            this.Initialize(ResolveServiceType(instanceType), instanceType, instance, null, null);
        }

        /// <summary>
        /// Создаёт прокси, который создаст экземпляр указанного типа. Creates a proxy that activates the specified type.
        /// </summary>
        /// <param name="type">Тип целевого объекта. Target type.</param>
        public SingleThreadedProxy(Type type)
#if NETFRAMEWORK
            : base(GetRemoteType(type))
#endif
        {
            EnsureActivatable(type);
            this.Initialize(ResolveServiceType(type), type, null, null, null);
        }

        /// <summary>
        /// Создаёт прокси, который создаст экземпляр указанного типа с аргументами конструктора. Creates a proxy that activates the type with constructor arguments.
        /// </summary>
        /// <param name="type">Тип целевого объекта. Target type.</param>
        /// <param name="args">Аргументы конструктора. Constructor arguments.</param>
        public SingleThreadedProxy(Type type, params object[] args)
#if NETFRAMEWORK
            : base(GetRemoteType(type))
#endif
        {
            EnsureActivatable(type);
            this.Initialize(ResolveServiceType(type), type, null, args, null);
        }

        /// <summary>
        /// Создаёт прокси для экземпляра с указанным диспетчером. Creates a proxy for an instance using the specified dispatcher.
        /// </summary>
        /// <param name="instance">Целевой объект. Target instance.</param>
        /// <param name="dispatcher">Диспетчер. Dispatcher.</param>
        public SingleThreadedProxy(object instance, SingleThreadedDispatcher dispatcher)
#if NETFRAMEWORK
            : base(GetRemoteInstanceType(instance))
#endif
        {
            var instanceType = GetRemoteInstanceType(instance);
            this.Initialize(ResolveServiceType(instanceType), instanceType, instance, null, dispatcher);
        }

        /// <summary>
        /// Создаёт прокси для типа с указанным диспетчером. Creates a proxy for a type using the specified dispatcher.
        /// </summary>
        /// <param name="type">Тип целевого объекта. Target type.</param>
        /// <param name="dispatcher">Диспетчер. Dispatcher.</param>
        public SingleThreadedProxy(Type type, SingleThreadedDispatcher dispatcher)
#if NETFRAMEWORK
            : base(GetRemoteType(type))
#endif
        {
            EnsureActivatable(type);
            this.Initialize(ResolveServiceType(type), type, null, null, dispatcher);
        }

        /// <summary>
        /// Создаёт прокси для типа с диспетчером и аргументами конструктора. Creates a proxy for a type using the dispatcher and constructor arguments.
        /// </summary>
        /// <param name="type">Тип целевого объекта. Target type.</param>
        /// <param name="dispatcher">Диспетчер. Dispatcher.</param>
        /// <param name="args">Аргументы конструктора. Constructor arguments.</param>
        public SingleThreadedProxy(Type type, SingleThreadedDispatcher dispatcher, params object[] args)
#if NETFRAMEWORK
            : base(GetRemoteType(type))
#endif
        {
            EnsureActivatable(type);
            this.Initialize(ResolveServiceType(type), type, null, args, dispatcher);
        }

        /// <summary>
        /// Создаёт прокси, разделяя тип сервиса и тип реализации. Creates a proxy that separates the service and implementation types.
        /// </summary>
        /// <param name="serviceType">Тип сервиса (интерфейс). Service type (interface).</param>
        /// <param name="implementationType">Тип реализации. Implementation type.</param>
        /// <param name="dispatcher">Диспетчер. Dispatcher.</param>
        /// <param name="args">Аргументы конструктора. Constructor arguments.</param>
        public SingleThreadedProxy(Type serviceType, Type implementationType, SingleThreadedDispatcher? dispatcher = null, params object[] args)
#if NETFRAMEWORK
            : base(GetRemoteType(serviceType))
#endif
        {
            this.Initialize(serviceType, implementationType, null, args, dispatcher);
        }

        /// <summary>
        /// Таймаут ожидания вызова. Timeout of a proxied call.
        /// </summary>
        public TimeSpan? InvokeTimeout { get; set; }

        /// <summary>
        /// Возвращает интерфейсный прокси указанного типа. Returns the typed transparent proxy.
        /// </summary>
        /// <typeparam name="T">Тип сервиса. Service type.</typeparam>
        /// <returns>Прозрачный прокси. Transparent proxy.</returns>
        public T GetTransparentProxy<T>() where T : class {
            return (T)this.GetTransparentProxy();
        }

#if NET8_0_OR_GREATER

        /// <summary>
        /// Возвращает прозрачный прокси. Returns the transparent proxy.
        /// </summary>
        /// <returns>Прозрачный прокси. Transparent proxy.</returns>
        public object GetTransparentProxy() {
            return this._transparentProxy;
        }

        private class DispatchProxyImpl : DispatchProxy {
            private SingleThreadedProxy? _owner;

            public DispatchProxyImpl() {
            }

            internal void Initialize(SingleThreadedProxy owner) {
                this._owner = owner;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) {
                if(targetMethod == null) {
                    throw new ArgumentNullException(nameof(targetMethod));
                }
                if(this._owner == null) {
                    throw new InvalidOperationException("Прокси не инициализирован.");
                }
                return this._owner.InvokeCore(targetMethod, args ?? Array.Empty<object>());
            }
        }

#endif

#if NETFRAMEWORK

        /// <summary>
        /// Перехватывает вызов целевого объекта. Intercepts a call to the target object.
        /// </summary>
        /// <param name="msg">Сообщение вызова. Call message.</param>
        /// <returns>Сообщение с результатом. Return message.</returns>
        public override IMessage Invoke(IMessage msg) {
            var call = msg as IMethodCallMessage;
            if(call == null) {
                return new ReturnMessage(new ArgumentException("Ожидалось сообщение IMethodCallMessage.", nameof(msg)), msg as IMethodCallMessage);
            }
            try {
                var args = call.Args;
                var result = this.InvokeCore(call.MethodBase, args);
                var parameters = call.MethodBase.GetParameters();
                var outArgs = new List<object>(parameters.Length);
                for(var i = 0; i < parameters.Length; i++) {
                    if(parameters[i].ParameterType.IsByRef) {
                        outArgs.Add(args[i]);
                    }
                }
                return new ReturnMessage(result, outArgs.ToArray(), outArgs.Count, call.LogicalCallContext, call);
            } catch(Exception ex) {
                return new ReturnMessage(ex, call);
            }
        }

#endif

        /// <summary>
        /// Освобождает прокси и собственный диспетчер. Disposes the proxy and its owned dispatcher.
        /// </summary>
        public void Dispose() {
            lock(this._eventsSync) {
                if(this._disposed) {
                    return;
                }
                this._disposed = true;
                foreach(var adapter in this._events.Values) {
                    adapter.Clear();
                }
                this._events.Clear();
            }
            if(this._ownsDispatcher) {
                this._dispatcher.Dispose();
            }
        }

        private void Initialize(Type serviceType, Type implementationType, object? instance, object?[]? activationArgs, SingleThreadedDispatcher? dispatcher) {
            if(serviceType == null) {
                throw new ArgumentNullException(nameof(serviceType));
            }
            if(implementationType == null) {
                throw new ArgumentNullException(nameof(implementationType));
            }
            this._implementationType = implementationType;
            this._instance = instance;
            this._activationArgs = activationArgs ?? Array.Empty<object>();
            if(dispatcher == null) {
                this._dispatcher = new SingleThreadedDispatcher();
                this._ownsDispatcher = true;
            } else {
                this._dispatcher = dispatcher;
                this._ownsDispatcher = false;
            }
#if NET8_0_OR_GREATER
            if(!serviceType.IsInterface) {
                throw new NotSupportedException("На этой платформе SingleThreadedProxy поддерживает только интерфейсы. Укажите интерфейс в качестве типа сервиса.");
            }
            var transparent = DispatchProxy.Create(serviceType, typeof(DispatchProxyImpl));
            ((DispatchProxyImpl)transparent).Initialize(this);
            this._transparentProxy = transparent;
#endif
        }

        private object? InvokeCore(MethodBase method, object?[] args) {
            this.ThrowIfDisposed();
            if(method.IsSpecialName) {
                if(method.Name.StartsWith("add_", StringComparison.Ordinal)) {
                    return this.ChangeSubscription(true, method.Name.Substring(4), method, args);
                }
                if(method.Name.StartsWith("remove_", StringComparison.Ordinal)) {
                    return this.ChangeSubscription(false, method.Name.Substring(7), method, args);
                }
            }
            return this.InvokeOnTarget(method, args);
        }

        private object? ChangeSubscription(bool add, string eventName, MethodBase method, object?[] args) {
            var handler = args.Length > 0 ? args[0] as Delegate : null;
            if(add) {
                if(handler == null) {
                    throw new ArgumentException("Делегат обработчика события не может быть null.");
                }
                lock(this._eventsSync) {
                    if(this._events.TryGetValue(eventName, out var current)) {
                        current.AddHandler(handler);
                        return null;
                    }
                }
                var context = SynchronizationContext.Current;
                this._dispatcher.Invoke(() => {
                    lock(this._eventsSync) {
                        if(this._events.TryGetValue(eventName, out var existing)) {
                            existing.AddHandler(handler);
                            return true;
                        }
                        var adapter = EventAdapter.Create(handler, this._dispatcher, context);
                        args[0] = adapter.ProxyHandler;
                        this.InvokeOnTargetInline(method, args);
                        this._events.Add(eventName, adapter);
                        return true;
                    }
                }, this.InvokeTimeout);
                return null;
            }
            EventAdapter? target;
            lock(this._eventsSync) {
                if(!this._events.TryGetValue(eventName, out target)) {
                    target = null;
                } else {
                    target.RemoveHandler(handler);
                    if(target.HasHandlers) {
                        return null;
                    }
                }
            }
            if(target == null) {
                return this.InvokeOnTarget(method, args);
            }
            var adapterToDetach = target;
            this._dispatcher.Invoke(() => {
                lock(this._eventsSync) {
                    if(!this._events.TryGetValue(eventName, out var current) || current.HasHandlers) {
                        return true;
                    }
                    args[0] = current.ProxyHandler;
                    this.InvokeOnTargetInline(method, args);
                    this._events.Remove(eventName);
                    return true;
                }
            }, this.InvokeTimeout);
            return null;
        }

        private object? InvokeOnTarget(MethodBase method, object?[] args) {
            return this._dispatcher.Invoke(() => this.InvokeOnTargetInline(method, args), this.InvokeTimeout);
        }

        private object? InvokeOnTargetInline(MethodBase method, object?[] args) {
            var instance = this.EnsureInstance();
            try {
                return method.Invoke(instance, args);
            } catch(TargetInvocationException ex) when(ex.InnerException != null) {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                return null;
            }
        }

        private object EnsureInstance() {
            if(this._instance == null) {
                try {
                    this._instance = this._activationArgs.Length == 0
                        ? Activator.CreateInstance(this._implementationType)
                        : Activator.CreateInstance(this._implementationType, this._activationArgs);
                } catch(TargetInvocationException ex) when(ex.InnerException != null) {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                }
            }
            return this._instance!;
        }

        private void ThrowIfDisposed() {
            if(this._disposed) {
                throw new ObjectDisposedException(nameof(SingleThreadedProxy));
            }
        }

        private static Type ResolveServiceType(Type implementationType) {
#if NET8_0_OR_GREATER
            if(implementationType.IsInterface) {
                return implementationType;
            }
            var interfaces = implementationType.GetInterfaces();
            if(interfaces.Length != 1) {
                throw new NotSupportedException("Тип " + implementationType.FullName + " реализует " + interfaces.Length + " интерфейсов. Укажите интерфейс явно: SingleThreadedProxy(Type serviceType, Type implementationType, ...).");
            }
            return interfaces[0];
#else
            return implementationType;
#endif
        }

        private static Type EnsureActivatable(Type type) {
            if(type == null) {
                throw new ArgumentNullException(nameof(type));
            }
#if NET8_0_OR_GREATER
            if(type.IsInterface) {
                throw new InvalidOperationException("Невозможно создать экземпляр интерфейса " + type.FullName + ". Используйте конструктор SingleThreadedProxy(Type serviceType, Type implementationType, ...).");
            }
#endif
            return type;
        }

        private static Type GetRemoteInstanceType(object? instance) {
            if(instance == null) {
                throw new ArgumentNullException(nameof(instance));
            }
            return instance.GetType();
        }

        private static Type GetRemoteType(Type? type) {
            if(type == null) {
                throw new ArgumentNullException(nameof(type));
            }
            return type;
        }
    }
}
