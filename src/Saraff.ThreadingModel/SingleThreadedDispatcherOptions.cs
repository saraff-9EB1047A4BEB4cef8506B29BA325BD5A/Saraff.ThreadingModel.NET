using System;
using System.Threading;

namespace Saraff.ThreadingModel {

    /// <summary>
    /// Настройки диспетчера однопоточного исполнения. Options of the single-threaded dispatcher.
    /// </summary>
    public sealed class SingleThreadedDispatcherOptions {

        /// <summary>
        /// Имя служебного потока. Name of the dispatcher thread.
        /// </summary>
        public string? ThreadName { get; set; }

        /// <summary>
        /// Апартамент служебного потока. Apartment state of the dispatcher thread.
        /// </summary>
        public ApartmentState ApartmentState { get; set; } = ApartmentState.STA;

        /// <summary>
        /// Откачивать сообщения Win32 в служебном потоке. Whether the dispatcher thread pumps Win32 messages.
        /// </summary>
        public bool UseMessagePump { get; set; } = true;

        /// <summary>
        /// Максимальная длина очереди (0 — без ограничения). Maximum queue length (0 means unlimited).
        /// </summary>
        public int MaxQueueLength { get; set; }

        /// <summary>
        /// Таймаут ожидания результата синхронного вызова. Timeout of a synchronous call.
        /// </summary>
        public TimeSpan? InvokeTimeout { get; set; }

        /// <summary>
        /// Таймаут исполнения обработчика сообщения. Timeout of a queue message handler.
        /// </summary>
        public TimeSpan? HandlerTimeout { get; set; }

        /// <summary>
        /// Продолжать обработку очереди после ошибки обработчика. Whether to keep processing after a handler error.
        /// </summary>
        public bool ContinueOnHandlerError { get; set; } = true;

        /// <summary>
        /// Игнорировать сообщения без зарегистрированных обработчиков. Whether messages without handlers are ignored.
        /// </summary>
        public bool IgnoreUnhandledMessages { get; set; }

        /// <summary>
        /// Таймаут запуска служебного потока. Timeout of the dispatcher thread startup.
        /// </summary>
        public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Таймаут остановки служебного потока. Timeout of the dispatcher thread shutdown.
        /// </summary>
        public TimeSpan DisposeTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }
}
