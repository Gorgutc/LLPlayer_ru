using System.Windows.Input;
using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer;

// HC-09: the command-palette (Ctrl+K) default is backfilled into an existing config one-shot (version-gated) and only
// when its chord is free, so it never re-adds a binding the user removed and never creates a duplicate chord (which
// SettingsKeys treats as a conflict, blocking the Keys tab's Apply).
public class KeyBindingBackfillTests
{
    private static readonly Version Threshold = new(0, 3, 45);

    // The candidate default: OpenCommandPalette on the Ctrl chord (Key left at its default; the chord check compares
    // Key + Ctrl/Alt/Shift, and the tests vary the modifiers so no WPF Key value needs naming).
    private static KeyBinding Palette() =>
        new() { Ctrl = true, Action = KeyBindingAction.Custom, ActionName = "OpenCommandPalette" };

    [Fact]
    public void Backfills_PreFixVersion_ChordFree_ActionAbsent()
    {
        KeyBindingBackfill.ShouldBackfill("0.3.44", Threshold, [], Palette()).Should().BeTrue();
    }

    [Theory]
    [InlineData("0.3.45")] // == threshold
    [InlineData("0.3.46")] // > threshold
    [InlineData("1.0.0")]
    public void DoesNotBackfill_AtOrAfterThreshold(string version)
    {
        // A config already at/after the fix that lacks the binding removed it intentionally — do not re-add it.
        KeyBindingBackfill.ShouldBackfill(version, Threshold, [], Palette()).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void Backfills_WhenVersionMissingOrUnparseable(string? version)
    {
        // A null/blank/garbage version predates the fix → backfill.
        KeyBindingBackfill.ShouldBackfill(version, Threshold, [], Palette()).Should().BeTrue();
    }

    [Fact]
    public void DoesNotBackfill_WhenChordTaken_ByAnotherAction()
    {
        // User reassigned the palette's chord (Ctrl+K) to a different action; adding the palette default would create a
        // duplicate chord that blocks Settings > Keys Apply.
        KeyBinding chordTaker = new() { Ctrl = true, Action = KeyBindingAction.Custom, ActionName = "SomethingElse" };
        KeyBindingBackfill.ShouldBackfill("0.3.0", Threshold, [chordTaker], Palette()).Should().BeFalse();
    }

    [Fact]
    public void DoesNotBackfill_WhenActionAlreadyPresent()
    {
        // The palette is already bound (even on a different chord the user chose) — keep the user's binding.
        KeyBinding existingPalette =
            new() { Alt = true, Action = KeyBindingAction.Custom, ActionName = "OpenCommandPalette" };
        KeyBindingBackfill.ShouldBackfill("0.3.0", Threshold, [existingPalette], Palette()).Should().BeFalse();
    }

    [Fact]
    public void Backfills_WhenPreFix_AndUnrelatedBindingsPresent()
    {
        // A different chord + different action does not block the backfill.
        KeyBinding unrelated = new() { Alt = true, Action = KeyBindingAction.Custom, ActionName = "Other" };
        KeyBindingBackfill.ShouldBackfill("0.3.0", Threshold, [unrelated], Palette()).Should().BeTrue();
    }

    [Fact]
    public void Backfills_WhenSameModifiers_DifferentKey()
    {
        // Same modifiers but a DIFFERENT key (Ctrl+J vs the palette's Ctrl+K) is NOT the same chord — the Key half of
        // the chord comparison must still allow the backfill.
        KeyBinding candidate =
            new() { Ctrl = true, Key = Key.K, Action = KeyBindingAction.Custom, ActionName = "OpenCommandPalette" };
        KeyBinding otherKey =
            new() { Ctrl = true, Key = Key.J, Action = KeyBindingAction.Custom, ActionName = "Other" };
        KeyBindingBackfill.ShouldBackfill("0.3.0", Threshold, [otherKey], candidate).Should().BeTrue();
    }

    [Fact]
    public void DoesNotBackfill_WhenSameModifiers_SameKey()
    {
        // Same modifiers AND same key = the palette's Ctrl+K chord is taken → no backfill (a duplicate would block
        // Settings > Keys Apply).
        KeyBinding candidate =
            new() { Ctrl = true, Key = Key.K, Action = KeyBindingAction.Custom, ActionName = "OpenCommandPalette" };
        KeyBinding sameChord =
            new() { Ctrl = true, Key = Key.K, Action = KeyBindingAction.Custom, ActionName = "Other" };
        KeyBindingBackfill.ShouldBackfill("0.3.0", Threshold, [sameChord], candidate).Should().BeFalse();
    }
}
