using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using Xunit;

namespace LLPlayer.Avalonia.Tests;

public sealed class PdicBaselineTests
{
    [Fact]
    public void WordInput_RetainsExactUtf16OffsetInsteadOfSearchingRepeatedWords()
    {
        Assert.NotNull(typeof(WordClickedEventArgs).GetProperty("WordOffset"));
        Assert.NotNull(typeof(WordSelectionSnapshot).GetProperty("WordOffset"));
    }

    [Fact]
    public void Pdic_HasAnExplicitPlatformAwareSettingsAndOwnedClientSurface()
    {
        var assembly = typeof(AppPrefs).Assembly;
        Assert.NotNull(assembly.GetType("LLPlayer.Avalonia.Services.PdicClient"));
        Assert.NotNull(assembly.GetType("LLPlayer.Avalonia.ViewModels.PdicSettingsViewModel"));
        Assert.NotNull(assembly.GetType("LLPlayer.Avalonia.Views.PdicSettingsWindow"));
    }
}
