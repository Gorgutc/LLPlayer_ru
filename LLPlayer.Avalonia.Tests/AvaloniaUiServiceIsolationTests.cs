using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public sealed class AvaloniaUiServiceIsolationTests
{
    [AvaloniaFact]
    public async Task TestVm_rejects_worker_thread_construction_before_using_font_services()
    {
        Assert.True(Dispatcher.UIThread.CheckAccess());
        // Initialize the real platform font service on the headless UI thread. The worker below runs while this
        // UI operation is awaiting it, so the regression does not race locator setup or rely on corrupting it.
        _ = FontManager.Current.SystemFonts.ToArray();
        var (main, _, _, _, _) = TestVm.Create();
        using (main)
            Assert.Equal(AppPrefs.DefaultSidebarFontFamily, main.Sidebar.Appearance.FontFamily);

        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
        Task<Exception?> worker = Task.Run(() =>
        {
            workerCancellation.Token.ThrowIfCancellationRequested();
            return Record.Exception(() =>
            {
                var (workerMain, _, _, _, _) = TestVm.Create();
                using (workerMain)
                    Assert.Equal(AppPrefs.DefaultSidebarFontFamily, workerMain.Sidebar.Appearance.FontFamily);
            });
        }, workerCancellation.Token);

        Exception? primaryFailure = null;
        try
        {
            Exception? exception = await worker.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
            Assert.Equal("TestVm.Create must run in an AvaloniaFact or AvaloniaTheory UI-thread context.",
                Assert.IsType<InvalidOperationException>(exception).Message);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            // Cancel a worker that has not started yet, then drain the accepted task before PerTest tears down
            // Avalonia's scope. Cleanup cannot use an already-cancelled test token.
            workerCancellation.Cancel();
            try
            {
                await worker.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch (OperationCanceledException) when (worker.IsCanceled)
            {
            }
            catch (Exception cleanupFailure) when (primaryFailure != null)
            {
                throw new AggregateException("Worker cleanup failed after the test failure.", primaryFailure, cleanupFailure);
            }
        }
    }
}
