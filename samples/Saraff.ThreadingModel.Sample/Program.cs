using System;
using System.Threading;
using System.Threading.Tasks;
using Saraff.IoC;
using Saraff.ThreadingModel;
using Saraff.ThreadingModel.DependencyInjection;

using(var container = new ServiceContainer()) {
    container.RegisterSingleThreadedDispatcher("scanner", options => {
        options.ThreadName = "SARAFF STA Scanner";
        options.HandlerTimeout = TimeSpan.FromSeconds(10);
        options.DisposeTimeout = TimeSpan.FromSeconds(5);
    });
    container.RegisterSingleThreadedProxy<IScanner, Scanner>("scanner");
    container.Register<ScanSession, ScanSession>(ServiceLifetime.Scoped);
    container.RegisterQueueMessageHandler<ScanRequest, ScanRequestHandler>();
    container.RegisterQueueErrorHandler<ConsoleErrorHandler>(ServiceLifetime.Singleton);

    var dispatcher = container.ResolveNamed<SingleThreadedDispatcher>("scanner");
    var scanner = container.Resolve<IScanner>();

    scanner.Completed += (sender, args) =>
        Console.WriteLine($"  [event]   страниц: {args.Pages}, поток: {Thread.CurrentThread.ManagedThreadId}");

    Console.WriteLine($"Основной поток: {Thread.CurrentThread.ManagedThreadId}");
    Console.WriteLine($"Поток STA-объекта: {scanner.ThreadId}, апартамент: {scanner.Apartment}");
    Console.WriteLine($"Прямой вызов Scan(2) => {scanner.Scan(2)}");

    Console.WriteLine("Вызов из пула потоков:");
    var fromPool = await Task.Run(() => scanner.Scan(1));
    Console.WriteLine($"  [pool]    поток: {Thread.CurrentThread.ManagedThreadId}, результат: {fromPool}");

    Console.WriteLine("Очередь сообщений:");
    dispatcher.Post(new ScanRequest(3, "invoice"));
    dispatcher.Post(new ScanRequest(5, "contract"));
    await dispatcher.PostAsync(new ScanRequest(7, "passport"));
    dispatcher.Post(new ScanRequest(-1, "broken"));
    await Task.Delay(300);

    Console.WriteLine("Завершение: контейнер освобождает диспетчер и scoped-сессии.");
}

public interface IScanner {
    string Name {
        get;
    }

    int ThreadId {
        get;
    }

    ApartmentState Apartment {
        get;
    }

    int Scan(int pages);

    event EventHandler<ScanCompletedEventArgs>? Completed;
}

public sealed class ScanCompletedEventArgs : EventArgs {
    public ScanCompletedEventArgs(int pages) {
        this.Pages = pages;
    }

    public int Pages {
        get;
    }
}

public sealed class Scanner : IScanner {
    public event EventHandler<ScanCompletedEventArgs>? Completed;

    public string Name {
        get {
            return "SARAFF Scanner";
        }
    }

    public int ThreadId {
        get {
            return Thread.CurrentThread.ManagedThreadId;
        }
    }

    public ApartmentState Apartment {
        get {
            return Thread.CurrentThread.GetApartmentState();
        }
    }

    public int Scan(int pages) {
        Thread.Sleep(100);
        this.Completed?.Invoke(this, new ScanCompletedEventArgs(pages));
        return pages * 100;
    }
}

public sealed class ScanRequest {
    public ScanRequest(int pages, string name) {
        this.Pages = pages;
        this.Name = name;
    }

    public int Pages {
        get;
    }

    public string Name {
        get;
    }
}

public sealed class ScanSession : IDisposable {
    private readonly int _id = Environment.CurrentManagedThreadId;

    public void Log(string text) {
        Console.WriteLine($"  [session] {text}");
    }

    public void Dispose() {
        Console.WriteLine($"  [session] освобождена (поток {this._id})");
    }
}

public sealed class ScanRequestHandler : IQueueMessageHandler<ScanRequest> {
    private readonly IScanner _scanner;
    private readonly ScanSession _session;

    [ServiceRequired]
    public ScanRequestHandler(IScanner scanner, ScanSession session) {
        this._scanner = scanner;
        this._session = session;
    }

    public Task HandleAsync(ScanRequest message, CancellationToken cancellationToken) {
        if(message.Pages <= 0) {
            throw new InvalidOperationException("Некорректное число страниц: " + message.Pages + ".");
        }
        this._session.Log("'" + message.Name + "': сканирование " + message.Pages + " стр., поток " + Thread.CurrentThread.ManagedThreadId);
        var result = this._scanner.Scan(message.Pages);
        Console.WriteLine($"  [handler] '{message.Name}': результат {result}");
        return Task.CompletedTask;
    }
}

public sealed class ConsoleErrorHandler : IQueueMessageErrorHandler {
    public Task HandleAsync(object message, Exception error, CancellationToken cancellationToken) {
        Console.WriteLine($"  [error]   {message.GetType().Name}: {error.GetType().Name}: {error.Message}");
        return Task.CompletedTask;
    }
}
