using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.Services.Automation;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Tests.Automation;

public sealed class ControlGuiLifecycleTests
{
    [Fact]
    public void Lifecycle_operations_are_described_as_targeted_observable_jobs()
    {
        foreach (string id in new[] { "app.new", "app.clone", "app.restart", "app.exit", "commands.execute" })
        {
            var schema = Assert.Single(ControlGuiOperations.All, item => item.Operation.Id == id);
            Assert.Equal(ControlOperationContext.Gui, schema.Operation.Context);
            Assert.Equal(ControlOperationCompletion.Job, schema.Operation.Completion);
            Assert.True(schema.Operation.Mutates);
        }
    }

    [Fact]
    public void Clone_and_restart_keep_launch_options_and_current_media_with_a_single_correlation_ticket()
    {
        var context = new ApplicationLaunchContext("dotnet", "working",
            "LLPlayer.Avalonia.dll", ["--ffmpeg-dir", "native libraries", "--theme", "dark", "--sidebar"]);
        const string media = "https://example.invalid/movie?q=1";
        var ticket = Guid.NewGuid();

        foreach (var kind in new[] { ApplicationLaunchKind.Clone, ApplicationLaunchKind.Restart })
        {
            var start = ApplicationProcessPlanner.Create(kind, context, media, ticket);
            Assert.Equal(new[] { "LLPlayer.Avalonia.dll", "--ffmpeg-dir", "native libraries", "--theme", "dark",
                "--sidebar", "--", media }, start.ArgumentList);
            Assert.Equal(ticket.ToString("D"), start.Environment[ControlProtocol.GuiLaunchIdVariable]);
        }

        var fresh = ApplicationProcessPlanner.Create(ApplicationLaunchKind.New, context, media, ticket);
        Assert.DoesNotContain(media, fresh.ArgumentList);
        Assert.DoesNotContain("--", fresh.ArgumentList);
        Assert.Equal(ticket.ToString("D"), fresh.Environment[ControlProtocol.GuiLaunchIdVariable]);
    }

    [Fact]
    public async Task Correlated_registration_observes_startup_media_result_and_ordinary_registration_does_not_wait()
    {
        var token = TestContext.Current.CancellationToken;
        var nonce = Guid.NewGuid();
        var open = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var correlated = ControlConnection.PrepareRegistrationAsync(nonce, open.Task, token);
        Assert.False(correlated.IsCompleted);
        var ordinary = await ControlConnection.PrepareRegistrationAsync(null, open.Task, token);
        Assert.Null(ordinary.LaunchSourceOpened);

        open.SetResult(false);
        var failed = await correlated;
        Assert.Equal(nonce, failed.LaunchId);
        Assert.False(failed.LaunchSourceOpened);
        var success = await ControlConnection.PrepareRegistrationAsync(nonce, Task.FromResult(true), token);
        Assert.True(success.LaunchSourceOpened);
    }

    [AvaloniaFact]
    public async Task Quit_is_handed_to_the_window_only_after_the_scoped_result_is_acknowledged()
    {
        var session = Guid.NewGuid();
        var request = new ControlRequest(Guid.NewGuid(), "app.exit", new(ControlProtocol.DefaultProfileId, session),
            42, ControlProtocol.Encode(new ControlGuiOperations.Empty()));
        var ready = new ControlResult(Value: ControlProtocol.Encode(new ControlGuiAction(true)));
        int quit = 0;
        var channel = new ControlGuiChannel((_, _) => Task.FromResult(ready), revision =>
        {
            Assert.Equal(42, revision);
            quit++;
        });

        var otherSession = await channel.DispatchAsync(Guid.NewGuid(), request, TestContext.Current.CancellationToken);
        channel.AcknowledgeResult(request, otherSession);
        channel.AcknowledgeResult(request with { OperationId = "app.clone" }, ready);
        var result = await channel.DispatchAsync(session, request, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, quit);

        channel.AcknowledgeResult(request, result);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, quit);
    }
}
