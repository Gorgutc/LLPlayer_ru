using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class MainWindow
{
    AiInsightsWindow? insightsWindow;
    DubbingWindow? dubbingWindow;
    VoicePickerWindow? voicePickerWindow;
    ModelManagerWindow? modelManagerWindow;
    BatchSubtitlesWindow? batchWindow;
    public Func<bool>? KeepAliveOnClose { get; set; }
    public Action<Window>? BackgroundRequested { get; set; }
    public Action? ShutdownStarting { get; set; }
    bool explicitQuit;
    Task? applicationShutdown;

    public Task RequestApplicationExitAsync()
    {
        RequestApplicationExit();
        return applicationShutdown ?? Task.CompletedTask;
    }

    public void RequestApplicationExit()
    {
        explicitQuit = true;
        if (ShutdownRequested == null) Close();
        else BeginApplicationShutdown();
    }

    void BeginApplicationShutdown()
    {
        if (shutdownStarted || shutdownComplete) return;
        shutdownStarted = true;
        ClearCommandButtonKeys();
        vm?.BeginWordActionsShutdown();
        vm?.CancelAppShortcuts();
        ShutdownStarting?.Invoke();
        DisableLanguageWindows();
        IsEnabled = false;
        applicationShutdown = DrainBeforeClosingAsync();
    }

    void RefreshLanguageClosingBehavior(Window? closingWindow = null)
    {
        Window[] owned = OwnedWindows.Where(child => !ReferenceEquals(child, closingWindow)).ToArray();
        // Only these root-owned windows are covered by the language drain. Legacy dialogs retain their
        // existing close veto; a nested manager belongs to Recognition and keeps that owner's semantics.
        ClosingBehavior = owned.Length > 0 && owned.All(child => child is RecognitionWindow or ModelManagerWindow
            or AiInsightsWindow or DubbingWindow or VoicePickerWindow or CaptureDevicesWindow
            or CommandPaletteWindow or AboutWindow or PlayerDebugWindow or CheatSheetWindow)
            ? WindowClosingBehavior.OwnerWindowOnly : WindowClosingBehavior.OwnerAndChildWindows;
    }

    void DisableLanguageWindows()
    {
        foreach (Window? child in new Window?[] { insightsWindow, dubbingWindow, voicePickerWindow, modelManagerWindow, batchWindow, recognitionWindow })
            if (child != null) child.IsEnabled = false;
    }

    public void ShowAiInsights(AiInsightsViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (insightsWindow != null) { insightsWindow.Activate(); return; }
        insightsWindow = new AiInsightsWindow(model);
        OwnKeyboardSurface(insightsWindow);
        insightsWindow.Closed += (_, _) => { insightsWindow = null; vm?.AiInsightsClosed(); };
        insightsWindow.Show(this);
    }

    public void ShowDubbing(DubbingViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (dubbingWindow != null) { dubbingWindow.Activate(); return; }
        dubbingWindow = new DubbingWindow(model);
        OwnKeyboardSurface(dubbingWindow);
        dubbingWindow.Closed += (_, _) => dubbingWindow = null;
        _ = dubbingWindow.ShowDialog(this);
    }

    public void ShowVoicePicker(VoicePickerViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (voicePickerWindow != null) { voicePickerWindow.Activate(); return; }
        voicePickerWindow = new VoicePickerWindow(model);
        OwnKeyboardSurface(voicePickerWindow);
        voicePickerWindow.Closed += (_, _) => voicePickerWindow = null;
        _ = voicePickerWindow.ShowDialog(this);
    }

    public async Task ShowModelManagerAsync(ModelManagerViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (modelManagerWindow != null)
        {
            await model.DisposeAsync();
            modelManagerWindow.Activate();
            return;
        }
        var window = new ModelManagerWindow(model);
        modelManagerWindow = window;
        OwnKeyboardSurface(window);
        try { await window.ShowDialog(recognitionWindow ?? (Window)this); }
        finally
        {
            await model.DisposeAsync();
            if (ReferenceEquals(modelManagerWindow, window)) modelManagerWindow = null;
        }
    }

    public void ShowBatch(BatchSubtitlesViewModel model)
    {
        vm?.CancelAppShortcuts();
        if (batchWindow != null) { batchWindow.Restore(); return; }
        batchWindow = new BatchSubtitlesWindow(model);
        OwnKeyboardSurface(batchWindow);
        batchWindow.BackgroundRequested = window => BackgroundRequested?.Invoke(window);
        batchWindow.Closed += (_, _) => batchWindow = null;
        batchWindow.Show();
    }

    public async Task<string?> PickFolderAsync(string? initialFolder)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Choose batch folder", AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    async Task DrainLanguageWindowsAsync()
    {
        if (modelManagerWindow?.DataContext is ModelManagerViewModel models) await models.DisposeAsync();
        if (insightsWindow?.DataContext is AiInsightsViewModel ai) await ai.DisposeAsync();
        if (dubbingWindow?.DataContext is DubbingViewModel dubbing && dubbing.SaveCommand.ExecutionTask is { } save)
            await save;
        if (voicePickerWindow?.DataContext is VoicePickerViewModel voice)
            await Task.WhenAll(voice.SaveCommand.ExecutionTask ?? Task.CompletedTask,
                voice.UseDefaultCommand.ExecutionTask ?? Task.CompletedTask);
    }

    void CloseLanguageWindows()
    {
        if (batchWindow != null) { batchWindow.IsShuttingDown = true; batchWindow.Close(); }
        insightsWindow?.Close(); modelManagerWindow?.Close(); dubbingWindow?.Close(); voicePickerWindow?.Close();
    }

    void OnCueVoiceClick(object? sender, RoutedEventArgs args)
    {
        // Avalonia invokes the bound command after an unhandled Click. The command also checks the current
        // row/source; consume only stale visuals so a normal pointer or automation click can open the picker.
        if (sender is not Button { CommandParameter: CueItemViewModel row } || vm?.Sidebar.IsCurrentRow(row) != true)
            args.Handled = true;
    }
}
