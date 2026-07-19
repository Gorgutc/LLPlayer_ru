using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using LLPlayer.Controls;

namespace FlyleafLibTests.LLPlayer;

[CollectionDefinition("WpfApplicationLifetime", DisableParallelization = true)]
public sealed class WpfApplicationLifetimeCollection
{
}

[Collection("WpfApplicationLifetime")]
public class NonTopmostPopupLifetimeTests
{
    [Fact]
    public void Unloaded_detaches_main_window_handlers_and_releases_popup()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                VerifyLifetimeOnStaThread();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        thread.Join(TimeSpan.FromSeconds(15)).Should().BeTrue("the WPF lifetime probe must not hang");
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void VerifyLifetimeOnStaThread()
    {
        Application? app = null;
        WeakReference? rootedControl = null;
        try
        {
            app = new Application();
            app.MainWindow = new Window();

            rootedControl = CreateMainWindowRootControl();
            (WeakReference Popup, Border Child)[] releasedProbes = Enumerable.Range(0, 20)
                .Select(_ => CreateAndAttach(release: true))
                .ToArray();

            CollectFully();

            rootedControl.IsAlive.Should().BeTrue(
                "the control probe must detect the MainWindow event root when Unloaded is omitted");
            foreach ((WeakReference releasedPopup, Border _) in releasedProbes)
            {
                releasedPopup.IsAlive.Should().BeFalse(
                    "Unloaded must remove the child and MainWindow handlers that rooted discarded sidebar trees");
            }

            // Keep every child externally rooted through the collection checks. Otherwise a leaked child-handler
            // cycle could be collected together and the test would prove only MainWindow-handler removal.
            GC.KeepAlive(releasedProbes);
        }
        finally
        {
            if (rootedControl?.Target is NonTopmostPopup rootedPopup)
            {
                rootedPopup.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, rootedPopup));
            }

            if (app != null)
            {
                app.MainWindow = null;
                app.Shutdown();
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateMainWindowRootControl()
    {
        NonTopmostPopup popup = new() { Child = new Border() };
        popup.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, popup));
        WeakReference reference = new(popup);

        // Remove the child-to-logical-parent path and let the child/handler cycle become unreachable. Because
        // Unloaded was intentionally omitted, only the MainWindow event subscriptions should retain this popup.
        popup.Child = null;
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Popup, Border Child) CreateAndAttach(bool release)
    {
        Border child = new();
        NonTopmostPopup popup = new() { Child = child };
        popup.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, popup));
        WeakReference reference = new(popup);

        if (release)
        {
            popup.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, popup));
            popup.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, popup));
            popup.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, popup));
            // Break the normal child->logical-parent link only in the probe. The externally rooted child will
            // still keep the popup alive if OnPopupUnloaded failed to remove its routed-event handler.
            popup.Child = null;
        }

        return (reference, child);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectFully()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }
}
