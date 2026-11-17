using System.Text.Json;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;

namespace FlyleafLibTests.Automation;

public sealed class ControlContractTests
{
    [Fact]
    public void Request_roundtrip_preserves_scope_and_revision()
    {
        var request = new ControlRequest(Guid.NewGuid(), "media.test", new("default", Guid.NewGuid()),
            12, ControlProtocol.Encode(new TestArguments("example")));

        var decoded = JsonSerializer.Deserialize<ControlRequest>(
            JsonSerializer.Serialize(request, ControlProtocol.Json), ControlProtocol.Json)!;

        Assert.Equal(request.Scope, decoded.Scope);
        Assert.Equal(12, decoded.ExpectedRevision);
        Assert.Equal(request.RequestId, decoded.RequestId);
        Assert.Equal("example", decoded.Arguments.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Invalid_arguments_do_not_reach_the_operation()
    {
        var catalog = new ControlOperationCatalog();
        var called = false;
        catalog.Add<TestArguments, string>(new("media.test", "media", "Test operation."), (_, args, _) =>
        {
            called = true;
            return Task.FromResult(args.Name);
        });
        var scope = new ControlScope("default");

        var unknown = await catalog.InvokeAsync(new(Guid.NewGuid(), "media.unknown", scope, null,
            ControlProtocol.Encode(new { })), TestContext.Current.CancellationToken);
        var invalid = await catalog.InvokeAsync(new(Guid.NewGuid(), "media.test", scope, null,
            ControlProtocol.Encode(new { name = 8 })), TestContext.Current.CancellationToken);
        var missing = await catalog.InvokeAsync(new(Guid.NewGuid(), "media.test", scope, null,
            ControlProtocol.Encode(new { })), TestContext.Current.CancellationToken);

        Assert.Equal("unknown_operation", unknown.Error?.Code);
        Assert.Equal("invalid_arguments", invalid.Error?.Code);
        Assert.Equal("invalid_arguments", missing.Error?.Code);
        Assert.False(called);

        var valid = await catalog.InvokeAsync(new(Guid.NewGuid(), "media.test", scope, null,
            ControlProtocol.Encode(new TestArguments("example"))), TestContext.Current.CancellationToken);
        Assert.Equal("example", valid.Value?.GetString());
        Assert.True(called);
    }

    [Fact]
    public async Task Diagnostics_do_not_echo_handler_exceptions()
    {
        var catalog = new ControlOperationCatalog();
        catalog.Add<TestArguments, string>(new("settings.credentials.set", "settings", "Set credential."),
            (_, args, _) => throw new InvalidOperationException(args.Name));

        var result = await catalog.InvokeAsync(new(Guid.NewGuid(), "settings.credentials.set", new("default"), null,
            ControlProtocol.Encode(new TestArguments("private-api-key"))), TestContext.Current.CancellationToken);

        Assert.Equal("operation_failed", result.Error?.Code);
        Assert.DoesNotContain("private-api-key", JsonSerializer.Serialize(result, ControlProtocol.Json));
    }

    public sealed record TestArguments(string Name);
}
