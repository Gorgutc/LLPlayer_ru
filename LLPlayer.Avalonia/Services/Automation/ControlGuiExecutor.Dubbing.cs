using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Services.Automation;

internal sealed partial class ControlGuiExecutor
{
    private async Task<object?> ExecuteDubbingOperationAsync(ControlRequest request, CancellationToken token)
    {
        switch (request.OperationId)
        {
            case "dubbing.cue_voice.get":
                var get = ReadExtended<ControlSubtitleCueRequest>(request);
                var resolved = cues.Resolve(get);
                return new ControlCueVoice(get.CueId, get.Revision, player.GetCueVoiceId(resolved.Cue, get.Slot));
            case "dubbing.cue_voice.set":
                var set = ReadExtended<ControlCueVoiceSet>(request);
                if (model.DubbingVoices?.Invoke().Any(voice => voice.Id == set.VoiceId) != true)
                    throw Invalid("Choose a currently configured dubbing voice ID.");
                return await SetControlCueVoiceAsync(new(set.Slot, set.CueId, set.Revision), set.VoiceId, token);
            case "dubbing.cue_voice.clear":
                return await SetControlCueVoiceAsync(ReadExtended<ControlSubtitleCueRequest>(request), null, token);
            case "dubbing.attach":
                var attach = ReadExtended<ControlDubbingAttach>(request);
                if (request.ExpectedRevision is not long expected)
                    throw Invalid("Read the current media revision before attaching a dub.");
                return new ControlDubbingAttachment(player.AttachControlDub(attach.Path, expected, token));
            default:
                return null;
        }
    }

    private async Task<ControlCueVoice> SetControlCueVoiceAsync(ControlSubtitleCueRequest request, string? voiceId,
        CancellationToken token)
    {
        var (cue, delay) = cues.Resolve(request);
        if (cue.IsBitmap || string.IsNullOrWhiteSpace(cue.OriginalText))
            throw Invalid("Select a subtitle cue containing text.");
        await player.SetCueVoiceAsync(cue, request.Slot, voiceId, delay, token);
        if (!player.IsCueCurrent(cue, request.Slot) || player.GetCueVoiceId(cue, request.Slot) != voiceId)
            throw new ControlException(new("stale_revision", "The subtitle changed after the voice edit. Read its current state."));
        return new(request.CueId, cues.Describe(request.Slot).Revision, voiceId);
    }
}
