using System.Threading;
using System.Threading.Tasks;

namespace Saraff.ThreadingModel {

    /// <summary>
    /// Обработчик сообщения очереди диспетчера. Queue message handler.
    /// </summary>
    /// <typeparam name="TMessage">Тип сообщения. Message type.</typeparam>
    public interface IQueueMessageHandler<in TMessage> {

        /// <summary>
        /// Обрабатывает сообщение в служебном потоке. Handles the message on the dispatcher thread.
        /// </summary>
        /// <param name="message">Сообщение. Message.</param>
        /// <param name="cancellationToken">Токен отмены. Cancellation token.</param>
        /// <returns>Задача обработки. Handling task.</returns>
        Task HandleAsync(TMessage message, CancellationToken cancellationToken);
    }
}
