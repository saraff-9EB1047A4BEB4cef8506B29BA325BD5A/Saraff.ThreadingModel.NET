# Saraff.ThreadingModel.NET

![license: LGPL 3.0 or later](https://img.shields.io/badge/license-LGPL%203.0%20or%20later-blue?style=flat&logo=git)

Однопоточная модель исполнения для .NET: выделенный поток (по умолчанию STA) с откачкой сообщений Win32, интерфейсный прокси, который маршализует все вызовы в этот поток, и очередь сообщений с обработчиками, регистрируемыми через [Saraff.IoC](https://www.nuget.org/packages/Saraff.IoC).

A single-threaded execution model for .NET: a dedicated STA thread with a Win32 message pump, an interface proxy that marshals every call to that thread, and a DI-friendly message queue.

## Целевые платформы

| TFM | Прокси | Saraff.IoC |
|---|---|---|
| `net462` | `RealProxy` (`System.Runtime.Remoting`) | `net40`-сборка (синхронная) |
| `net8.0-windows` | `DispatchProxy` (только интерфейсы) | `netstandard2.0`-сборка (async-инициализация, `DisposeAsync`) |

## Ссылки

* [NuGet package](https://www.nuget.org/packages/Saraff.ThreadingModel/)
* [Документация](https://saraff-9eb1047a4beb4cef8506b29ba325bd5a.github.io/saraffthread/)
* [Исходный код](https://github.com/saraff-9EB1047A4BEB4cef8506B29BA325BD5A/Saraff.ThreadingModel.NET)

## Установка

```powershell
dotnet add package Saraff.ThreadingModel
```

```powershell
PM> Install-Package Saraff.ThreadingModel
```

Зависимость: `Saraff.IoC >= 2.0.0`.

## Быстрый старт: прокси

```csharp
using Saraff.ThreadingModel;

public interface IScanner {
    int ThreadId { get; }
    ApartmentState Apartment { get; }
    int Scan(int pages);
    event EventHandler<ScanCompletedEventArgs>? Completed;
}

using (var proxy = new SingleThreadedProxy(typeof(IScanner), typeof(Scanner))) {
    var scanner = proxy.GetTransparentProxy<IScanner>();
    // ThreadId != текущий поток, Apartment == STA
    var result = scanner.Scan(2);
}
```

- Прокси создаёт собственный диспетчер и владеет им (`Dispose` останавливает поток), либо принимает общий диспетчер через перегрузки конструктора.
- События целевого объекта перенаправляются подписчикам: если подписка выполнена в потоке с `SynchronizationContext`, обработчик будет вызван через `Send` этого контекста; иначе — в потоке диспетчера.
- `InvokeTimeout` ограничивает ожидание вызова; `ObjectDisposedException` после `Dispose`.

Ограничения `net8.0-windows`: проксируются только интерфейсы (`DispatchProxy`). Для класса с несколькими интерфейсами укажите интерфейс явно: `new SingleThreadedProxy(typeof(IService), typeof(Implementation))`.

## Быстрый старт: диспетчер и очередь сообщений

```csharp
using Saraff.IoC;
using Saraff.ThreadingModel;
using Saraff.ThreadingModel.DependencyInjection;

using (var container = new ServiceContainer()) {
    container.RegisterSingleThreadedDispatcher("scanner", options => {
        options.ThreadName = "STA Scanner";
        options.HandlerTimeout = TimeSpan.FromSeconds(10);
    });
    container.RegisterSingleThreadedProxy<IScanner, Scanner>("scanner");
    container.Register<ScanSession, ScanSession>(ServiceLifetime.Scoped);
    container.RegisterQueueMessageHandler<ScanRequest, ScanRequestHandler>();
    container.RegisterQueueErrorHandler<ConsoleErrorHandler>(ServiceLifetime.Singleton);

    var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("scanner");
    var scanner = container.Resolve<IScanner>();

    dispatcher.Post(new ScanRequest(3, "invoice"));
    await dispatcher.PostAsync(new ScanRequest(7, "passport"));
}
```

Обработчик — обычный сервис контейнера:

```csharp
public sealed class ScanRequestHandler : IQueueMessageHandler<ScanRequest> {
    private readonly IScanner _scanner;
    private readonly ScanSession _session;

    [ServiceRequired]
    public ScanRequestHandler(IScanner scanner, ScanSession session) {
        this._scanner = scanner;
        this._session = session;
    }

    public Task HandleAsync(ScanRequest message, CancellationToken cancellationToken) {
        this._session.Log(message.Name);
        this._scanner.Scan(message.Pages);
        return Task.CompletedTask;
    }
}
```

### Семантика очереди

- Сообщения обрабатываются в порядке постановки, в потоке диспетчера (thread-affinity для COM/TWAIN).
- На каждое сообщение создаётся `IServiceScope` Saraff.IoC; scoped-зависимости освобождаются после обработки.
- `IQueueMessageHandler<T>` разрешается по типу сообщения; поддерживается базовый тип и `object` как catch-all.
- Обработчики регистрируются под именем типа (`THandler.FullName`), поэтому несколько реализаций для одного сообщения не конфликтуют, в том числе при `ServiceLifetime.Singleton`.
- Ошибка обработчика передаётся всем `IQueueMessageErrorHandler`; при `ContinueOnHandlerError = false` обработка очереди прерывается и ошибка уходит в `UnhandledException`.
- `HandlerTimeout` ограничивает время обработки; `MaxQueueLength` — длину очереди; `IgnoreUnhandledMessages` отключает ошибку для сообщений без обработчиков.
- `PostAsync` возвращает задачу завершения обработки сообщения.

## Регистрация через Saraff.IoC

| Метод | Назначение |
|---|---|
| `RegisterSingleThreadedDispatcher(name, configure)` | Singleton-диспетчер (именованный или по умолчанию) |
| `RegisterSingleThreadedProxy<TService, TImplementation>(dispatcherName, lifetime, args)` | Прокси-сервис, привязанный к диспетчеру |
| `RegisterQueueMessageHandler<TMessage, THandler>(lifetime)` | Обработчик сообщений очереди |
| `RegisterQueueErrorHandler<THandler>(lifetime)` | Обработчик ошибок очереди |

Контейнер освобождает зарегистрированный диспетчер при собственном `Dispose`.

## Диспетчер напрямую

```csharp
using (var dispatcher = new SingleThreadedDispatcher(options: null, services: null)) {
    dispatcher.Send(() => { /* STA */ });
    int value = dispatcher.Invoke(() => 42);
    Task<int> async = dispatcher.InvokeAsync(() => 42 /* поток диспетчера */);
    dispatcher.Post(() => { /* fire-and-forget */ });
}
```

- `SynchronizationContext` диспетчера доступен через свойство `SynchronizationContext`; `SingleThreadedDispatcher.Current` возвращает диспетчер текущего служебного потока.
- `UseMessagePump = false` отключает насос сообщений (например, для фоновых сценариев без COM).
- `Dispose`/`DisposeAsync` останавливают поток; вызовы после остановки дают `ObjectDisposedException`.
- Вызовы `Send`/`Invoke` блокируют вызывающий поток до завершения. Не вызывайте их из UI-потока без необходимости и не ожидайте в обработчике события другого потока, который сам вызывает тот же диспетчер.

## Ограничения

- `SingleThreadedProxy` для `net8.0-windows` поддерживает только интерфейсы; `net462` — интерфейсы и `MarshalByRefObject`.
- События с параметрами `ref`/`out` не поддерживаются (бросается `NotSupportedException`).
- Обработчики событий вызываются синхронно; исключение обработчика пробрасывается вызывающему коду.
- Отмена in-flight вызова `Send` невозможна: `CancellationToken` действует до начала исполнения и для обработчиков очереди.
- `Assembly` больше не подписан: ключ `sign.snk` удалён из репозитория (см. CHANGELOG).

## Примеры

| Проект | Что показывает |
|---|---|
| `samples/Saraff.ThreadingModel.Sample` | Saraff.IoC, именованный диспетчер, прокси, очередь сообщений, scoped-зависимости, обработка ошибок |
| `samples/Saraff.ThreadingModel.Sample.Plain` | Диспетчер и прокси без контейнера, общий диспетчер, события |

## Сборка и тесты

```powershell
dotnet build Saraff.ThreadingModel.sln -c Release
dotnet test tests\Saraff.ThreadingModel.Tests\Saraff.ThreadingModel.Tests.csproj
dotnet run --project samples\Saraff.ThreadingModel.Sample
dotnet run --project samples\Saraff.ThreadingModel.Sample.Plain
```

Тесты прогоняются на `net8.0-windows` (roll-forward на установленный runtime) и `net462`.

## Лицензия

LGPL-3.0-or-later, см. `COPYING.txt` и `COPYING.LESSER.txt`.
