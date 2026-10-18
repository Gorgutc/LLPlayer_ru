using Avalonia.Threading;
using FlyleafLib;

namespace LLPlayer.Avalonia.Services;

/// <summary>FlyleafLib's portable UI-thread seam (<see cref="Utils.UIDispatcher"/>) backed by Avalonia's UI dispatcher.</summary>
public sealed class AvaloniaUIDispatcher(Dispatcher dispatcher) : IUIDispatcher
{
    public AvaloniaUIDispatcher() : this(Dispatcher.UIThread)
    {
    }

    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Post(Action action) => dispatcher.Post(action, DispatcherPriority.Normal);

    public void Invoke(Action action)
    {
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action, DispatcherPriority.Send);
    }
}
