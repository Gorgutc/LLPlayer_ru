using System.Collections;

// F-13 portable (Linux) TFM only: stand-in for WPF's System.Windows.Data.BindingOperations so the shared engine code
// that registers its observable collections for cross-thread UI binding compiles unchanged.
namespace System.Windows.Data;

/// <summary>
/// Portable counterpart of WPF <c>BindingOperations</c>. WPF synchronizes bound collections with the supplied lock;
/// other UI toolkits have no such mechanism, so registrations are forwarded to <see cref="CollectionSynchronizationHandler"/>
/// (when the host sets one) and otherwise ignored.
/// </summary>
public static class BindingOperations
{
    /// <summary>
    /// Optional host hook receiving every (collection, lock) pair the engine registers. Collections are mutated from
    /// engine threads under that lock; UI hosts must marshal change notifications to their UI thread themselves.
    /// </summary>
    public static Action<IEnumerable, object> CollectionSynchronizationHandler { get; set; }

    /// <summary>Registers <paramref name="collection"/> as being guarded by <paramref name="lockObject"/>.</summary>
    public static void EnableCollectionSynchronization(IEnumerable collection, object lockObject)
        => CollectionSynchronizationHandler?.Invoke(collection, lockObject);
}

/// <summary>
/// Portable counterpart of WPF <c>CollectionViewSource.GetDefaultView</c>: returns one <see cref="CollectionView"/>
/// per source collection (so a host-set <see cref="CollectionView.Filter"/> persists) whose <see cref="CollectionView.Refresh"/>
/// is forwarded to <see cref="RefreshHandler"/>.
/// </summary>
public static class CollectionViewSource
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, CollectionView> views = new();

    /// <summary>Optional host hook invoked (on the UI thread) when the engine asks for a view refresh of a collection.</summary>
    public static Action<IEnumerable> RefreshHandler { get; set; }

    public static CollectionView GetDefaultView(object source)
        => source == null ? null : views.GetValue(source, s => new CollectionView((IEnumerable)s));
}

/// <summary>Minimal default view of a collection (filter predicate + refresh notification).</summary>
public sealed class CollectionView
{
    internal CollectionView(IEnumerable sourceCollection) => SourceCollection = sourceCollection;

    public IEnumerable SourceCollection { get; }

    /// <summary>Filter predicate set by the host UI (e.g. a search box); the engine only reads it.</summary>
    public Predicate<object> Filter { get; set; }

    public void Refresh() => CollectionViewSource.RefreshHandler?.Invoke(SourceCollection);
}
