# Изменения

## 2.0.0

Полный рефакторинг библиотеки: сохранён публичный контракт `SingleThreadedProxy` (конструкторы и `GetTransparentProxy`), внутренняя реализация переписана.

### Добавлено

- `SingleThreadedDispatcher` — служебный поток (STA по умолчанию), очередь FIFO, синхронные `Send`/`Invoke`, асинхронные `InvokeAsync`/`PostAsync`, таймауты, `IDisposable`/`IAsyncDisposable`.
- Насос сообщений Win32 (`MsgWaitForMultipleObjects` + `PeekMessage`) в служебном потоке.
- `StaSynchronizationContext`: `Post`/`Send` и реентерабельность без самодедлоков.
- Очередь сообщений: `IQueueMessageHandler<T>`, `IQueueMessageErrorHandler`, scope на сообщение, `HandlerTimeout`, `MaxQueueLength`, `IgnoreUnhandledMessages`.
- Интеграция с `Saraff.IoC` 2.0.0: `RegisterSingleThreadedDispatcher`, `RegisterSingleThreadedProxy`, `RegisterQueueMessageHandler`, `RegisterQueueErrorHandler` (именованные диспетчеры, lifetimes, авто-`Dispose`).
- Обработчики очереди регистрируются под именем своего типа: несколько реализаций `IQueueMessageHandler<T>` (включая `Singleton`) не конфликтуют в кэше контейнера; диспетчер разрешает их по имени.
- `net8.0-windows` на `DispatchProxy`; `net462` остаётся на `RealProxy`.
- Тесты (xUnit) на обоих TFM, включая стресс-сценарии, DI, события, `ref/out`.
- Пример: `samples/Saraff.ThreadingModel.Sample`.
- CI (GitHub Actions), `.editorconfig`, `.gitignore`, `Directory.Build.props`.

### Исправлено

- Зависание при исключении в конструкторе целевого объекта: ошибка возвращается вызывающему.
- Отсутствие остановки служебного потока: добавлены `Dispose`/`DisposeAsync` и `ObjectDisposedException`.
- STA без откачки сообщений: добавлен Win32 message pump.
- Дедлок при реентерабельном вызове прокси из обработчика события.
- `NullReferenceException` на `null`-аргументе события.
- Неверный IL для делегатов событий с возвращаемым значением (ранее требовался `void`).
- `TargetInvocationException` больше не «двойная» обёртка: исходное исключение сохраняется через `ExceptionDispatchInfo`.
- Утечка динамических типов/сборок: ручной `ILGenerator` и `AssemblyBuilderAccess.RunAndSave` заменены на `Expression`-деревья.
- Обработка снятия последнего подписчика события (удаление записи без дублей и утечек).
- `ref/out`-параметры проксируемых методов корректно возвращаются (`RealProxy`-путь).

### Изменено поведение

- Сериализация вызовов прокси выполняется очередью диспетчера, а не `Monitor`-блокировкой; `Monitor` на прокси больше не используется.
- События доставляются через `SynchronizationContext` подписчика (если он был захвачен при подписке); иначе — в потоке диспетчера.
- Публичный тип `Core.EventHandlerHelper` удалён; `Core.SingleThreadedHelper` удалён.
- Целевые платформы: `net462;net8.0-windows` (вместо .NET Framework 2.0).
- Сборка больше не подписана: `sign.snk` удалён из репозитория.

### Известные ограничения

- `net8.0-windows`: `SingleThreadedProxy` поддерживает только интерфейсы.
- События с `ref`/`out`-параметрами делегата не поддерживаются (`NotSupportedException`).
- Отмена уже выполняющегося синхронного вызова не поддерживается.
