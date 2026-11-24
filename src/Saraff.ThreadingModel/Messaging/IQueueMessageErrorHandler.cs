using System;
using System.Threading;
using System.Threading.Tasks;

namespace Saraff.ThreadingModel {

    /// <summary>
    /// Обработчик ошибок очереди сообщений. Queue message error handler.
    /// </summary>
    public interface IQueueMessageErrorHandler {

        /// <summary>
        /// Обрабатывает ошибку сообщения. Handles a message error.
        /// </summary>
        /// <param name="message">Сообщение. Message.</param>
        /// <param name="error">Ошибка. Error.</param>
        /// <param name="cancellationToken">Токен отмены. Cancellation token.</param>
        /// <returns>Задача обработки. Handling task.</returns>
        Task HandleAsync(object message, Exception error, CancellationToken cancellationToken);
    }
}
