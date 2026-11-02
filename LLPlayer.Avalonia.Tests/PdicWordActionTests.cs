using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public sealed class PdicWordActionTests
{
    static WordSelectionSnapshot Selection(bool isWord = true, MouseButton button = MouseButton.Left)
        => new("word", "😀 word word", 1, isWord, true, "ja", "en", WordSelectionOrigin.Sidebar, button, KeyModifiers.None, 8);

    [AvaloniaFact]
    public async Task PdicSingleWordUsesExactContextAndOffsetWithoutReplacingTheVisiblePopupSnapshot()
    {
        var (main, player, shell, _, _) = TestVm.Create(prefs: new AppPrefs { WordCopyOnSelected = true }.Normalize());
        using var owned = main;
        main.OnWordClicked("previous", "previous cue", 0, 0, 0);
        List<(string Text, int Offset)> sent = [];
        main.PdicSender = (text, offset, _) => { sent.Add((text, offset)); return Task.CompletedTask; };
        main.WordClickAction = "PDIC";
        player.IsPlaying = true;
        main.OnWordClicked(Selection(), 0, 0);
        await main.DrainPdicOperationsAsync();
        Assert.Equal(("😀 word word", 8), Assert.Single(sent));
        Assert.Equal("previous", main.WordPopup.Word); Assert.Equal(1, player.PauseCalls);
        Assert.Equal("word", shell.Clipboard.Last());
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left)] [InlineData(MouseButton.Middle)]
    public async Task PdicModeKeepsSentenceAndPhraseLookupsInTheNormalPopup(MouseButton button)
    {
        var (main, _, _, _, _) = TestVm.Create(); using var owned = main;
        int sends = 0;
        main.PdicSender = (_, _, _) => { sends++; return Task.CompletedTask; };
        main.WordClickAction = "PDIC";
        main.OnWordClicked(Selection(false, button), 0, 0);
        await main.DrainPdicOperationsAsync();
        Assert.Equal(0, sends); Assert.True(main.WordPopup.IsOpen); Assert.Equal("word", main.WordPopup.Word);
    }

    [AvaloniaFact]
    public async Task UnavailablePdicIsActionableAndShutdownCancelsOwnedLookupWithoutLateToast()
    {
        var (main, _, _, _, _) = TestVm.Create();
        main.WordClickAction = "PDIC";
        main.PdicSender = (_, _, _) => throw new PlatformNotSupportedException("PDIC is available only on Windows.");
        main.OnWordClicked(Selection(), 0, 0);
        await main.DrainPdicOperationsAsync();
        Assert.Contains(main.Toasts, item => item.Title == "PDIC unavailable" && item.Message!.Contains("Windows"));
        int count = main.Toasts.Count;
        CancellationToken received = default;
        main.PdicSender = (_, _, token) => { received = token; return Task.Delay(Timeout.InfiniteTimeSpan, token); };
        main.OnWordClicked(Selection(), 0, 0);
        main.BeginWordActionsShutdown();
        await main.DrainPdicOperationsAsync();
        Assert.True(received.IsCancellationRequested); Assert.Equal(count, main.Toasts.Count);
        main.OnWordClicked(Selection(), 0, 0); Assert.Equal(count, main.Toasts.Count);
        main.Dispose();
    }
}
