using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using FlyleafLib;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// Bridges FlyleafLib's cross-thread observable collections to the Avalonia UI thread.
/// <para>
/// The engine mutates collections such as <c>SubManager.Subs</c> or the demuxer stream lists on background threads
/// under a lock it registers through <c>BindingOperations.EnableCollectionSynchronization</c> (WPF synchronizes bound
/// views with that lock). Avalonia has no such mechanism, so UI code never binds those collections directly: it
/// <see cref="Snapshot{T}"/>s them under the registered lock and <see cref="Watch"/>es them, receiving a coalesced
/// notification on the UI thread after changes (and after the engine's <c>CollectionView.Refresh()</c> requests).
/// </para>
/// </summary>
public sealed class CollectionSyncBridge
{
    readonly ConditionalWeakTable<IEnumerable, object> locks = new();
    readonly ConditionalWeakTable<IEnumerable, WatchList> watchers = new();
    readonly IUIDispatcher? dispatcher;

    public CollectionSyncBridge(IUIDispatcher? dispatcher) => this.dispatcher = dispatcher;

    /// <summary>Installs this bridge as FlyleafLib's BindingOperations / CollectionViewSource handler.</summary>
    public void Install()
    {
        BindingOperations.CollectionSynchronizationHandler = Register;
        CollectionViewSource.RefreshHandler = NotifyRefresh;
    }

    public void Register(IEnumerable collection, object lockObject)
    {
        locks.AddOrUpdate(collection, lockObject);
    }

    public bool IsRegistered(IEnumerable collection) => locks.TryGetValue(collection, out _);

    /// <summary>Copies <paramref name="collection"/> under its registered lock (or its SyncRoot/itself when unregistered).</summary>
    public List<T> Snapshot<T>(IEnumerable<T> collection)
    {
        object gate = locks.TryGetValue(collection, out object? l) ? l : (collection as ICollection)?.SyncRoot ?? collection;
        // A snapshot can race a writer that does not take the lock (a few engine paths); retry instead of failing.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                lock (gate)
                    return [.. collection];
            }
            catch (InvalidOperationException) when (attempt < 3)
            {
                Thread.Yield();
            }
        }
    }

    /// <summary>
    /// Calls <paramref name="onChanged"/> on the UI thread (coalesced: at most one pending call per watcher) after
    /// every change of <paramref name="collection"/>. Dispose the result to stop watching.
    /// </summary>
    public IDisposable Watch<TCollection>(TCollection collection, Action onChanged)
        where TCollection : class, IEnumerable, INotifyCollectionChanged
    {
        WatchList list = watchers.GetValue(collection, c => new WatchList((INotifyCollectionChanged)c));
        Watcher w = new(this, list, onChanged);
        list.Add(w);
        return w;
    }

    void NotifyRefresh(IEnumerable collection)
    {
        if (watchers.TryGetValue(collection, out WatchList? list))
            list.RaiseAll();
    }

    void Post(Action action)
    {
        if (dispatcher == null)
            action();
        else
            dispatcher.Post(action);
    }

    sealed class WatchList
    {
        readonly INotifyCollectionChanged source;
        readonly List<Watcher> items = [];
        readonly Lock gate = new();

        public WatchList(INotifyCollectionChanged source)
        {
            this.source = source;
        }

        public void Add(Watcher w)
        {
            lock (gate)
            {
                if (items.Count == 0)
                    source.CollectionChanged += OnChanged;
                items.Add(w);
            }
        }

        public void Remove(Watcher w)
        {
            lock (gate)
            {
                if (items.Remove(w) && items.Count == 0)
                    source.CollectionChanged -= OnChanged;
            }
        }

        void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => RaiseAll();

        public void RaiseAll()
        {
            Watcher[] copy;
            lock (gate)
                copy = [.. items];
            foreach (Watcher w in copy)
                w.Raise();
        }
    }

    sealed class Watcher(CollectionSyncBridge bridge, WatchList list, Action onChanged) : IDisposable
    {
        int pending;
        volatile bool disposed;

        public void Raise()
        {
            if (disposed || Interlocked.Exchange(ref pending, 1) == 1)
                return;

            bridge.Post(() =>
            {
                Volatile.Write(ref pending, 0);
                if (!disposed)
                    onChanged();
            });
        }

        public void Dispose()
        {
            disposed = true;
            list.Remove(this);
        }
    }
}
