using System;

namespace FlyleafLib.MediaPlayer;

#nullable enable

/// <summary>
/// Per-track ownership of the OCR engines used by <see cref="SubtitlesOCR"/>. Each sub-track (0: primary,
/// 1: secondary) has its own <see cref="IOCRService"/> slot. Installing a new engine for a track disposes the
/// previous engine for that same track — fixing the native Tesseract engine leak on re-init — and the two tracks
/// never share an engine, so a secondary-track init can no longer clobber the primary's engine (wrong language /
/// double dispose). HC-36.
///
/// Not internally synchronized: <see cref="SubtitlesOCR"/> calls <see cref="Install"/> and <see cref="Get"/> under
/// that track's lock, so the engine is read+used and swapped+disposed under the same lock (no use-after-dispose).
/// </summary>
public sealed class OcrEngineSlots
{
    private readonly IOCRService?[] _slots;

    public OcrEngineSlots(int trackCount)
    {
        _slots = new IOCRService?[trackCount];
    }

    /// <summary>
    /// Installs <paramref name="engine"/> as the given track's engine and disposes the previous engine for that
    /// track, if any. Call under the track's lock so a concurrent <see cref="Get"/>-and-use can't run on the
    /// engine being disposed.
    /// </summary>
    public void Install(int trackIndex, IOCRService engine)
    {
        IOCRService? old = _slots[trackIndex];
        _slots[trackIndex] = engine;
        old?.Dispose();
    }

    /// <summary>
    /// The current engine for a track, or null if none is installed. Call under the track's lock and use the
    /// returned engine only while holding it (so an <see cref="Install"/> cannot dispose it mid-use).
    /// </summary>
    public IOCRService? Get(int trackIndex) => _slots[trackIndex];

    /// <summary>
    /// Disposes and clears the engine for a track (teardown). Call under the track's lock. Idempotent — a second
    /// call is a no-op once the slot is empty.
    /// </summary>
    public void Clear(int trackIndex)
    {
        IOCRService? engine = _slots[trackIndex];
        _slots[trackIndex] = null;
        engine?.Dispose();
    }
}
