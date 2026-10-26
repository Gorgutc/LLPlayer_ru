using System.ComponentModel;
using System.Runtime.CompilerServices;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Scriptable <see cref="IPlaybackController"/> recording what the UI asked for.</summary>
public class FakePlaybackController : IPlaybackController
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<int>? CuesChanged;
    public event Action<string?, bool, string?>? OpenCompleted;
    public event Action? ExternalMediaOpening;
    public event Action<string>? ErrorOccurred;

    readonly List<Action<string?>?> openCompletions = [];
    public List<TimeSpan> Seeks { get; } = [];
    public List<WpfKey> KeysDown { get; } = [];
    public List<WpfKey> KeysUp { get; } = [];
    public List<string> Opened { get; } = [];
    public List<string> OpenedSubtitles { get; } = [];
    public List<(int Slot, SubtitleTrack? Track)> SelectedTracks { get; } = [];
    public int PauseCalls { get; private set; }
    public int ToggleCalls { get; private set; }
    public List<CueInfo>[] Cues { get; } = [[], []];
    public List<SubtitleTrack> Tracks { get; } = [];
    public bool HandleEngineKeys { get; set; } = true;
    public string? Language { get; set; } = "en";

    public bool IsOpened { get; set => Set(ref field, value); }
    public bool IsPlaying { get; set => Set(ref field, value); }
    public bool IsOpening { get; set => Set(ref field, value); }
    public string? Title { get; set => Set(ref field, value); }
    public string? Url { get; set => Set(ref field, value); }
    public long CurTime { get; set => Set(ref field, value); }
    public long Duration { get; set => Set(ref field, value); }
    public double Speed { get; set => Set(ref field, value); } = 1;
    public int Volume { get; set => Set(ref field, value); } = 100;
    public int VolumeMax => 150;
    public bool Mute { get; set => Set(ref field, value); }
    public string PrimaryText { get; set => Set(ref field, value); } = "";
    public string SecondaryText { get; set => Set(ref field, value); } = "";
    public SubtitleImage? PrimaryImage { get; set => Set(ref field, value); }
    public SubtitleImage? SecondaryImage { get; set => Set(ref field, value); }
    double primaryBitmapScale = 1, secondaryBitmapScale = 1, primaryBitmapPosition = 100;
    public double PrimaryBitmapScale { get => primaryBitmapScale; set => Set(ref primaryBitmapScale, value); }
    public double SecondaryBitmapScale { get => secondaryBitmapScale; set => Set(ref secondaryBitmapScale, value); }
    public double PrimaryBitmapPosition { get => primaryBitmapPosition; set => Set(ref primaryBitmapPosition, value); }
    public bool PrimarySlotEnabled { get; set => Set(ref field, value); }
    public bool SecondarySlotEnabled { get; set => Set(ref field, value); }

    // F-12 waveform: engine-owned peaks and the master decode switch (persisted by the view model).
    float[]? waveformPeaks;
    public float[]? WaveformPeaks
    {
        get => waveformPeaks;
        set
        {
            if (ReferenceEquals(waveformPeaks, value))
                return;
            waveformPeaks = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WaveformPeaks)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WaveformActive)));
        }
    }
    public bool WaveformActive => waveformPeaks is not null;

    // Engine parity: disabling the waveform clears the built peaks (WaveformEnabled=false => no overlay).
    bool waveformEnabled;
    public bool WaveformEnabled
    {
        get => waveformEnabled;
        set
        {
            if (waveformEnabled == value)
                return;
            waveformEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WaveformEnabled)));
            if (!value)
                WaveformPeaks = null;
        }
    }

    public int PrimaryCueIndex { get; set => Set(ref field, value); } = -1;
    public bool SubtitlesVisible { get; set => Set(ref field, value); } = true;

    // F-12 A-B repeat: the fake mirrors the engine truth table (both points set AND A < B => looping; the points are
    // transient and drop when a new file opens). It deliberately does NOT repeat the engine's canPlay guard: the
    // view-model guards are what the headless tests isolate (the engine-side guard is covered by FlyleafLibTests).
    long abLoopA = -1, abLoopB = -1;
    public long ABLoopA => abLoopA;
    public long ABLoopB => abLoopB;
    public bool HasABLoopA => abLoopA >= 0;
    public bool HasABLoopB => abLoopB >= 0;
    public bool ABLoopEnabled => HasABLoopA && HasABLoopB && abLoopA < abLoopB;
    public bool ABLoopActive => HasABLoopA || HasABLoopB;
    public List<string> AbLoopCalls { get; } = [];

    /// <summary>Runs at the start of <see cref="SetABLoopEnd"/> so a test can move the playhead between the view
    /// model's pre-sample and the engine call (the playback-advances-past-A race).</summary>
    public Action? BeforeSetABLoopEnd { get; set; }

    public void SetABLoopStart()
    {
        AbLoopCalls.Add("setA");
        abLoopA = CurTime;
        if (abLoopB >= 0 && abLoopB <= abLoopA)
            abLoopB = -1;
        RaiseAbLoopChanged();
    }

    public void SetABLoopEnd()
    {
        BeforeSetABLoopEnd?.Invoke();
        AbLoopCalls.Add("setB");
        if (abLoopA >= 0 && CurTime <= abLoopA)
            return;
        abLoopB = CurTime;
        RaiseAbLoopChanged();
    }

    public void ClearABLoop()
    {
        if (!ABLoopActive)
            return;
        AbLoopCalls.Add("clear");
        abLoopA = abLoopB = -1;
        RaiseAbLoopChanged();
    }

    public void ToggleABLoop()
    {
        if (abLoopA < 0)
            SetABLoopStart();
        else if (abLoopB < 0)
            SetABLoopEnd();
        else
            ClearABLoop();
    }

    /// <summary>Simulates the engine dropping the A-B points when a new file opens; the engine re-raises even when
    /// the points were already unset, so this raises unconditionally.</summary>
    public void ResetAbLoopForNewMedia()
    {
        abLoopA = abLoopB = -1;
        RaiseAbLoopChanged();
    }

    void RaiseAbLoopChanged()
    {
        RaiseAb(nameof(ABLoopA));
        RaiseAb(nameof(ABLoopB));
        RaiseAb(nameof(HasABLoopA));
        RaiseAb(nameof(HasABLoopB));
        RaiseAb(nameof(ABLoopEnabled));
        RaiseAb(nameof(ABLoopActive));
    }

    void RaiseAb(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public IReadOnlyList<CueInfo> GetCues(int slot) => [.. Cues[slot]];
    public IReadOnlyList<SubtitleTrack> GetSubtitleTracks() => [.. Tracks];
    public void SelectSubtitleTrack(int slot, SubtitleTrack? track) => SelectedTracks.Add((slot, track));

    public void Open(string url)
    {
        Opened.Add(url);
        openCompletions.Add(null);
    }

    public void Open(string url, Action<string?> completed)
    {
        Open(url);
        openCompletions[^1] = completed;
    }

    public void OpenSubtitles(string path) => OpenedSubtitles.Add(path);
    public void Play() => IsPlaying = true;

    public void Pause()
    {
        PauseCalls++;
        IsPlaying = false;
    }

    public void TogglePlayPause()
    {
        ToggleCalls++;
        IsPlaying = !IsPlaying;
    }

    public void SeekTo(TimeSpan position) => Seeks.Add(position);
    public void SeekToSubtitle(TimeSpan position, int slot) => Seeks.Add(position);

    public bool KeyDown(WpfKey key)
    {
        KeysDown.Add(key);
        return HandleEngineKeys;
    }

    public bool KeyUp(WpfKey key)
    {
        KeysUp.Add(key);
        return false;
    }

    public string? GetSubtitleLanguage(int slot) => Language;

    // Engine key-binding editor seam: records the applied lists and serves them back as the current snapshot.
    public List<EngineKeyBindingInfo> AppliedEngineKeyBindings { get; } = [];
    public List<EngineKeyBindingInfo> CurrentEngineKeyBindings { get; set; } = [];

    public IReadOnlyList<EngineKeyBindingInfo> GetEngineKeyBindings() => [.. CurrentEngineKeyBindings];
    /// <summary>Engine config snapshot in Flyleaf form for the cheat-sheet builder closure.</summary>
    public List<KeyBinding> EngineBindingsForCheatSheet => [.. CurrentEngineKeyBindings.Select(b => new KeyBinding { Action = b.Action, Key = b.Key, Ctrl = b.Ctrl, Alt = b.Alt, Shift = b.Shift, IsKeyUp = b.IsKeyUp, IsEnabled = b.IsEnabled })];

    public void ApplyEngineKeyBindings(IReadOnlyList<EngineKeyBindingInfo> bindings)
    {
        CurrentEngineKeyBindings = [.. bindings];
        AppliedEngineKeyBindings.AddRange(bindings);
    }

    public void CompleteOpen(int requestIndex, string? error = null)
    {
        Action<string?>? completed = openCompletions[requestIndex];
        openCompletions[requestIndex] = null;
        OpenCompleted?.Invoke(Opened[requestIndex], false, error);
        completed?.Invoke(error);
    }

    public void RaiseExternalMediaOpening() => ExternalMediaOpening?.Invoke();

    public void RaiseCuesChanged(int slot) => CuesChanged?.Invoke(slot);
    public void RaiseOpenCompleted(string? url, bool isSubtitles, string? error)
    {
        if (!isSubtitles)
            for (int i = 0; i < Opened.Count; i++)
                if (Opened[i] == url && openCompletions[i] != null)
                {
                    CompleteOpen(i, error);
                    return;
                }
        OpenCompleted?.Invoke(url, isSubtitles, error);
    }
    public void RaiseError(string message) => ErrorOccurred?.Invoke(message);

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class FakeShell : IAppShell
{
    public bool IsFullScreen { get; set; }
    public string? NextPick { get; set; }
    public int CheatSheetShown { get; private set; }
    public List<string> Clipboard { get; } = [];
    public List<string> Themes { get; } = [];
    public int ExitCalls { get; private set; }

    public Exception? PickError { get; set; }

    public Task<string?> PickMediaFileAsync(string? initialFolder)
        => PickError != null ? Task.FromException<string?>(PickError) : Task.FromResult(NextPick);
    public Task<string?> PickSubtitlesFileAsync(string? initialFolder) => Task.FromResult(NextPick);
    public void ShowCheatSheet() => CheatSheetShown++;
    public void SetClipboardText(string text) => Clipboard.Add(text);
    public void ApplyTheme(string theme) => Themes.Add(theme);
    public void Exit() => ExitCalls++;
}

public sealed class FakeTranslator(Func<string, int, CancellationToken, Task<WordTranslationResult>>? impl = null) : IWordTranslator
{
    public List<(string Word, int Slot)> Calls { get; } = [];

    public Task<WordTranslationResult> TranslateAsync(string word, int slot, CancellationToken token)
    {
        Calls.Add((word, slot));
        return impl?.Invoke(word, slot, token)
               ?? Task.FromResult(new WordTranslationResult(WordTranslationStatus.Translated, "[" + word + "]"));
    }
}

/// <summary>Manually advanced clock for idle / seek-hold logic.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    DateTimeOffset now = start;

    public ManualTimeProvider() : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

public static class TestVm
{
    public static (MainWindowViewModel Vm, FakePlaybackController Player, FakeShell Shell, AppPrefs Prefs, List<AppPrefs> Saved)
        Create(TimeProvider? time = null, IWordTranslator? translator = null, AppPrefs? prefs = null)
    {
        FakePlaybackController player = new();
        FakeShell shell = new();
        prefs ??= new AppPrefs().Normalize();
        List<AppPrefs> saved = [];
        MainWindowViewModel vm = new(player, prefs, p => saved.Add(p), translator ?? new FakeTranslator(), shell, time);
        return (vm, player, shell, prefs, saved);
    }
}
