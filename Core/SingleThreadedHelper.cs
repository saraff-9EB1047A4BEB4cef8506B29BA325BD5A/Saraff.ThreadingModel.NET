/* Этот файл является частью библиотеки Saraff.ThreadingModel.NET
 * © SARAFF SOFTWARE (Кирножицкий Андрей), 2015.
 * Saraff.ThreadingModel.NET - свободная программа: вы можете перераспространять ее и/или
 * изменять ее на условиях Меньшей Стандартной общественной лицензии GNU в том виде,
 * в каком она была опубликована Фондом свободного программного обеспечения;
 * либо версии 3 лицензии, либо (по вашему выбору) любой более поздней
 * версии.
 * Saraff.ThreadingModel.NET распространяется в надежде, что она будет полезной,
 * но БЕЗО ВСЯКИХ ГАРАНТИЙ; даже без неявной гарантии ТОВАРНОГО ВИДА
 * или ПРИГОДНОСТИ ДЛЯ ОПРЕДЕЛЕННЫХ ЦЕЛЕЙ. Подробнее см. в Меньшей Стандартной
 * общественной лицензии GNU.
 * Вы должны были получить копию Меньшей Стандартной общественной лицензии GNU
 * вместе с этой программой. Если это не так, см.
 * <http://www.gnu.org/licenses/>.)
 * 
 * This file is part of Saraff.ThreadingModel.NET.
 * © SARAFF SOFTWARE (Kirnazhytski Andrei), 2011.
 * Saraff.ThreadingModel.NET is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * Saraff.ThreadingModel.NET is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 * You should have received a copy of the GNU Lesser General Public License
 * along with Saraff.Twain.NET. If not, see <http://www.gnu.org/licenses/>.
 * 
 * PLEASE SEND EMAIL TO:  threading@saraff.ru.
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.Threading;
using System.Diagnostics;
using System.Reflection;

namespace Saraff.ThreadingModel.Core {

    internal sealed class SingleThreadedHelper {
        private AutoResetEvent _coreSyncEvent=new AutoResetEvent(false);
        private AutoResetEvent _externalSyncEvent=new AutoResetEvent(false);
        private Stack<InvokeHelper> _eventsStack=new Stack<InvokeHelper>();
        private Stack<InvokeHelper> _methodsStack=new Stack<InvokeHelper>();

        private SingleThreadedHelper() {
            this._ThreadLoop();
        }

        internal static SingleThreadedHelper Create() {
            return new SingleThreadedHelper();
        }

        private void _ThreadLoop() {
            var _thread=new Thread(() => {
                while(true) {
                    this._coreSyncEvent.WaitOne();

                    this._methodsStack.Peek().Invoke();

                    this._externalSyncEvent.Set();
                }
            }) {
                IsBackground=true
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        private void _WaitInternal() {
            this._externalSyncEvent.Set();
            this._coreSyncEvent.WaitOne();
        }

        private void _WaitExternal() {
            this._coreSyncEvent.Set();
            this._externalSyncEvent.WaitOne();
        }

        internal object InvokeHandler(object obj, MethodBase target, params object[] args) {
            this._methodsStack.Push(InvokeHelper.Create(obj, target, args));
            try {
                this._WaitExternal();
                while(this._eventsStack.Count==this._methodsStack.Count) {
                    this._eventsStack.Peek().Invoke();
                    this._WaitExternal();
                }

                if(this._methodsStack.Peek().Exception!=null) {
                    throw new TargetInvocationException(this._methodsStack.Peek().Exception);
                }
                return this._methodsStack.Peek().ReturnValue;
            } finally {
                this._methodsStack.Pop();
            }
        }

        internal object EventHandler(Delegate target, params object[] args) {
            this._eventsStack.Push(InvokeHelper.Create(target, args));
            try {
                this._WaitInternal();
                while(this._eventsStack.Count==this._methodsStack.Count-1) {
                    this._methodsStack.Peek().Invoke();
                    this._WaitInternal();
                }
                if(this._eventsStack.Peek().Exception!=null) {
                    throw new TargetInvocationException(this._eventsStack.Peek().Exception);
                }
                return this._eventsStack.Peek().ReturnValue;
            } finally {
                this._eventsStack.Pop();
            }
        }

        private sealed class InvokeHelper {
            private Delegate _target;
            private object _obj;
            private MethodBase _targetMethod;
            private object[] _args;

            private InvokeHelper() {
            }

            public static InvokeHelper Create(Delegate target, params object[] args) {
                if(target==null||args==null) {
                    throw new ArgumentNullException();
                }
                var _params=target.Method.GetParameters();
                if(_params.Length!=args.Length) {
                    throw new ArgumentException();
                }
                for(int i=0; i<_params.Length; i++) {
                    if(_params[i].ParameterType!=args[i].GetType()&&!args[i].GetType().IsSubclassOf(_params[i].ParameterType)) {
                        throw new ArgumentException();
                    }
                }
                return new InvokeHelper {
                    _target=target,
                    _args=args
                };
            }

            public static InvokeHelper Create(object obj, MethodBase target, params object[] args) {
                if(target==null||args==null) {
                    throw new ArgumentNullException();
                }
                if(target.GetParameters().Length!=args.Length) {
                    throw new ArgumentException();
                }
                return new InvokeHelper {
                    _obj=obj,
                    _targetMethod=target,
                    _args=args
                };
            }

            public void Invoke() {
                try {
                    this.ReturnValue=this._targetMethod!=null?this._targetMethod.Invoke(this._obj, this._args):this._target.DynamicInvoke(this._args);
                } catch(Exception ex) {
                    this.Exception=ex;
                }
            }

            public object ReturnValue {
                get;
                private set;
            }

            public Exception Exception {
                get;
                private set;
            }
        }
    }
}
