using Avalonia.Threading;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services.Automation;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia;

public sealed partial class AppHost
{
    ControlConnection? automationConnection;
    ControlGuiExecutor? automationExecutor;

    void ConfigureAutomation(MainWindow window, Task<bool>? startupMedia = null)
    {
        if (controller == null || viewModel == null || options.ScreenshotPath != null) return;
        if (HasControlHost()) controller.ConfigureControlRecognition(paths);
        else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LLPLAYER_CONTROL_HOST")))
            controller.ConfigureControlAsr(paths);
        var model = viewModel;
        var executor = new ControlGuiExecutor(controller, model, window, launchControlApplication);
        automationExecutor = executor;
        automationConnection = new(paths, new(executor.ExecuteAsync, revision =>
        {
            if (revision == null || controller?.ControlMediaRevision == revision)
                window.RequestApplicationExit();
        }), state =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (automationConnection == null) return;
                ApplyAutomationState(model, state);
                RefreshControlSettings(state);
            });
        }, startupMedia);
    }

    static bool HasControlHost()
    {
        string? overridePath = Environment.GetEnvironmentVariable("LLPLAYER_CONTROL_HOST");
        string hostPath = string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "LLPlayer.ControlHost.exe" : "LLPlayer.ControlHost")
            : overridePath;
        return File.Exists(hostPath);
    }

    void ApplyAutomationState(MainWindowViewModel model, ControlState? state)
    {
        model.AutomationJobsStatus = AutomationStatus(state);
        var rows = model.AutomationJobs;
        var jobs = state?.Jobs.OrderByDescending(job => job.UpdatedAt).ToArray() ?? [];
        var previous = rows.ToDictionary(row => row.JobId);
        for (int i = 0; i < jobs.Length; i++)
        {
            ControlJob job = jobs[i];
            if (!previous.TryGetValue(job.JobId, out AutomationJobRow? row))
            {
                rows.Insert(i, new(job, CancelAutomationJobAsync));
                continue;
            }
            row.Refresh(job);
            int current = rows.IndexOf(row);
            if (current != i) rows.Move(current, i);
        }
        while (rows.Count > jobs.Length) rows.RemoveAt(rows.Count - 1);
    }

    async Task CancelAutomationJobAsync(ControlJob job)
    {
        try
        {
            using ControlClient client = await ControlClient.ConnectOrStartAsync(paths.ConfigDir, paths.StateDir);
            ControlResult result = await client.CancelAsync(new(job.JobId, job.Scope));
            if (result.Error != null) throw new ControlException(result.Error);
        }
        catch (Exception error)
        {
            viewModel?.ShowToast("Could not cancel the job", ControlGuiExecutor.PublicError(error), ToastVariant.Destructive);
        }
    }

    static string AutomationStatus(ControlState? state)
    {
        ControlJob? latest = state?.Jobs.OrderByDescending(job => job.UpdatedAt).FirstOrDefault();
        if (latest == null) return "";
        int active = state!.Jobs.Count(job => job.Status is ControlJobStatus.Accepted or ControlJobStatus.WaitingForResource
            or ControlJobStatus.Running or ControlJobStatus.AwaitingTranslation
            or ControlJobStatus.Cancelling or ControlJobStatus.AwaitingCleanup);
        string progress = latest.Progress is double amount ? $" ({amount:P0})" : "";
        return $"Automation jobs: {active} active · {latest.OperationId}: {latest.Status}{progress}";
    }

    async Task DetachAutomationAsync()
    {
        controller?.DetachControlRecognition();
        ControlConnection? connection = automationConnection;
        automationConnection = null;
        try { if (connection != null) await connection.DisposeAsync(); }
        finally
        {
            automationExecutor?.ClearSubtitleDownloadSession();
            automationExecutor = null;
        }
    }
}
