using System.Text.Json.Serialization;

namespace LLPlayer.ControlApi;

public sealed record ControlApplicationLaunchTicket(Guid LaunchId);
public sealed record ControlApplicationStarted(int ProcessId, long SourceRevision);
public sealed record ControlApplicationWindow(Guid SessionId, int ProcessId, DateTimeOffset AttachedAt);
public sealed record ControlApplicationLifecycleResult(Guid SourceSessionId, bool SourceExited,
    ControlApplicationWindow? Replacement = null);
public sealed record ControlExecuteCommand(string Id);
public sealed record ControlExecutedCommand(string Id, ControlPlaybackSnapshot? Playback = null,
    ControlGuiUiSnapshot? Window = null, ControlUiSurfaceStatus? Surface = null);

public enum ControlUiSurface
{
    CommandPalette, DebugOverlay, CheatSheet, About, Settings, PlaybackSettings, PluginOptions, Playlist,
    WordManager, SubtitleExport, SubtitleDownload, AiInsights, Batch, PdicSettings, OnlineVideo,
    Translation, Recognition, Dubbing, AudioCapture, VideoCapture,
}

public sealed record ControlUiSurfaceRequest([property: JsonRequired] ControlUiSurface Surface);
public sealed record ControlUiSurfaceStatus(ControlUiSurface Surface, bool Visible, bool Available);

public static class ControlGuiLifecycleOperations
{
    public static IReadOnlyList<ControlGuiOperationSchema> All { get; } =
    [
        Lifecycle("app.new", "Open a new player with the selected window's launch settings and wait for it to attach."),
        Lifecycle("app.clone", "Open the selected window's source in a new player and wait for the new window to attach."),
        Lifecycle("app.restart", "Wait for a replacement player to attach and then for the selected player to exit."),
        Lifecycle("app.exit", "Quit the selected player and wait for its process to exit."),
        new(new("commands.execute", "commands", "Execute an explicitly supported palette command and read the resulting state.",
            true, ControlOperationCompletion.Job, ControlOperationContext.Gui),
            typeof(ControlExecuteCommand), typeof(ControlExecutedCommand)),
        new(new("ui.surface.list", "ui", "Read the visible and available tools of the selected window.",
            false, ControlOperationCompletion.Immediate, ControlOperationContext.Gui),
            typeof(ControlGuiOperations.Empty), typeof(ControlUiSurfaceStatus[])),
        new(new("ui.surface.open", "ui", "Open or restore a supported tool of the selected window and report its visibility.",
            true, ControlOperationCompletion.Job, ControlOperationContext.Gui),
            typeof(ControlUiSurfaceRequest), typeof(ControlUiSurfaceStatus)),
        new(new("ui.sidebar.search.focus", "ui", "Show the selected window's sidebar and focus subtitle search.",
            true, ControlOperationCompletion.Job, ControlOperationContext.Gui),
            typeof(ControlGuiOperations.Empty), typeof(ControlGuiUiSnapshot)),
    ];

    public static bool IsApplicationLifecycle(string id)
        => id is "app.new" or "app.clone" or "app.restart" or "app.exit";

    private static ControlGuiOperationSchema Lifecycle(string id, string description)
        => new(new(id, "app", description, true, ControlOperationCompletion.Job, ControlOperationContext.Gui),
            typeof(ControlGuiOperations.Empty), typeof(ControlApplicationLifecycleResult));
}
