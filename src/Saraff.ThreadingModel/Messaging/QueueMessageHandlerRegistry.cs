using System;
using System.Collections.Generic;

namespace Saraff.ThreadingModel.Messaging {

    internal sealed class QueueMessageHandlerRegistration {
        public QueueMessageHandlerRegistration(Type serviceType, string? name) {
            this.ServiceType = serviceType;
            this.Name = name;
        }

        public Type ServiceType { get; }

        public string? Name { get; }
    }

    internal sealed class QueueMessageHandlerRegistry {
        private readonly object _sync = new object();
        private readonly List<Entry> _registrations = new List<Entry>();

        public void Register(Type messageType, Type serviceType, string? name) {
            if(messageType == null) {
                throw new ArgumentNullException(nameof(messageType));
            }
            if(serviceType == null) {
                throw new ArgumentNullException(nameof(serviceType));
            }
            var expected = typeof(IQueueMessageHandler<>).MakeGenericType(messageType);
            if(!expected.IsAssignableFrom(serviceType)) {
                throw new ArgumentException("Тип " + serviceType.FullName + " не реализует " + expected.FullName + ".", nameof(serviceType));
            }
            lock(this._sync) {
                foreach(var entry in this._registrations) {
                    if(entry.ServiceType == serviceType && entry.MessageType == messageType && string.Equals(entry.Name, name, StringComparison.Ordinal)) {
                        return;
                    }
                }
                this._registrations.Add(new Entry(messageType, serviceType, name));
            }
        }

        public IReadOnlyList<QueueMessageHandlerRegistration> GetRegistrations(Type messageType) {
            var result = new List<QueueMessageHandlerRegistration>();
            lock(this._sync) {
                foreach(var entry in this._registrations) {
                    if(entry.MessageType.IsAssignableFrom(messageType) && !Contains(result, entry.ServiceType, entry.Name)) {
                        result.Add(new QueueMessageHandlerRegistration(entry.ServiceType, entry.Name));
                    }
                }
            }
            return result;
        }

        private static bool Contains(List<QueueMessageHandlerRegistration> registrations, Type serviceType, string? name) {
            foreach(var registration in registrations) {
                if(registration.ServiceType == serviceType && string.Equals(registration.Name, name, StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }

        private sealed class Entry {
            public Entry(Type messageType, Type serviceType, string? name) {
                this.MessageType = messageType;
                this.ServiceType = serviceType;
                this.Name = name;
            }

            public Type MessageType { get; }

            public Type ServiceType { get; }

            public string? Name { get; }
        }
    }
}
