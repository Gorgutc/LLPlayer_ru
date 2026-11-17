namespace LLPlayer.ControlApi;

public sealed record ControlCueVoiceSet(int Slot, Guid CueId, Guid Revision, string VoiceId);
public sealed record ControlCueVoice(Guid CueId, Guid Revision, string? VoiceId);
public sealed record ControlDubbingAttach(string? Path = null);
public sealed record ControlDubbingAttachment(string Path);

internal static class ControlGuiDubbingOperations
{
    public static IReadOnlyList<ControlGuiOperationSchema> All { get; } =
    [
        Define<ControlSubtitleCueRequest, ControlCueVoice>("dubbing.cue_voice.get", "Read the current cue's voice override."),
        Define<ControlCueVoiceSet, ControlCueVoice>("dubbing.cue_voice.set", "Set a voice override for a current text cue.", true),
        Define<ControlSubtitleCueRequest, ControlCueVoice>("dubbing.cue_voice.clear", "Clear a voice override for a current text cue.", true),
        Define<ControlDubbingAttach, ControlDubbingAttachment>("dubbing.attach", "Add a rendered dub to the current media's selectable audio tracks.",
            true, ControlOperationCompletion.Job),
    ];

    private static ControlGuiOperationSchema Define<TArgs, TResult>(string id, string description,
        bool mutates = false, ControlOperationCompletion completion = ControlOperationCompletion.Immediate) =>
        new(new(id, "dubbing", description, mutates, completion, ControlOperationContext.Gui), typeof(TArgs), typeof(TResult));
}
