using System.Text.Json;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Services.Automation;

internal sealed partial class ControlGuiExecutor
{
    object? ExecuteLifecycle(ControlRequest request, CancellationToken token)
    {
        if (request.OperationId == "commands.execute")
        {
            var command = request.Arguments.Deserialize<ControlExecuteCommand>(ControlProtocol.Json)
                ?? throw Invalid("Supply the ID of a supported palette command.");
            return ExecuteObservedCommand(command.Id);
        }

        if (!ControlGuiLifecycleOperations.IsApplicationLifecycle(request.OperationId)) return null;
        token.ThrowIfCancellationRequested();
        if (launchApplication == null) throw Unavailable("This window does not own application process commands.");
        if (request.OperationId == "app.exit")
            return new ControlGuiAction(true); // ControlConnection hands off shutdown only after the broker acknowledges this result.

        var launch = request.Arguments.Deserialize<ControlApplicationLaunchTicket>(ControlProtocol.Json);
        if (launch == null || launch.LaunchId == Guid.Empty) throw Invalid("The broker must supply a launch identity.");

        ApplicationLaunchKind kind = request.OperationId switch
        {
            "app.new" => ApplicationLaunchKind.New,
            "app.clone" => ApplicationLaunchKind.Clone,
            _ => ApplicationLaunchKind.Restart,
        };
        long revision = player.ControlMediaRevision;
        return new ControlApplicationStarted(launchApplication(kind, launch.LaunchId), revision);
    }

    static bool IsObservedCommand(string id)
        => id is "engine:TogglePlayPause" or "engine:ToggleMute" or "engine:ToggleFullScreen"
            || id == "app:" + AppShortcutMap.ToggleSidebar;

    ControlExecutedCommand ExecuteObservedCommand(string id)
    {
        if (!IsObservedCommand(id) || !model.BuildExecutableCommands().Any(command => command.Id == id && command.CanExecute))
            throw new ControlException(new("command_unavailable", "The selected palette command has no available observed execution."));

        switch (id)
        {
            case "engine:TogglePlayPause":
                RequireMedia();
                if (player.IsPlaying) player.Pause();
                else player.Play();
                return new(id, Playback: Playback());
            case "engine:ToggleMute":
                player.Mute = !player.Mute;
                return new(id, Playback: Playback());
            case "engine:ToggleFullScreen":
                window.IsFullScreen = !window.IsFullScreen;
                return new(id, Window: WindowState());
            default:
                model.ToggleSidebar();
                return new(id, Window: WindowState());
        }
    }
}
