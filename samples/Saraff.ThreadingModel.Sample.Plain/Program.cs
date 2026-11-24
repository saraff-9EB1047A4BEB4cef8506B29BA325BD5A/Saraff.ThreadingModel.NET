using System;
using System.Threading;
using Saraff.ThreadingModel;

using(var dispatcher = new SingleThreadedDispatcher(new SingleThreadedDispatcherOptions { ThreadName = "plain STA" }))
using(var proxy = new SingleThreadedProxy(typeof(IClock), typeof(SingleThreadedClock), dispatcher)) {    var clock = proxy.GetTransparentProxy<IClock>();
    clock.Ticked += (sender, args) =>
        Console.WriteLine($"  [event]   {args.Ticks} тиков, поток {Thread.CurrentThread.ManagedThreadId}");

    Console.WriteLine($"Основной поток: {Thread.CurrentThread.ManagedThreadId}");
    Console.WriteLine($"Поток объекта: {clock.ThreadId}, апартамент: {clock.Apartment}");
    Console.WriteLine($"Tick() => {clock.Tick()}");
    Console.WriteLine($"Tick() => {clock.Tick()}");
    Console.WriteLine("Прокси освобождён; диспетчер общий и продолжает работать.");
}

public interface IClock {
    int ThreadId {
        get;
    }

    ApartmentState Apartment {
        get;
    }

    long Tick();

    event EventHandler<TickEventArgs>? Ticked;
}

public sealed class TickEventArgs : EventArgs {
    public TickEventArgs(long ticks) {
        this.Ticks = ticks;
    }

    public long Ticks {
        get;
    }
}

public sealed class SingleThreadedClock : IClock {
    private long _ticks;

    public event EventHandler<TickEventArgs>? Ticked;

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

    public long Tick() {
        this._ticks++;
        Thread.Sleep(50);
        this.Ticked?.Invoke(this, new TickEventArgs(this._ticks));
        return this._ticks;
    }
}
