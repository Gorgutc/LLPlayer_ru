using System.ComponentModel;
using System.Runtime.CompilerServices;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Scriptable <see cref="IPlaybackController"/> recording what the UI asked for.</summary>
public sealed class FakePlaybackController : IPlaybackController
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<int>? CuesChanged;
    public event Action<string?, bool, string?>? OpenCompleted;
    public event Action<string>? ErrorOccurred;

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
    public int PrimaryCueIndex { get; set => Set(ref field, value); } = -1;
    public bool SubtitlesVisible { get; set => Set(ref field, value); } = true;

    public IReadOnlyList<CueInfo> GetCues(int slot) => [.. Cues[slot]];
    public IReadOnlyList<SubtitleTrack> GetSubtitleTracks() => [.. Tracks];
    public void SelectSubtitleTrack(int slot, SubtitleTrack? track) => SelectedTracks.Add((slot, track));

    public void Open(string url)
    {
        Opened.Add(url);
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

    public void RaiseCuesChanged(int slot) => CuesChanged?.Invoke(slot);
    public void RaiseOpenCompleted(string? url, bool isSubtitles, string? error) => OpenCompleted?.Invoke(url, isSubtitles, error);
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
