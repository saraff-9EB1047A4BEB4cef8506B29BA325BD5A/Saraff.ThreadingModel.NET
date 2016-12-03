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
using System.Security.Permissions;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using Saraff.ThreadingModel.Core;

namespace Saraff.ThreadingModel {

    /// <summary>
    /// Предоставляет функциональность для прокси. Provides functionality for proxies.
    /// </summary>
    public sealed class SingleThreadedProxy:RealProxy {
        private object _instance;
        private _CreateInstanceCallback _activator;
        private AutoResetEvent _activatorEvent=new AutoResetEvent(false);
        private Dictionary<string,EventHandlerHelper> _events=new Dictionary<string,EventHandlerHelper>();
        private SingleThreadedHelper _helper;

        /// <summary>
        /// Инициализирует новый экземпляр класса <c>SingleThreadedProxy</c>
        /// указанным объектом <c>System.Object</c>.
        /// Initializes a new instance of the <c>SingleThreadedProxy</c>
        /// class that represents a remote object of the specified <c>System.Object</c>.
        /// </summary>
        /// <param name="instance">Удаленный объект для которого создается прокси. The the remote object for which to create a proxy.</param>
        [PermissionSet(SecurityAction.LinkDemand)]
        public SingleThreadedProxy(object instance) : base(instance.GetType()) {
            this._activator=() => {
                this._instance=instance;
                this._activatorEvent.Set();
            };
        }

        /// <summary>
        /// Инициализирует новый экземпляр класса <c>SingleThreadedProxy</c>
        /// указанным объектом <c>System.Type</c>.
        /// Initializes a new instance of the <c>SingleThreadedProxy</c>
        /// class that represents a remote object of the specified <c>System.Type</c>.
        /// </summary>
        /// <param name="type">Тип удаленного объекта для которого создается прокси. The System.Type of the remote object for which to create a proxy.</param>
        [PermissionSet(SecurityAction.LinkDemand)]
        public SingleThreadedProxy(Type type) : base(type) {
            this._activator=() => {
                this._instance=Activator.CreateInstance(type);
                this._activatorEvent.Set();
            };
        }

        /// <summary>
        /// Инициализирует новый экземпляр класса <c>SingleThreadedProxy</c>
        /// указанным объектом <c>System.Type</c>.
        /// Initializes a new instance of the <c>SingleThreadedProxy</c>
        /// class that represents a remote object of the specified <c>System.Type</c>.
        /// </summary>
        /// <param name="type">Тип удаленного объекта для которого создается прокси. The System.Type of the remote object for which to create a proxy.</param>
        /// <param name="args">
        /// Массив аргументов, соответствующих количеству, порядку и типу параметров вызываемого конструктора.
        /// An array of arguments that match in number, order, and type the parameters of the constructor to invoke.
        /// </param>
        [PermissionSet(SecurityAction.LinkDemand)]
        public SingleThreadedProxy(Type type,params object[] args) : base(type) {
            this._activator=() => {
                this._instance=Activator.CreateInstance(type,args);
                this._activatorEvent.Set();
            };
        }

        /// <summary>
        /// Вызывает метод удаленного объекта определенный в указанном в 
        /// System.Runtime.Remoting.Messaging.IMessage.
        /// Invokes the method that is specified in the
        /// provided System.Runtime.Remoting.Messaging.IMessage on the remote
        /// object that is represented by the current instance.
        /// </summary>
        /// <param name="msg">
        /// Сообщение, которое содержит словарь с информацией о вызове метода.
        /// A System.Runtime.Remoting.Messaging.IMessage that contains a System.Collections.IDictionary
        /// of information about the method call.
        /// </param>
        /// <returns>
        /// Сообщение возвращенное вызвонным методом, содержащее возвращаемое значение и
        /// все исходящие параметры или параметры переданные по ссылке.
        /// The message returned by the invoked method, containing the return value and
        /// any out or ref parameters.
        /// </returns>
        [SecurityPermissionAttribute(SecurityAction.LinkDemand, Flags=SecurityPermissionFlag.Infrastructure)]
        public override IMessage Invoke(IMessage msg) {
            Monitor.Enter(this);
            try {
                var _msg=msg as IMethodCallMessage;
                try {

                    var _args=new object[_msg.ArgCount];
                    for(int i=0; i<_msg.ArgCount; i++) {
                        _args[i]=_msg.Args[i];
                    }

                    if(_msg.MethodBase.IsSpecialName) {
                        var _name=_msg.MethodBase.Name.Split(new string[] { "_" }, 2, StringSplitOptions.None);
                        var _eventName=_name[1];
                        switch(_name[0]) {
                            case "add":
                                if(this._events.ContainsKey(_eventName)) {
                                    var _isExist=this._events[_eventName].Handler!=null;
                                    this._events[_eventName].AddHandler(_args[0] as Delegate);
                                    if(_isExist) {
                                        return new ReturnMessage(null,_args,0,_msg.LogicalCallContext,_msg);
                                    }
                                } else {
                                    this._events.Add(_eventName,EventHandlerHelper.Create(_eventName,_args[0] as Delegate,this.Helper));
                                }
                                _args[0]=this._events[_eventName].ProxyHandler;
                                break;
                            case "remove":
                                if(this._events.ContainsKey(_eventName)) {
                                    this._events[_eventName].RemoveHandler(_args[0] as Delegate);
                                    if(this._events[_eventName].Handler!=null) {
                                        return new ReturnMessage(null, _args, 0, _msg.LogicalCallContext, _msg);
                                    }
                                    _args[0]=this._events[_eventName].ProxyHandler;
                                }
                                break;
                        }
                    }

                    var _result=this.Helper.InvokeHandler(this._instance, _msg.MethodBase, _args);
                    return new ReturnMessage(_result, _args, 0, _msg.LogicalCallContext, _msg);
                } catch(Exception ex) {
                    return new ReturnMessage(ex, _msg);
                }
            } finally {
                Monitor.Exit(this);
            }
        }

        private SingleThreadedHelper Helper {
            get {
                if(this._helper==null) {
                    this._helper=SingleThreadedHelper.Create(this._activator);
                    this._activatorEvent.WaitOne();
                    this._activatorEvent.Close();
                }
                return this._helper;
            }
        }
    }
}
