using System.Text.Json;
using LLPlayer.Avalonia.Services;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Batch;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia;

public sealed partial class AppHost
{
    static readonly ControlScope SettingsScope = new(ControlProtocol.DefaultProfileId);
    readonly Dictionary<string, Guid> appliedControlSettings = new(StringComparer.Ordinal);
    readonly HashSet<string> applyingControlSettings = new(StringComparer.Ordinal);
    readonly Dictionary<string, Func<Task>> reloadControlSettings = new(StringComparer.Ordinal);

    bool CanCommitControlSettings => automationConnection != null && File.Exists(
        Environment.GetEnvironmentVariable("LLPLAYER_CONTROL_HOST") ?? Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "LLPlayer.ControlHost.exe" : "LLPlayer.ControlHost"));

    Task SaveControlBatchSettingsAsync(BatchSettings baseline, BatchSettings candidate)
        => CommitControlSettingsAsync(ControlSettingsService.CreateDraft(baseline, candidate), default);

    Task SaveControlRecognitionSettingsAsync(RecognitionSettings original, RecognitionSettings edited, RecognitionSettingsStore store)
    {
        if (!CanCommitControlSettings) return controller!.SaveRecognitionSettingsAsync(edited, store);
        var draft = ControlSettingsService.CreateDraft(original, edited);
        if (draft.Patch.Fields.Count == 0) return Task.CompletedTask;
        return controller!.ApplyPersistedRecognitionSettingsAsync(token =>
        {
            CommitControlSettingsAsync(draft, token).GetAwaiter().GetResult();
            return store.Load() ?? throw new InvalidOperationException("Saved recognition settings could not be read.");
        });
    }

    Task SaveControlDubbingSettingsAsync(Func<DubbingSettings, DubbingSettings> edit)
        => controller!.Recognition.MutateIfIdleAsync(token =>
        {
            DubbingSettings original = dubbingSettings!;
            var draft = ControlSettingsService.CreateDraft(original, edit(original).Clone());
            CommitControlSettingsAsync(draft, token).GetAwaiter().GetResult();
            DubbingSettings saved = dubbingStore!.Load();
            controller.ApplyDubbingSettings(saved);
            dubbingSettings = saved;
        });

    async Task CommitControlSettingsAsync(ControlSettingsDraft draft, CancellationToken cancellationToken,
        Func<ControlSettingsDraft>? comparePrivate = null)
    {
        if (!draft.HasChanges) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var client = await ControlClient.ConnectOrStartAsync(paths.ConfigDir, paths.StateDir, token).ConfigureAwait(false);
        var read = await client.InvokeAsync(new(Guid.NewGuid(), "settings.read", SettingsScope, null,
            ControlProtocol.Encode(new ControlSettingsFamilyRequest(draft.Patch.Family))), token).ConfigureAwait(false);
        var current = read.Value?.Deserialize<ControlSettingsSnapshot>(ControlProtocol.Json)
            ?? throw new InvalidOperationException(read.Error?.Message ?? "Current settings could not be read.");
        if (comparePrivate == null)
        {
            foreach (var (field, original) in draft.Original)
                if (!current.Requested.TryGetValue(field, out var value)
                    || (!JsonElement.DeepEquals(value, original) && !JsonElement.DeepEquals(value, draft.Patch.Fields[field])))
                    throw SettingsEditorConflict();
        }
        else
            ValidatePrivateDraft(draft, comparePrivate());

        string operation = comparePrivate == null ? "settings.apply" : "settings.credentials.apply";
        JsonElement arguments = comparePrivate == null
            ? ControlProtocol.Encode(draft.Patch)
            : ControlProtocol.Encode(new ControlCredentialPatch(draft.Patch.Family, draft.Patch.Fields, draft.CredentialChanges!));
        var admission = await client.InvokeAsync(new(Guid.NewGuid(), operation, SettingsScope, current.Revision, arguments), token)
            .ConfigureAwait(false);
        Guid jobId = admission.JobId ?? throw new InvalidOperationException(admission.Error?.Message ?? "The settings save could not start.");
        for (;;)
        {
            var job = await client.GetAsync(SettingsScope, jobId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The save result could not be confirmed.");
            if (job.Status == ControlJobStatus.Completed) return;
            if (job.Status is ControlJobStatus.Failed or ControlJobStatus.Cancelled or ControlJobStatus.Interrupted or ControlJobStatus.AwaitingCleanup)
                throw new InvalidOperationException(job.Error?.Message ?? "The save result could not be confirmed.");
            await Task.Delay(50, token).ConfigureAwait(false);
        }
    }

    static void ValidatePrivateDraft(ControlSettingsDraft edit, ControlSettingsDraft persisted)
    {
        foreach (var (field, value) in edit.Patch.Fields)
            if (persisted.Patch.Fields.TryGetValue(field, out var latest) && !JsonElement.DeepEquals(latest, value))
                throw SettingsEditorConflict();
        foreach (var (name, value) in edit.CredentialChanges!)
            if (persisted.CredentialChanges!.TryGetValue(name, out string? latest) && !string.Equals(latest, value, StringComparison.Ordinal))
                throw SettingsEditorConflict();
    }

    static InvalidOperationException SettingsEditorConflict() => new(
        "A setting changed while this editor was open. Reopen it and review the current values.");

    void RefreshControlSettings(ControlState? state)
    {
        if (state == null || controller == null || languageShuttingDown) return;
        var latest = state.Jobs.Where(job => (job.OperationId.StartsWith("settings.", StringComparison.Ordinal)
                || job.OperationId == "backends.set_requested")
                && job.Status == ControlJobStatus.Completed)
            .SelectMany(job => SettingsFamilies(job).Select(family => (job, family)))
            .GroupBy(item => item.family, StringComparer.Ordinal)
            .Select(group => group.MaxBy(item => item.job.UpdatedAt));
        foreach (var (job, family) in latest)
        {
            if (family == null || (family is not ("recognition" or "dubbing") && !reloadControlSettings.ContainsKey(family))
                || appliedControlSettings.GetValueOrDefault(family) == job.JobId
                || !applyingControlSettings.Add(family)) continue;
            _ = ApplyControlSettingsAsync(family, job.JobId);
        }
    }

    static IEnumerable<string> SettingsFamilies(ControlJob job)
    {
        if (job.Value is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("family", out var field)
            && field.ValueKind == JsonValueKind.String && field.GetString() is { } family)
            return [family];
        // Durable credential jobs intentionally expose only their completion. Both owners keep the real data in their stores.
        return job.OperationId.StartsWith("settings.credentials.", StringComparison.Ordinal) ? ["translation", "online_video"] : [];
    }

    async Task ApplyControlSettingsAsync(string family, Guid jobId)
    {
        try
        {
            if (family == "recognition")
            {
                var saved = controller!.RecognitionStore!.Load();
                if (saved != null) await controller.ApplyPersistedRecognitionSettingsAsync(_ => saved);
            }
            else if (family == "dubbing")
            {
                await controller!.Recognition.MutateIfIdleAsync(_ =>
                {
                    var saved = dubbingStore!.Load();
                    controller.ApplyDubbingSettings(saved);
                    dubbingSettings = saved;
                });
            }
            else await reloadControlSettings[family]();
            appliedControlSettings[family] = jobId;
        }
        catch (InvalidOperationException) when (controller!.Recognition.IsBusy) { }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            CrashLog.Write("Reload automation settings", ex);
            appliedControlSettings[family] = jobId;
        }
        finally { applyingControlSettings.Remove(family); }
    }
}
