using System.Text.Json;
using Avalonia.Headless.XUnit;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class SlotSubtitleSizeCommandTests
{
    [AvaloniaTheory]
    [InlineData(40, .8, true, 42)]
    [InlineData(40, .8, false, 38)]
    [InlineData(40, .4, true, 40)]
    [InlineData(40, 1.5, false, 40)]
    [InlineData(40, .41, true, 41)]
    [InlineData(40, 1.48, false, 39.46666666666667)]
    [InlineData(96, .8, true, 96)]
    [InlineData(12, .8, false, 12)]
    public void Primary_text_size_preserves_secondary_size_and_clips_to_the_joint_legal_range(
        double primary, double scale, bool increase, double expectedPrimary)
    {
        AppPrefs prefs = new() { SubtitleFontSize = primary, SecondaryFontScale = scale };
        var player = new FakePlaybackController { PrimarySlotEnabled = true, SecondarySlotEnabled = true };
        int writes = 0;
        AppPrefs? saved = null;
        using var main = new MainWindowViewModel(player, prefs, candidate => { writes++; saved = candidate; },
            new FakeTranslator(), new FakeShell());
        double oldSecondary = main.SecondaryFontSize;
        double oldAbsoluteSecondary = primary * scale;

        if (increase) main.IncreasePrimarySubtitleSizeCommand.Execute(null);
        else main.DecreasePrimarySubtitleSizeCommand.Execute(null);

        Assert.Equal(oldSecondary, main.SecondaryFontSize);
        Assert.Equal(oldAbsoluteSecondary, prefs.SubtitleFontSize * prefs.SecondaryFontScale, 9);
        Assert.Equal(expectedPrimary, main.SubtitleFontSize, 9);
        Assert.Equal(expectedPrimary, prefs.SubtitleFontSize, 9);
        Assert.InRange(prefs.SecondaryFontScale, .4, 1.5);
        Assert.Equal(expectedPrimary == primary ? 0 : 1, writes);
        if (saved != null)
        {
            Assert.Equal(expectedPrimary, saved.SubtitleFontSize, 9);
            Assert.Equal(oldAbsoluteSecondary, saved.SubtitleFontSize * saved.SecondaryFontScale, 9);
        }
    }

    [AvaloniaTheory]
    [InlineData(.8, true, .85)]
    [InlineData(.8, false, .75)]
    [InlineData(.4, false, .4)]
    [InlineData(1.5, true, 1.5)]
    public void Secondary_text_size_changes_only_secondary_and_does_not_save_a_blocked_bound(
        double scale, bool increase, double expectedScale)
    {
        AppPrefs prefs = new() { SubtitleFontSize = 40, SecondaryFontScale = scale };
        var player = new FakePlaybackController { PrimarySlotEnabled = true, SecondarySlotEnabled = true };
        int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, _ => writes++, new FakeTranslator(), new FakeShell());

        if (increase) main.IncreaseSecondarySubtitleSizeCommand.Execute(null);
        else main.DecreaseSecondarySubtitleSizeCommand.Execute(null);

        Assert.Equal(40, main.SubtitleFontSize);
        Assert.Equal(40, prefs.SubtitleFontSize);
        Assert.Equal(expectedScale, prefs.SecondaryFontScale, 9);
        Assert.Equal(Math.Round(40 * expectedScale, 1), main.SecondaryFontSize);
        Assert.Equal(expectedScale == scale ? 0 : 1, writes);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inheriting_font_face_does_not_make_primary_size_change_secondary_size(bool inheritsFont)
    {
        AppPrefs prefs = new() { SubtitleFontSize = 40, SecondaryFontScale = .8, SecondarySubtitleInheritsFont = inheritsFont };
        var player = new FakePlaybackController { PrimarySlotEnabled = true, SecondarySlotEnabled = true };
        using var main = new MainWindowViewModel(player, prefs, _ => { }, new FakeTranslator(), new FakeShell());

        main.IncreasePrimarySubtitleSizeCommand.Execute(null);

        Assert.Equal(42, main.SubtitleFontSize);
        Assert.Equal(32, main.SecondaryFontSize);
        Assert.Equal(inheritsFont, prefs.SecondarySubtitleInheritsFont);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Disabled_slot_size_command_changes_neither_slot_or_saved_preferences(int slot)
    {
        AppPrefs prefs = new() { SubtitleFontSize = 40, SecondaryFontScale = .8 };
        var player = new FakePlaybackController { PrimarySlotEnabled = slot != 0, SecondarySlotEnabled = slot != 1 };
        int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, _ => writes++, new FakeTranslator(), new FakeShell());
        string before = JsonSerializer.Serialize(prefs);

        if (slot == 0) main.IncreasePrimarySubtitleSizeCommand.Execute(null);
        else main.IncreaseSecondarySubtitleSizeCommand.Execute(null);

        Assert.Equal(before, JsonSerializer.Serialize(prefs));
        Assert.Equal(40, main.SubtitleFontSize);
        Assert.Equal(32, main.SecondaryFontSize);
        Assert.Equal(0, writes);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Failed_slot_size_save_preserves_both_live_sizes_and_all_preferences(int slot)
    {
        AppPrefs prefs = new() { SubtitleFontSize = 40, SecondaryFontScale = .8 };
        var player = new FakePlaybackController { PrimarySlotEnabled = true, SecondarySlotEnabled = true };
        int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, _ => { writes++; throw new IOException("disk full"); },
            new FakeTranslator(), new FakeShell());
        string before = JsonSerializer.Serialize(prefs);

        if (slot == 0) main.IncreasePrimarySubtitleSizeCommand.Execute(null);
        else main.IncreaseSecondarySubtitleSizeCommand.Execute(null);

        Assert.Equal(before, JsonSerializer.Serialize(prefs));
        Assert.Equal(40, main.SubtitleFontSize);
        Assert.Equal(32, main.SecondaryFontSize);
        Assert.Equal(1, writes);
        Assert.Contains(main.Toasts, toast => toast.Title == "Could not apply setting" && toast.Message!.Contains("disk full", StringComparison.Ordinal));
    }
}
