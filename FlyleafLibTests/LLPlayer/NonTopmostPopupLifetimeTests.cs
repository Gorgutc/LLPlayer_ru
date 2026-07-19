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

            rootedControl = CreateAndAttach(release: false);
            WeakReference[] releasedControls = Enumerable.Range(0, 20)
                .Select(_ => CreateAndAttach(release: true))
                .ToArray();

            CollectFully();

            rootedControl.IsAlive.Should().BeTrue(
                "the control probe must detect the MainWindow event root when Unloaded is omitted");
            foreach (WeakReference releasedControl in releasedControls)
            {
                releasedControl.IsAlive.Should().BeFalse(
                    "Unloaded must remove the child and MainWindow handlers that rooted discarded sidebar trees");
            }
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
    private static WeakReference CreateAndAttach(bool release)
    {
        NonTopmostPopup popup = new() { Child = new Border() };
        popup.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, popup));
        WeakReference reference = new(popup);

        if (release)
        {
            popup.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, popup));
        }

        return reference;
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
