using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace LLPlayer.Avalonia.Services;

/// <summary>Optional desktop affordances. Their failure never changes queue ownership or job results.</summary>
public sealed class BatchDesktopFeedback : IAsyncDisposable
{
    readonly CancellationTokenSource cancellation = new();
    Task pending = Task.CompletedTask;
    bool disposed;
    public string? LastWarning { get; private set; }
    public void Cancel() { if (!disposed) cancellation.Cancel(); }
    public void Notify(string message)
    {
        if (disposed) return;
        pending = NotifyAfterAsync(pending, message, cancellation.Token);
    }
    async Task NotifyAfterAsync(Task previous, string message, CancellationToken token)
    {
        await previous;
        try
        {
            token.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows()) await Task.Run(() =>
            {
                if (OperatingSystem.IsWindows()) NotifyWindows(message, token);
            }, token);
            else if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/notify-send"))
            {
                var info = new ProcessStartInfo("/usr/bin/notify-send") { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("--app-name=LLPlayer"); info.ArgumentList.Add("--expire-time=4000");
                info.ArgumentList.Add("LLPlayer"); info.ArgumentList.Add(message);
                using var process = Process.Start(info) ?? throw new InvalidOperationException("Desktop notification could not start.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
                if (process.ExitCode != 0) LastWarning = "The desktop notification service rejected the completion notification. Batch status remains in the queue and tray.";
            }
            else LastWarning = "Desktop notifications are unavailable. Batch status remains in the queue and tray.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LastWarning = $"Desktop notification is unavailable: {ex.Message}"; }
    }
    public static void UpdateTaskbar(Window window, bool running, double progress)
    {
        window.Title = running ? $"Batch Subtitles — {progress:P0} — LLPlayer" : "Batch Subtitles — LLPlayer";
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) return;
        try { SetWindowsProgress(handle, running, progress); }
        catch (Exception ex) when (ex is COMException or InvalidCastException or PlatformNotSupportedException) { /* Title remains the accessible fallback. */ }
    }
    [SupportedOSPlatform("windows")]
    static void SetWindowsProgress(IntPtr handle, bool running, double progress)
    {
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), throwOnError: true)!);
            var taskbar = (ITaskbarList3)instance!; taskbar.HrInit();
            taskbar.SetProgressState(handle, running ? 2u : 0u);
            if (running) taskbar.SetProgressValue(handle, (ulong)(Math.Clamp(double.IsFinite(progress) ? progress : 0, 0, 1) * 1000), 1000);
        }
        finally { if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
    }
    [SupportedOSPlatform("windows")]
    static void NotifyWindows(string message, CancellationToken token)
    {
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("0010890e-8789-413c-adbc-48f5b511b3af"), throwOnError: true)!);
            var notification = (IUserNotification)instance!;
            notification.SetBalloonInfo("LLPlayer", message, 1 | 0x80); // Respect Windows quiet time.
            notification.SetBalloonRetry(4000, 0, 0);
            notification.SetIconInfo(IntPtr.Zero, "LLPlayer");
            notification.Show(new ContinueQuery(token), 100);
        }
        finally { if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) { await pending; return; }
        disposed = true; cancellation.Cancel(); await pending; cancellation.Dispose();
    }
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList3
    {
        void HrInit(); void AddTab(IntPtr window); void DeleteTab(IntPtr window); void ActivateTab(IntPtr window); void SetActiveAlt(IntPtr window);
        void MarkFullscreenWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr window, ulong completed, ulong total); void SetProgressState(IntPtr window, uint state);
    }
    [ComImport, Guid("ba9711ba-5893-4787-a7e1-41277151550b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUserNotification
    {
        void SetBalloonInfo([MarshalAs(UnmanagedType.LPWStr)] string title, [MarshalAs(UnmanagedType.LPWStr)] string text, uint flags);
        void SetBalloonRetry(uint showTime, uint interval, uint retryCount);
        void SetIconInfo(IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string toolTip);
        void Show(IQueryContinue query, uint interval);
        void PlaySound([MarshalAs(UnmanagedType.LPWStr)] string sound);
    }
    [ComVisible(true), Guid("7307055c-b24a-486b-9f25-163e597a28a9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IQueryContinue { [PreserveSig] int QueryContinue(); }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class ContinueQuery(CancellationToken token) : IQueryContinue
    { public int QueryContinue() => token.IsCancellationRequested ? 1 : 0; }
}
