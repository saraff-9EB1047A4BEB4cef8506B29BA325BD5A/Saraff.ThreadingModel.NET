using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Saraff.ThreadingModel.Core {

    internal sealed class EventAdapter {
        private readonly object _sync = new object();
        private readonly SingleThreadedDispatcher _dispatcher;
        private readonly SynchronizationContext? _context;
        private readonly Delegate _proxyHandler;
        private Delegate? _handler;

        private EventAdapter(Delegate handler, SingleThreadedDispatcher dispatcher, SynchronizationContext? context) {
            this._handler = handler;
            this._dispatcher = dispatcher;
            this._context = context;
            this._proxyHandler = EventAdapterFactory.Create(this, handler.GetType());
        }

        public static EventAdapter Create(Delegate handler, SingleThreadedDispatcher dispatcher, SynchronizationContext? context) {
            if(handler == null) {
                throw new ArgumentNullException(nameof(handler));
            }
            if(dispatcher == null) {
                throw new ArgumentNullException(nameof(dispatcher));
            }
            return new EventAdapter(handler, dispatcher, context);
        }

        public Delegate ProxyHandler {
            get {
                return this._proxyHandler;
            }
        }

        public bool HasHandlers {
            get {
                lock(this._sync) {
                    return this._handler != null;
                }
            }
        }

        public void AddHandler(Delegate? handler) {
            if(handler == null) {
                return;
            }
            lock(this._sync) {
                this._handler = Delegate.Combine(this._handler, handler);
            }
        }

        public void RemoveHandler(Delegate? handler) {
            if(handler == null) {
                return;
            }
            lock(this._sync) {
                this._handler = Delegate.Remove(this._handler, handler);
            }
        }

        public void Clear() {
            lock(this._sync) {
                this._handler = null;
            }
        }

        public object? Invoke(object?[] args) {
            Delegate? handler;
            lock(this._sync) {
                handler = this._handler;
            }
            if(handler == null) {
                return null;
            }
            var context = this._context;
            if(context == null || ReferenceEquals(context, this._dispatcher.SynchronizationContext)) {
                return InvokeDirect(handler, args);
            }
            object? result = null;
            Exception? error = null;
            try {
                context.Send(state => {
                    try {
                        result = InvokeDirect(handler, args);
                    } catch(Exception ex) {
                        error = ex;
                    }
                }, null);
            } catch(NotSupportedException) {
                return InvokeDirect(handler, args);
            } catch(ObjectDisposedException) {
                return InvokeDirect(handler, args);
            } catch(ArgumentException) {
                return InvokeDirect(handler, args);
            }
            if(error != null) {
                ExceptionDispatchInfo.Capture(error).Throw();
            }
            return result;
        }

        private static object? InvokeDirect(Delegate handler, object?[] args) {
            try {
                return handler.DynamicInvoke(args);
            } catch(TargetInvocationException ex) when(ex.InnerException != null) {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                return null;
            }
        }
    }

    internal static class EventAdapterFactory {
        private static readonly ConcurrentDictionary<Type, EventShape> Shapes = new ConcurrentDictionary<Type, EventShape>();

        public static Delegate Create(EventAdapter adapter, Type delegateType) {
            var shape = Shapes.GetOrAdd(delegateType, CreateShape);
            var parameters = new ParameterExpression[shape.Parameters.Length];
            var arguments = new Expression[shape.Parameters.Length];
            for(var i = 0; i < shape.Parameters.Length; i++) {
                parameters[i] = Expression.Parameter(shape.Parameters[i].ParameterType, shape.Parameters[i].Name ?? "arg" + i);
                arguments[i] = Expression.Convert(parameters[i], typeof(object));
            }
            var call = Expression.Call(
                Expression.Constant(adapter),
                EventShape.InvokeMethod,
                Expression.NewArrayInit(typeof(object), arguments));
            var body = shape.ReturnType == typeof(void)
                ? (Expression)Expression.Block(call, Expression.Empty())
                : Expression.Convert(call, shape.ReturnType);
            return Expression.Lambda(delegateType, body, parameters).Compile();
        }

        private static EventShape CreateShape(Type delegateType) {
            var invoke = delegateType.GetMethod("Invoke");
            if(invoke == null) {
                throw new ArgumentException("Тип " + delegateType.FullName + " не является делегатом.", nameof(delegateType));
            }
            var parameters = invoke.GetParameters();
            for(var i = 0; i < parameters.Length; i++) {
                if(parameters[i].ParameterType.IsByRef) {
                    throw new NotSupportedException("События с параметрами ref/out не поддерживаются: " + delegateType.FullName + ".");
                }
            }
            return new EventShape(invoke.ReturnType, parameters);
        }

        private sealed class EventShape {
            internal static readonly MethodInfo InvokeMethod = typeof(EventAdapter).GetMethod(nameof(EventAdapter.Invoke), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

            public EventShape(Type returnType, ParameterInfo[] parameters) {
                this.ReturnType = returnType;
                this.Parameters = parameters;
            }

            public Type ReturnType { get; }

            public ParameterInfo[] Parameters { get; }
        }
    }
}
