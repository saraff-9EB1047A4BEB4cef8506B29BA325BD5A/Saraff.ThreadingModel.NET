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
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.IO;

namespace Saraff.ThreadingModel.Core {

    public class EventHandlerHelper {
        private static AssemblyName _assemblyName=null;
        private static AssemblyBuilder _assemblyBuilder=null;
        private static ModuleBuilder _moduleBuilder=null;
        private static string _handlerName="InvokeCore";

        protected EventHandlerHelper() {
        }

        internal static EventHandlerHelper Create(string eventName, Delegate handler, SingleThreadedHelper helper) {
            var _helper=Activator.CreateInstance(EventHandlerHelper.CreateHandler(handler.Method)) as EventHandlerHelper;
            _helper.EventName=eventName;
            _helper.Handler=handler;
            _helper.Helper=helper;
            _helper.ProxyHandler=Delegate.CreateDelegate(_helper.Handler.GetType(), _helper, EventHandlerHelper._handlerName);

            return _helper;
        }

        internal void AddHandler(Delegate handler) {
            this.Handler=Delegate.Combine(this.Handler, handler);
        }

        internal void RemoveHandler(Delegate handler) {
            this.Handler=Delegate.Remove(this.Handler, handler);
        }

        internal string EventName {
            get;
            private set;
        }

        internal Delegate Handler {
            get;
            private set;
        }

        internal Delegate ProxyHandler {
            get;
            private set;
        }

        internal SingleThreadedHelper Helper {
            get;
            private set;
        }

        protected object Invoke(object[] args) {
            if(this.Handler!=null) {
                return this.Helper.EventHandler(this.Handler, args);
            }
            return null;
        }

        #region Emit

        #region Methods

        private static Type CreateHandler(MethodInfo handler) {
            var _typeBuilder=EventHandlerHelper.ModuleBuilder.DefineType(string.Format("{0}.__{1}", typeof(EventHandlerHelper).Namespace, Guid.NewGuid().ToString("N")), TypeAttributes.Class|TypeAttributes.Public|TypeAttributes.Sealed, typeof(EventHandlerHelper));

            #region Определение конструктора

            _typeBuilder.DefineDefaultConstructor(MethodAttributes.Public|MethodAttributes.HideBySig);

            #endregion

            #region Определение метода (обработчика)

            #region Определение типов параметров

            var _parameterInfo=handler.GetParameters();
            var _parameterTypes=new Type[_parameterInfo.Length];
            for(var i=0; i<_parameterTypes.Length; i++) {
                _parameterTypes[i]=_parameterInfo[i].ParameterType;
            }

            #endregion

            var _methodBuilder=_typeBuilder.DefineMethod(EventHandlerHelper._handlerName, MethodAttributes.Private|MethodAttributes.HideBySig, handler.ReturnType, _parameterTypes);

            #region Определение параметров

            for(var i=0; i<_parameterInfo.Length; i++) {
                _methodBuilder.DefineParameter(i+1, _parameterInfo[i].Attributes, _parameterInfo[i].Name);
            }

            #endregion

            #region Определение тела

            var _il=_methodBuilder.GetILGenerator();
            var _args=_il.DeclareLocal(typeof(object[])); // создаем локальную переменную с индексом 0

            var _paramCount=handler.GetParameters().Length;

            #region Создаем новый массив

            switch(_paramCount) {
                case 0:
                    _il.Emit(OpCodes.Ldc_I4_0); // число элементов массива
                    break;
                case 1:
                    _il.Emit(OpCodes.Ldc_I4_1); // число элементов массива
                    break;
                case 2:
                    _il.Emit(OpCodes.Ldc_I4_2); // число элементов массива
                    break;
                case 3:
                    _il.Emit(OpCodes.Ldc_I4_3); // число элементов массива
                    break;
                case 4:
                    _il.Emit(OpCodes.Ldc_I4_4); // число элементов массива
                    break;
                case 5:
                    _il.Emit(OpCodes.Ldc_I4_5); // число элементов массива
                    break;
                case 6:
                    _il.Emit(OpCodes.Ldc_I4_6); // число элементов массива
                    break;
                case 7:
                    _il.Emit(OpCodes.Ldc_I4_7); // число элементов массива
                    break;
                case 8:
                    _il.Emit(OpCodes.Ldc_I4_8); // число элементов массива
                    break;
                default:
                    _il.Emit(OpCodes.Ldc_I4, _paramCount); // число элементов массива
                    break;
            }

            _il.Emit(OpCodes.Newarr, typeof(object)); // тип элементов массива
            _il.Emit(OpCodes.Stloc, _args); // сохраняем ссылку на массив в локальной переменной с индексом 0

            #endregion

            #region Записываем аргумент в массив

            for(var i=0; i<_paramCount; i++) {
                _il.Emit(OpCodes.Ldloc, _args); // ссылка на массив

                switch(i) {
                    case 0:
                        _il.Emit(OpCodes.Ldc_I4_0); // индекс элемента в массиве
                        _il.Emit(OpCodes.Ldarg_1); // аргумет
                        break;
                    case 1:
                        _il.Emit(OpCodes.Ldc_I4_1); // индекс элемента в массиве
                        _il.Emit(OpCodes.Ldarg_2); // аргумет
                        break;
                    case 2:
                        _il.Emit(OpCodes.Ldc_I4_2); // индекс элемента в массиве
                        _il.Emit(OpCodes.Ldarg_3); // аргумет
                        break;
                    default:
                        _il.Emit(OpCodes.Ldc_I4, i); // индекс элемента в массиве
                        _il.Emit(OpCodes.Ldarg, i+1); // аргумет
                        break;
                }
                if(_parameterTypes[i].IsValueType) {
                    _il.Emit(OpCodes.Box, _parameterTypes[i]);
                }
                _il.Emit(OpCodes.Stelem_Ref); // заменяем элемент массива
            }

            #endregion

            #region Вызываем метод object Invoke(object[] args)

            _il.Emit(OpCodes.Ldarg_0); // this
            _il.Emit(OpCodes.Ldloc, _args); // ссылка на массив
            _il.Emit(OpCodes.Call, typeof(EventHandlerHelper).GetMethod("Invoke", BindingFlags.NonPublic|BindingFlags.Instance)); // вызов метода
            if(handler.ReturnType==typeof(void)) {
                _il.Emit(OpCodes.Pop);
            }

            #endregion

            _il.Emit(OpCodes.Ret); // выполняем возврат из метода

            #endregion

            #endregion

            return _typeBuilder.CreateType();
        }

        #endregion

        #region Properties

        private static AssemblyName AssemblyName {
            get {
                if(EventHandlerHelper._assemblyName==null) {
                    EventHandlerHelper._assemblyName=new AssemblyName(string.Format("__{0}", Guid.NewGuid().ToString("N")));
                }
                return EventHandlerHelper._assemblyName;
            }
        }

        private static AssemblyBuilder AssemblyBuilder {
            get {
                if(EventHandlerHelper._assemblyBuilder==null) {
                    EventHandlerHelper._assemblyBuilder=Thread.GetDomain().DefineDynamicAssembly(EventHandlerHelper.AssemblyName, AssemblyBuilderAccess.RunAndSave);
                }
                return EventHandlerHelper._assemblyBuilder;
            }
        }

        private static ModuleBuilder ModuleBuilder {
            get {
                if(EventHandlerHelper._moduleBuilder==null) {
                    EventHandlerHelper._moduleBuilder=EventHandlerHelper.AssemblyBuilder.DefineDynamicModule(EventHandlerHelper.AssemblyName.Name, Path.ChangeExtension(EventHandlerHelper.AssemblyName.Name, ".dll"), true);
                }
                return EventHandlerHelper._moduleBuilder;
            }
        }

        #endregion

        #endregion
    }
}
