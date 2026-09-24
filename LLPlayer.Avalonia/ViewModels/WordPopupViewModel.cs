using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>
/// Word popup (click a subtitle word): the word, a copy action and its translation through FlyleafLib's translation
/// stack. A newer click cancels the pending lookup; results of a superseded lookup are dropped.
/// </summary>
public sealed partial class WordPopupViewModel(IWordTranslator translator, Action<string> copyToClipboard) : ObservableObject
{
    CancellationTokenSource? cts;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Word { get; private set; } = "";

    [ObservableProperty]
    public partial string Context { get; private set; } = "";

    [ObservableProperty]
    public partial string? Translation { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool IsNotConfigured { get; private set; }

    /// <summary>Anchor of the popup in the video area (DIPs); the view clamps it inside the area.</summary>
    [ObservableProperty]
    public partial double AnchorX { get; private set; }

    [ObservableProperty]
    public partial double AnchorY { get; private set; }

    public int Slot { get; private set; }

    public async Task OpenAsync(string word, string context, int slot, double x, double y)
    {
        cts?.Cancel();
        cts?.Dispose();
        CancellationTokenSource mine = cts = new CancellationTokenSource();

        Word = word;
        Context = context;
        Slot = slot;
        AnchorX = x;
        AnchorY = y;
        Translation = null;
        IsNotConfigured = false;
        Status = "Translating…";
        IsBusy = true;
        IsOpen = true;

        WordTranslationResult result;
        try
        {
            result = await translator.TranslateAsync(word, slot, mine.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ReferenceEquals(mine, cts) || mine.IsCancellationRequested)
            return;

        IsBusy = false;
        switch (result.Status)
        {
            case WordTranslationStatus.Translated:
                Translation = result.Text;
                Status = "";
                break;
            case WordTranslationStatus.NotConfigured:
                IsNotConfigured = true;
                Status = "Translation not configured" + (string.IsNullOrEmpty(result.Detail) ? "" : ": " + result.Detail);
                break;
            default:
                Status = "Translation failed" + (string.IsNullOrEmpty(result.Detail) ? "" : ": " + result.Detail);
                break;
        }
    }

    [RelayCommand]
    public void Close()
    {
        cts?.Cancel();
        IsBusy = false;
        IsOpen = false;
    }

    [RelayCommand]
    void Copy()
    {
        if (!string.IsNullOrEmpty(Word))
            copyToClipboard(Word);
    }

    [RelayCommand]
    void CopyTranslation()
    {
        if (!string.IsNullOrEmpty(Translation))
            copyToClipboard(Translation);
    }
}
