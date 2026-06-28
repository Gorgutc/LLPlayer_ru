using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Pure, GPU-free resolver for the dubbing voice bank (F-16 phase 1). The built-in preset list
/// mirrors <c>dub_sidecar/server.py</c>'s <c>VOICES</c> and is the single C# source of truth used by
/// the picker UI (batch dialog + Settings ▸ Subtitles ▸ Dubbing), which must NEVER start the multi-GB
/// sidecar just to enumerate voices. server.py stays the engine runtime source for <c>GET /voices</c>;
/// the two lists must be kept in lockstep <b>by hand</b> — a unit test pins the C# bank's ids/order
/// (it does not read server.py), and both files carry a cross-reference comment.
/// <para>
/// Phase 1 selects a single default voice (<see cref="DubbingConfig.DefaultVoiceId"/>). Per-line /
/// diarization-driven gender selection is phase 2 (needs per-line data plumbing). All methods are
/// deterministic and never throw (except cancellation in <see cref="ResolveAsync"/>).
/// </para>
/// </summary>
public static class VoiceBankResolver
{
    /// <summary>
    /// Canonical built-in preset bank (hand-mirror of <c>dub_sidecar/server.py</c> VOICES —
    /// ids/names/genders/language). Synchronous and always available — the UI binds this directly.
    /// Exposed as a genuinely read-only collection so a downcast cannot mutate the shared static.
    /// </summary>
    public static IReadOnlyList<TtsVoice> BuiltIn { get; } = Array.AsReadOnly(new[]
    {
        new TtsVoice("ru-preset-1", "Russian Narrator (M)", "male", "ru"),
        new TtsVoice("ru-preset-2", "Russian Narrator (F)", "female", "ru"),
    });

    /// <summary>
    /// Exact-id lookup over the built-in bank (case-insensitive on Id, matching the sidecar contract).
    /// Returns null for an unknown/blank id.
    /// </summary>
    public static TtsVoice? Resolve(string? voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
            return null;

        foreach (TtsVoice v in BuiltIn)
            if (string.Equals(v.Id, voiceId, StringComparison.OrdinalIgnoreCase))
                return v;

        return null;
    }

    /// <summary>
    /// The built-in bank for binding a picker's <c>ItemsSource</c>, guaranteeing the currently-selected
    /// id is present so the ComboBox never shows a blank selection. If <paramref name="selectedVoiceId"/>
    /// is a non-blank id that is not already in the bank (e.g. a hand-edited config value or a future
    /// cloud/cloned voice), a trailing placeholder entry is appended so the value stays selectable and
    /// round-trips. A blank/known id returns the plain bank.
    /// </summary>
    public static IReadOnlyList<TtsVoice> ForConfig(string? selectedVoiceId)
    {
        if (string.IsNullOrWhiteSpace(selectedVoiceId) || Resolve(selectedVoiceId) is not null)
            return BuiltIn;

        List<TtsVoice> list = new(BuiltIn.Count + 1);
        list.AddRange(BuiltIn);
        list.Add(new TtsVoice(selectedVoiceId, selectedVoiceId, "unknown", ""));
        return list;
    }

    /// <summary>
    /// Phase-2 / live-discovery merge (NOT wired into the phase-1 UI). Returns the built-in voices first
    /// in canonical order, then appends engine-discovered voices whose ids are not already present
    /// (dedup by Id, OrdinalIgnoreCase). When an id appears in both, the curated BUILT-IN metadata wins
    /// (the engine — especially <c>--mock</c> — returns generic names that must not clobber the bank).
    /// <para>
    /// Fail-soft: a null <paramref name="service"/>, or one whose <see cref="ITtsService.GetVoicesAsync"/>
    /// throws / returns null / returns empty, yields the built-in bank only. This method NEVER throws on
    /// an engine error and NEVER starts the sidecar — the CALLER owns the <see cref="ITtsService"/>
    /// lifecycle and only passes an already-initialized service. Cancellation is honoured (propagates
    /// <see cref="OperationCanceledException"/> when <paramref name="token"/> is cancelled).
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<TtsVoice>> ResolveAsync(ITtsService? service, CancellationToken token = default)
    {
        if (service is null)
            return BuiltIn;

        IReadOnlyList<TtsVoice>? discovered;
        try
        {
            discovered = await service.GetVoicesAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return BuiltIn;
        }

        if (discovered is null || discovered.Count == 0)
            return BuiltIn;

        List<TtsVoice> result = new(BuiltIn.Count + discovered.Count);
        result.AddRange(BuiltIn);

        HashSet<string> known = new(BuiltIn.Select(v => v.Id), StringComparer.OrdinalIgnoreCase);
        foreach (TtsVoice v in discovered)
        {
            if (v is null || string.IsNullOrEmpty(v.Id))
                continue;
            if (known.Add(v.Id))
                result.Add(v);
        }

        return result;
    }
}
