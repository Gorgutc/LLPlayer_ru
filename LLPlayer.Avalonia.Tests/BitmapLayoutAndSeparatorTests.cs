using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AwesomeAssertions;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

/// <summary>
/// Remaining half of the WPF "Subtitles > Position / Size" page in the shared Avalonia transport: bitmap subtitle
/// scale/position, the dual-text separator and the vertical block alignment (adapted to the approved DIP bottom model).
/// </summary>
public class BitmapLayoutAndSeparatorTests
{
    static AppPrefs Normalize(string key, string jsonValue)
    {
        string json = "{\"" + key + "\":" + jsonValue + "}";
        return (JsonSerializer.Deserialize<AppPrefs>(json, PrefsStore.JsonOptions) ?? new AppPrefs()).Normalize();
    }

    [Fact]
    public void Separator_defaults_are_the_approved_wpf_values()
    {
        AppPrefs prefs = new AppPrefs().Normalize();
        prefs.SubtitleSeparatorMaxWidth.Should().Be(280);
        prefs.SubtitleSeparatorOpacityPercent.Should().Be(30);
        prefs.SubtitlePositionAlignment.Should().Be("Bottom");
        prefs.SubtitlePositionAlignmentWhenDual.Should().Be("Bottom");
    }

    [Fact]
    public void Non_finite_separator_values_fall_back_to_their_defaults()
    {
        AppPrefs prefs = new AppPrefs { SubtitleSeparatorMaxWidth = double.NaN }.Normalize();
        prefs.SubtitleSeparatorMaxWidth.Should().Be(280);
        prefs = new AppPrefs { SubtitleSeparatorMaxWidth = double.PositiveInfinity }.Normalize();
        prefs.SubtitleSeparatorMaxWidth.Should().Be(280);
    }

    [Theory]
    [InlineData("SubtitleSeparatorMaxWidth", "-5", 0)]
    [InlineData("SubtitleSeparatorMaxWidth", "2500", 2000)]
    [InlineData("SubtitleSeparatorOpacityPercent", "-1", 0)]
    [InlineData("SubtitleSeparatorOpacityPercent", "150", 100)]
    [InlineData("SubtitlePositionAlignment", "\" top \"", "Top")]
    [InlineData("SubtitlePositionAlignment", "\"centre\"", "Bottom")]
    [InlineData("SubtitlePositionAlignmentWhenDual", "\"CENTER\"", "Center")]
    public void Separator_and_alignment_normalize_to_supported_values(string key, string jsonValue, object expected)
    {
        AppPrefs prefs = Normalize(key, jsonValue);
        object actual = key switch
        {
            "SubtitleSeparatorMaxWidth" => prefs.SubtitleSeparatorMaxWidth,
            "SubtitleSeparatorOpacityPercent" => prefs.SubtitleSeparatorOpacityPercent,
            "SubtitlePositionAlignment" => prefs.SubtitlePositionAlignment,
            _ => prefs.SubtitlePositionAlignmentWhenDual,
        };
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(100, 100, 20, 20, 100, 100, 1.0, 100, 100, 100, 20, 20)]
    [InlineData(40, 60, 20, 20, 100, 100, 2.0, 100, 30, 50, 40, 40)]
    [InlineData(40, 60, 20, 20, 100, 100, 1.0, 150, 40, 110, 20, 20)]
    [InlineData(40, 60, 20, 20, 100, 100, 1.0, 50, 40, 10, 20, 20)]
    [InlineData(40, 20, 20, 20, 100, 100, 1.0, 150, 40, 20, 20, 20)]
    public void Bitmap_geometry_mirrors_the_engine_bitmap_position_math(
        int x, int y, int w, int h, int videoWidth, int videoHeight, double scale, double position,
        double expectedX, double expectedY, double expectedWidth, double expectedHeight)
    {
        BitmapSubtitleGeometry geometry = BitmapSubtitleGeometry.Compute(x, y, w, h, videoWidth, videoHeight, scale, position);
        geometry.X.Should().BeApproximately(expectedX, 0.001);
        geometry.Y.Should().BeApproximately(expectedY, 0.001);
        geometry.Width.Should().BeApproximately(expectedWidth, 0.001);
        geometry.Height.Should().BeApproximately(expectedHeight, 0.001);
    }

    [Theory]
    [InlineData(1.234, 100, 47.7, 24.6)]
    [InlineData(1.0, 100.6, 51.0, 20.0)]
    [InlineData(1.0, 200, 100.0, 20.0)]
    [InlineData(1.0, -5, -50.0, 20.0)]
    [InlineData(0, 150, 100.0, 20.0)]
    public void Bitmap_geometry_rounds_and_clamps_like_the_engine(double scale, double position, double expectedY, double expectedWidth)
    {
        // x=50,y=50,w=20,h=20,video 100x100: y=50 is the horizontal-cue boundary (>= videoHeight/2).
        BitmapSubtitleGeometry geometry = BitmapSubtitleGeometry.Compute(50, 50, 20, 20, 100, 100, scale, position);
        geometry.Y.Should().BeApproximately(expectedY, 0.001);
        geometry.Width.Should().BeApproximately(expectedWidth, 0.001);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(0, 0)]
    [InlineData(-10, 50)]
    public void Bitmap_geometry_falls_back_to_raw_rect_when_video_size_is_unknown(int videoWidth, int videoHeight)
    {
        BitmapSubtitleGeometry geometry = BitmapSubtitleGeometry.Compute(40, 40, 20, 20, videoWidth, videoHeight, 2.0, 50);
        geometry.X.Should().Be(40);
        geometry.Y.Should().Be(40);
        geometry.Width.Should().Be(20);
        geometry.Height.Should().Be(20);
    }

    [AvaloniaFact]
    public void Save_failure_keeps_the_previous_session_bitmap_values()
    {
        FakePlaybackController player = new() { IsOpened = true, PrimaryBitmapScale = 1.25, SecondaryBitmapScale = 0.8, PrimaryBitmapPosition = 90 };
        AppPrefs prefs = new AppPrefs().Normalize();
        using MainWindowViewModel vm = CreateVm(player, prefs, _ => throw new IOException("disk is full"));
        SettingsViewModel draft = vm.CreateSettings();
        draft.PrimaryBitmapScaleText = 2.ToString(draft.NumberFormat);
        draft.PrimaryBitmapPositionText = 120.ToString(draft.NumberFormat);
        draft.SaveCommand.Execute(null);

        draft.Error.Should().NotBeNull().And.Contain("Could not save preferences");
        player.PrimaryBitmapScale.Should().Be(1.25, "a failed save must not apply session bitmap values");
        player.PrimaryBitmapPosition.Should().Be(90);
        vm.SubtitleSeparatorMaxWidth.Should().Be(280);
    }

    static MainWindowViewModel CreateVm(FakePlaybackController player, AppPrefs prefs, Action<AppPrefs>? save = null)
    {
        var shell = new FakeShell();
        return new MainWindowViewModel(player, prefs, save ?? (_ => { }), new FakeTranslator(), shell);
    }

    [AvaloniaFact]
    public void Settings_editors_validate_and_save_separator_and_alignment_prefs()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        FakePlaybackController player = new() { IsOpened = true };
        AppPrefs prefs = new AppPrefs().Normalize();
        int writes = 0;
        using MainWindowViewModel vm = CreateVm(player, prefs, candidate => { store.Save(candidate); writes++; });
        SettingsViewModel draft = vm.CreateSettings();

        draft.SubtitleSeparatorMaxWidthText = "2001";
        draft.SaveCommand.Execute(null);
        draft.Error.Should().NotBeNull("separator max width above 2000 must visibly block Save");
        draft.SubtitleSeparatorMaxWidthText = "-1";
        draft.SaveCommand.Execute(null);
        draft.Error.Should().NotBeNull();
        draft.SubtitleSeparatorMaxWidthText = "120";
        draft.SubtitleSeparatorOpacityPercentText = "101";
        draft.SaveCommand.Execute(null);
        draft.Error.Should().NotBeNull("separator opacity must be a whole 0..100 percent");
        draft.SubtitleSeparatorOpacityPercentText = "45";
        draft.SubtitlePositionAlignment = "middle";
        draft.SaveCommand.Execute(null);
        draft.Error.Should().NotBeNull("unsupported position alignment must block Save");

        draft.SubtitlePositionAlignment = "Top";
        draft.SubtitlePositionAlignmentWhenDual = "Center";
        draft.SaveCommand.Execute(null);
        draft.Error.Should().BeNull();
        writes.Should().Be(1);
        vm.SubtitleSeparatorMaxWidth.Should().Be(120);
        vm.SubtitleSeparatorOpacityPercent.Should().Be(45);
        vm.SubtitlePositionAlignment.Should().Be("Top");
        vm.SubtitlePositionAlignmentWhenDual.Should().Be("Center");
        AppPrefs saved = store.Load();
        saved.SubtitleSeparatorMaxWidth.Should().Be(120);
        saved.SubtitleSeparatorOpacityPercent.Should().Be(45);
        saved.SubtitlePositionAlignment.Should().Be("Top");
        saved.SubtitlePositionAlignmentWhenDual.Should().Be("Center");
    }

    [AvaloniaFact]
    public void Separator_and_dual_alignment_survive_restart()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        FakePlaybackController player = new() { IsOpened = true };
        using (MainWindowViewModel vm = CreateVm(player, new AppPrefs().Normalize(), store.Save))
        {
            SettingsViewModel draft = vm.CreateSettings();
            draft.SubtitleSeparatorMaxWidthText = "180";
            draft.SubtitleSeparatorOpacityPercentText = "60";
            draft.SubtitlePositionAlignment = "Center";
            draft.SubtitlePositionAlignmentWhenDual = "Top";
            draft.SaveCommand.Execute(null);
            draft.Error.Should().BeNull();
        }
        AppPrefs loaded = store.Load();
        using MainWindowViewModel restarted = CreateVm(player, loaded);
        restarted.SubtitleSeparatorMaxWidth.Should().Be(180);
        restarted.SubtitleSeparatorOpacityPercent.Should().Be(60);
        restarted.SubtitlePositionAlignment.Should().Be("Center");
        restarted.SubtitlePositionAlignmentWhenDual.Should().Be("Top");
    }

    [AvaloniaFact]
    public void Bitmap_scale_and_position_are_live_session_state_and_are_applied_on_save()
    {
        FakePlaybackController player = new() { IsOpened = true, PrimaryBitmapScale = 1.25, SecondaryBitmapScale = 0.8, PrimaryBitmapPosition = 90 };
        AppPrefs prefs = new AppPrefs().Normalize();
        List<AppPrefs> saved = [];
        using MainWindowViewModel vm = CreateVm(player, prefs, saved.Add);
        SettingsViewModel draft = vm.CreateSettings();
        draft.PrimaryBitmapScaleText.Should().Be(1.25.ToString(draft.NumberFormat));
        draft.SecondaryBitmapScaleText.Should().Be(0.8.ToString(draft.NumberFormat));
        draft.PrimaryBitmapPositionText.Should().Be(90.ToString(draft.NumberFormat));

        draft.PrimaryBitmapScaleText = "0";
        draft.SaveCommand.Execute(null);
        draft.Error.Should().NotBeNull("a zero bitmap scale must block Save");
        draft.PrimaryBitmapScaleText = 1.75.ToString(draft.NumberFormat);
        draft.SecondaryBitmapScaleText = 1.ToString(draft.NumberFormat);
        draft.PrimaryBitmapPositionText = 120.ToString(draft.NumberFormat);
        draft.SaveCommand.Execute(null);
        draft.Error.Should().BeNull();
        player.PrimaryBitmapScale.Should().Be(1.75);
        player.SecondaryBitmapScale.Should().Be(1);
        player.PrimaryBitmapPosition.Should().Be(120);
        saved.Should().HaveCount(1);
        saved[0].SubtitleSeparatorMaxWidth.Should().Be(280, "bitmap runtime values are session state, not persisted preferences");
        vm.SubtitleSeparatorMaxWidth.Should().Be(280);
    }

    static (MainWindow Window, MainWindowViewModel Vm, FakePlaybackController Player) BuildWindow()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var player = new FakePlaybackController { IsOpened = true, PrimaryText = "Primary", PrimarySlotEnabled = true };
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 700 };
        MainWindowViewModel vm = new(player, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, player);
    }

    [AvaloniaFact]
    public void Separator_is_visible_only_when_the_secondary_subtitle_is_displayed()
    {
        var (window, vm, player) = BuildWindow();
        try
        {
            Border separator = window.FindControl<Border>("SubtitleSeparator")!;
            separator.IsVisible.Should().BeFalse("a single subtitle line needs no separator");
            separator.MaxWidth.Should().Be(280);
            separator.Opacity.Should().BeApproximately(0.3, 0.001);
            SubtitleText primary = window.FindControl<SubtitleText>("PrimarySubtitle")!;
            double singleY = primary.TranslatePoint(new Point(0, 0), window)!.Value.Y;

            player.SecondaryText = "Secondary";
            Dispatcher.UIThread.RunJobs();
            separator.IsVisible.Should().BeTrue();
            separator.Height.Should().Be(2);
            separator.Bounds.Width.Should().BeApproximately(280, 0.5, "the separator must render at its max width");
            StackPanel overlay = window.FindControl<StackPanel>("SubtitleOverlay")!;
            separator.Bounds.Center.X.Should().BeApproximately(overlay.Bounds.Width / 2, 1.0,
                "the separator must be horizontally centred in the subtitle box");
            primary.TranslatePoint(new Point(0, 0), window)!.Value.Y.Should().BeApproximately(singleY, 0.5,
                "bottom anchoring keeps the primary line in place");

            SettingsViewModel draft = vm.CreateSettings();
            draft.SubtitleSeparatorMaxWidthText = "150";
            draft.SubtitleSeparatorOpacityPercentText = "50";
            draft.SubtitlePrimarySecondaryDistanceText = "8";
            draft.SaveCommand.Execute(null);
            draft.Error.Should().BeNull();
            Dispatcher.UIThread.RunJobs();
            separator.MaxWidth.Should().Be(150);
            separator.Bounds.Width.Should().BeApproximately(150, 0.5);
            separator.Opacity.Should().BeApproximately(0.5, 0.001);
            separator.Height.Should().Be(8);
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void Vertical_block_alignment_anchors_the_overlay_bottom_center_or_top_at_the_offset()
    {
        var (window, vm, player) = BuildWindow();
        try
        {
            StackPanel overlay = window.FindControl<StackPanel>("SubtitleOverlay")!;
            double height = overlay.Bounds.Height;
            height.Should().BeGreaterThan(0);
            double bottomMargin = overlay.Margin.Bottom;

            SettingsViewModel draft = vm.CreateSettings();
            draft.SubtitlePositionAlignment = "Center";
            draft.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            overlay.Margin.Bottom.Should().BeApproximately(Math.Max(0, bottomMargin - height / 2), 0.5,
                "Center anchors the block center at the configured distance, guarded at the viewport bottom");

            draft = vm.CreateSettings();
            draft.SubtitlePositionAlignment = "Top";
            draft.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            overlay.Margin.Bottom.Should().BeApproximately(Math.Max(0, bottomMargin - height), 0.5,
                "Top anchors the block top at the configured distance, guarded at the viewport bottom");

            vm.SecondaryVisible.Should().BeFalse();
            draft = vm.CreateSettings();
            draft.SubtitlePositionAlignment = "Bottom";
            draft.SubtitlePositionAlignmentWhenDual = "Top";
            draft.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            overlay.Margin.Bottom.Should().BeApproximately(bottomMargin, 0.5,
                "single mode still uses the single alignment");

            // Dual mode is selected by loaded slots (WPF parity), not by the current secondary cue text, so the anchor
            // does not jump when a secondary cue appears or disappears.
            player.SecondarySlotEnabled = true;
            Dispatcher.UIThread.RunJobs();
            vm.SecondarySlotEnabled.Should().BeTrue();
            vm.SubtitleDualLayoutActive.Should().BeTrue();
            overlay.Margin.Bottom.Should().BeApproximately(Math.Max(0, bottomMargin - height), 0.5,
                "the dual anchor applies as soon as both slots have a track");

            player.SecondaryText = "Secondary";
            Dispatcher.UIThread.RunJobs();
            vm.SecondaryVisible.Should().BeTrue();
            double dualHeight = overlay.Bounds.Height;
            overlay.Margin.Bottom.Should().BeApproximately(Math.Max(0, bottomMargin - dualHeight), 0.5,
                "a displayed secondary cue changes the block height, not the dual anchor choice");
            draft = vm.CreateSettings();
            draft.SubtitlePositionAlignmentWhenDual = "Center";
            draft.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            overlay.Margin.Bottom.Should().BeApproximately(Math.Max(0, bottomMargin - dualHeight / 2), 0.5);
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void Settings_window_editors_apply_separator_alignment_and_bitmap_values()
    {
        var (window, vm, player) = BuildWindow();
        SettingsViewModel draft = vm.CreateSettings();
        var settings = new SettingsWindow(draft) { Height = 900 };
        try
        {
            settings.Show();
            Dispatcher.UIThread.RunJobs();
            settings.FindControl<NumericUpDown>("SubtitleSeparatorWidthEditor")!.Text = "160";
            settings.FindControl<NumericUpDown>("SubtitleSeparatorOpacityEditor")!.Text = "40";
            settings.FindControl<ComboBox>("PositionAlignmentEditor")!.SelectedItem = "Center";
            settings.FindControl<ComboBox>("PositionAlignmentDualEditor")!.SelectedItem = "Top";
            settings.FindControl<NumericUpDown>("PrimaryBitmapScaleEditor")!.Text = 1.5.ToString(draft.NumberFormat);
            settings.FindControl<NumericUpDown>("SecondaryBitmapScaleEditor")!.Text = 0.5.ToString(draft.NumberFormat);
            settings.FindControl<NumericUpDown>("PrimaryBitmapPositionEditor")!.Text = "110";
            settings.FindControl<Button>("SaveButton")!.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            draft.Error.Should().BeNull();
            vm.SubtitleSeparatorMaxWidth.Should().Be(160);
            vm.SubtitleSeparatorOpacityPercent.Should().Be(40);
            vm.SubtitlePositionAlignment.Should().Be("Center");
            vm.SubtitlePositionAlignmentWhenDual.Should().Be("Top");
            player.PrimaryBitmapScale.Should().Be(1.5);
            player.SecondaryBitmapScale.Should().Be(0.5);
            player.PrimaryBitmapPosition.Should().Be(110);
            window.FindControl<Border>("SubtitleSeparator")!.MaxWidth.Should().Be(160);
        }
        finally
        {
            settings.Close();
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
}
