using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
/// WPF "Subtitles > Position / Size" text-layout adaptation: box width percent, max width, ignore line breaks,
/// primary/secondary distance and the additive Avalonia line alignment (WPF keeps subtitle lines centered).
/// The vertical base offset keeps its previously approved DIP model; bitmap scale/position, the dual-text separator
/// and per-mode alignment stay outside this slice.
/// </summary>
public class SubtitleLayoutSettingsTests
{
    static MainWindowViewModel CreateVm(AppPrefs prefs, Action<AppPrefs>? save = null)
        => new(new FakePlaybackController(), prefs, save ?? (_ => { }), new FakeTranslator(), new FakeShell());

    // ---- defaults and normalization ------------------------------------------------------------------------------

    [Fact]
    public void Missing_layout_keys_restore_the_current_look()
    {
        AppPrefs prefs = JsonSerializer.Deserialize<AppPrefs>("{}", PrefsStore.JsonOptions)!.Normalize();
        prefs.SubtitleWidthPercent.Should().Be(100);
        prefs.SubtitleMaxWidth.Should().Be(0);
        prefs.SubtitleIgnoreLineBreak.Should().BeFalse();
        prefs.SubtitleLineAlignment.Should().Be("Center");
        prefs.SubtitlePrimarySecondaryDistance.Should().Be(2);

        using var vm = CreateVm(prefs);
        vm.SubtitleWidthPercent.Should().Be(100);
        vm.SubtitleMaxWidth.Should().Be(0);
        vm.SubtitleIgnoreLineBreak.Should().BeFalse();
        vm.SubtitleLineAlignment.Should().Be("Center");
        vm.SubtitlePrimarySecondaryDistance.Should().Be(2);
        vm.SubtitleTextAlignment.Should().Be(TextAlignment.Center);
    }

    [Theory]
    [InlineData("-5", 1)]
    [InlineData("0", 1)]
    [InlineData("1", 1)]
    [InlineData("50", 50)]
    [InlineData("100", 100)]
    [InlineData("130", 100)]
    public void Width_percent_normalizes_to_1_100(string jsonValue, int expected)
        => Normalize("SubtitleWidthPercent", jsonValue).SubtitleWidthPercent.Should().Be(expected);

    [Theory]
    [InlineData("-20", 0)]
    [InlineData("0", 0)]
    [InlineData("640.5", 640.5)]
    [InlineData("4000", 4000)]
    [InlineData("9999", 4000)]
    public void Max_width_normalizes_to_0_4000(string jsonValue, double expected)
        => Normalize("SubtitleMaxWidth", jsonValue).SubtitleMaxWidth.Should().Be(expected);

    [Theory]
    [InlineData("-1", 0)]
    [InlineData("0", 0)]
    [InlineData("2", 2)]
    [InlineData("18.5", 18.5)]
    [InlineData("250", 200)]
    [InlineData("\"NaN\"", 2)]
    [InlineData("\"Infinity\"", 2)]
    public void Primary_secondary_distance_normalizes_to_0_200(string jsonValue, double expected)
        => Normalize("SubtitlePrimarySecondaryDistance", jsonValue).SubtitlePrimarySecondaryDistance.Should().Be(expected);

    [Theory]
    [InlineData("\"left\"", "Left")]
    [InlineData("\" CENTER \"", "Center")]
    [InlineData("\"Right\"", "Right")]
    [InlineData("\"justified\"", "Center")]
    [InlineData("\"\"", "Center")]
    public void Alignment_normalizes_to_a_supported_value(string jsonValue, string expected)
        => Normalize("SubtitleLineAlignment", jsonValue).SubtitleLineAlignment.Should().Be(expected);

    [Theory]
    [InlineData("\"NaN\"", 0)]
    [InlineData("\"Infinity\"", 0)]
    public void Non_finite_max_width_falls_back_to_zero(string jsonValue, double expected)
    {
        var options = new JsonSerializerOptions(PrefsStore.JsonOptions) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        AppPrefs prefs = JsonSerializer.Deserialize<AppPrefs>("{\"SubtitleMaxWidth\":" + jsonValue + "}", options)!.Normalize();
        prefs.SubtitleMaxWidth.Should().Be(expected);
    }

    static AppPrefs Normalize(string property, string jsonValue)
    {
        var options = new JsonSerializerOptions(PrefsStore.JsonOptions) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        AppPrefs prefs = JsonSerializer.Deserialize<AppPrefs>("{\"" + property + "\":" + jsonValue + "}", options)!.Normalize();
        return prefs;
    }

    // ---- settings draft validation and transaction ---------------------------------------------------------------

    [Fact]
    public void Settings_draft_exposes_the_five_layout_fields_with_defaults()
    {
        using var vm = CreateVm(new AppPrefs().Normalize());
        SettingsViewModel draft = vm.CreateSettings();
        draft.SubtitleWidthPercent.Should().Be(100);
        draft.SubtitleWidthPercentText.Should().Be("100");
        draft.SubtitleMaxWidth.Should().Be(0);
        draft.SubtitleMaxWidthText.Should().Be("0");
        draft.SubtitlePrimarySecondaryDistance.Should().Be(2);
        draft.SubtitlePrimarySecondaryDistanceText.Should().Be("2");
        draft.SubtitleIgnoreLineBreak.Should().BeFalse();
        draft.SubtitleLineAlignment.Should().Be("Center");
        SettingsViewModel.LineAlignments.Should().Equal("Center", "Left", "Right");
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("100", true)]
    [InlineData("101", false)]
    [InlineData("-3", false)]
    [InlineData("2.5", false)]
    public void Invalid_width_blocks_save_with_a_visible_error(string width, bool shouldSave)
    {
        using var vm = CreateVm(new AppPrefs().Normalize());
        SettingsViewModel draft = vm.CreateSettings();
        draft.SubtitleWidthPercentText = width;
        draft.SaveCommand.Execute(null);
        if (shouldSave)
        {
            draft.Error.Should().BeNull();
        }
        else
        {
            draft.Error.Should().NotBeNull();
            draft.Error.Should().Contain("width");
        }
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("4000", true)]
    [InlineData("4001", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]
    public void Max_width_draft_validation(string value, bool shouldSave)
    {
        using var vm = CreateVm(new AppPrefs().Normalize());
        SettingsViewModel draft = vm.CreateSettings();
        draft.SubtitleMaxWidthText = value;
        draft.SaveCommand.Execute(null);
        (draft.Error == null).Should().Be(shouldSave);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("200", true)]
    [InlineData("200.5", false)]
    [InlineData("-2", false)]
    public void Distance_draft_validation(string value, bool shouldSave)
    {
        using var vm = CreateVm(new AppPrefs().Normalize());
        SettingsViewModel draft = vm.CreateSettings();
        draft.SubtitlePrimarySecondaryDistanceText = value;
        draft.SaveCommand.Execute(null);
        (draft.Error == null).Should().Be(shouldSave);
    }

    [Fact]
    public void Save_persists_layout_before_apply_and_preserves_unrelated_preferences()
    {
        AppPrefs prefs = new AppPrefs { Volume = 42, SubtitleTextColor = "#112233", SubtitleBackgroundOpacityPercent = 30 }.Normalize();
        AppPrefs? saved = null;
        MainWindowViewModel? live = null;
        using var vm = new MainWindowViewModel(new FakePlaybackController(), prefs, candidate =>
        {
            saved = JsonSerializer.Deserialize<AppPrefs>(JsonSerializer.Serialize(candidate, PrefsStore.JsonOptions), PrefsStore.JsonOptions);
            live!.SubtitleWidthPercent.Should().Be(100, "the live state must not change before persistence succeeds");
        }, new FakeTranslator(), new FakeShell());
        live = vm;

        SettingsViewModel draft = vm.CreateSettings();
        draft.SubtitleWidthPercentText = "60";
        draft.SubtitleMaxWidthText = "700";
        draft.SubtitlePrimarySecondaryDistanceText = "12";
        draft.SubtitleIgnoreLineBreak = true;
        draft.SubtitleLineAlignment = "Right";
        draft.SaveCommand.Execute(null);

        draft.Error.Should().BeNull();
        saved.Should().NotBeNull();
        saved!.SubtitleWidthPercent.Should().Be(60);
        saved.SubtitleMaxWidth.Should().Be(700);
        saved.SubtitlePrimarySecondaryDistance.Should().Be(12);
        saved.SubtitleIgnoreLineBreak.Should().BeTrue();
        saved.SubtitleLineAlignment.Should().Be("Right");
        saved.Volume.Should().Be(42);
        saved.SubtitleTextColor.Should().Be("#112233");
        saved.SubtitleBackgroundOpacityPercent.Should().Be(30);

        vm.SubtitleWidthPercent.Should().Be(60);
        vm.SubtitleMaxWidth.Should().Be(700);
        vm.SubtitlePrimarySecondaryDistance.Should().Be(12);
        vm.SubtitleIgnoreLineBreak.Should().BeTrue();
        vm.SubtitleLineAlignment.Should().Be("Right");
        vm.SubtitleTextAlignment.Should().Be(TextAlignment.Right);
    }

    [Fact]
    public void Save_failure_keeps_live_and_persisted_state_and_leaves_the_dialog_open()
    {
        AppPrefs prefs = new AppPrefs().Normalize();
        using var vm = new MainWindowViewModel(new FakePlaybackController(), prefs, _ => throw new IOException("disk full"), new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = vm.CreateSettings();
        draft.SubtitleWidthPercentText = "40";
        draft.SaveCommand.Execute(null);

        draft.Error.Should().Contain("disk full");
        vm.SubtitleWidthPercent.Should().Be(100);
        prefs.SubtitleWidthPercent.Should().Be(100);
    }

    [Fact]
    public void Close_discards_layout_edits()
    {
        AppPrefs prefs = new AppPrefs().Normalize();
        using var vm = CreateVm(prefs);
        SettingsViewModel draft = vm.CreateSettings();
        draft.SubtitleWidthPercentText = "35";
        draft.SubtitleLineAlignment = "Left";
        draft.CloseCommand.Execute(null);
        vm.SubtitleWidthPercent.Should().Be(100);
        vm.SubtitleLineAlignment.Should().Be("Center");
        prefs.SubtitleWidthPercent.Should().Be(100);
    }

    [Fact]
    public void Persisted_layout_survives_restart()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        AppPrefs prefs = new AppPrefs().Normalize();
        using (var vm = new MainWindowViewModel(new FakePlaybackController(), prefs, store.Save, new FakeTranslator(), new FakeShell()))
        {
            SettingsViewModel draft = vm.CreateSettings();
            draft.SubtitleWidthPercentText = "70";
            draft.SubtitleMaxWidthText = "800";
            draft.SubtitlePrimarySecondaryDistanceText = "9";
            draft.SubtitleIgnoreLineBreak = true;
            draft.SubtitleLineAlignment = "Left";
            draft.SaveCommand.Execute(null);
            draft.Error.Should().BeNull();
        }

        using var restarted = new MainWindowViewModel(new FakePlaybackController(), store.Load(), _ => { }, new FakeTranslator(), new FakeShell());
        restarted.SubtitleWidthPercent.Should().Be(70);
        restarted.SubtitleMaxWidth.Should().Be(800);
        restarted.SubtitlePrimarySecondaryDistance.Should().Be(9);
        restarted.SubtitleIgnoreLineBreak.Should().BeTrue();
        restarted.SubtitleLineAlignment.Should().Be("Left");
        restarted.SubtitleTextAlignment.Should().Be(TextAlignment.Left);
    }

    [Theory]
    [InlineData(1000, 100, 0, 904)]          // the approved 48-DIP side margins stay
    [InlineData(1000, 50, 0, 452)]
    [InlineData(1000, 100, 400, 400)]
    [InlineData(640, 100, 0, 544)]
    [InlineData(200, 100, 0, 104)]
    [InlineData(50, 100, 0, 1)]              // never a zero/negative box in a squeezed window
    [InlineData(1000, 1, 0, 9.04)]           // the minimum 1 percent still rounds to a usable box
    [InlineData(double.NaN, 100, 0, 1)]      // non-finite video width degrades to the 1-DIP floor
    [InlineData(double.PositiveInfinity, 100, 0, 1)]
    [InlineData(-100, 100, 0, 1)]
    [InlineData(1000, 0, 0, 9.04)]           // percent is clamped to 1..100
    [InlineData(1000, 101, 0, 904)]
    [InlineData(1000, 100, double.NaN, 904)] // a non-finite cap is ignored
    [InlineData(1000, 100, 5000, 904)]       // an oversized cap never grows the box
    public void Box_width_is_percent_of_the_padded_video_width_capped_by_max_width(double videoWidth, int percent, double maxWidth, double expected)
        => MainWindowViewModel.ComputeSubtitleBoxWidth(videoWidth, percent, maxWidth).Should().BeApproximately(expected, 0.001);

    // ---- live window wiring --------------------------------------------------------------------------------------

    static (MainWindow Window, MainWindowViewModel Vm, FakePlaybackController Player) BuildWindow()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var player = new FakePlaybackController { IsOpened = true, PrimaryText = "Primary", SecondaryText = "Secondary" };
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 700 };
        MainWindowViewModel vm = new(player, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, player);
    }

    [AvaloniaFact]
    public void Overlay_width_distance_alignment_and_line_break_follow_live_preferences()
    {
        var (window, vm, _) = BuildWindow();
        try
        {
            StackPanel overlay = window.FindControl<StackPanel>("SubtitleOverlay")!;
            Panel videoHost = window.FindControl<Panel>("VideoHost")!;
            overlay.HorizontalAlignment.Should().Be(HorizontalAlignment.Center,
                "the computed box width only keeps the old geometry while the box is centered");
            double expected = MainWindowViewModel.ComputeSubtitleBoxWidth(videoHost.Bounds.Width, 100, 0);
            overlay.Width.Should().BeApproximately(expected, 0.5);
            Border separator = window.FindControl<Border>("SubtitleSeparator")!;
            separator.Height.Should().Be(2, "the dual separator supplies the primary/secondary distance");
            SubtitleText primary = window.FindControl<SubtitleText>("PrimarySubtitle")!;
            primary.TextAlignment.Should().Be(TextAlignment.Center);
            primary.IgnoreLineBreaks.Should().BeFalse();

            SettingsViewModel draft = vm.CreateSettings();
            draft.SubtitleWidthPercentText = "50";
            draft.SubtitlePrimarySecondaryDistanceText = "14";
            draft.SubtitleIgnoreLineBreak = true;
            draft.SubtitleLineAlignment = "Right";
            draft.SaveCommand.Execute(null);
            draft.Error.Should().BeNull();
            Dispatcher.UIThread.RunJobs();

            double half = MainWindowViewModel.ComputeSubtitleBoxWidth(videoHost.Bounds.Width, 50, 0);
            overlay.Width.Should().BeApproximately(half, 0.5);
            separator.Height.Should().Be(14);
            primary.TextAlignment.Should().Be(TextAlignment.Right);
            primary.IgnoreLineBreaks.Should().BeTrue();

            // The live max-width cap must apply immediately, without waiting for a resize.
            SettingsViewModel capped = vm.CreateSettings();
            capped.SubtitleWidthPercentText = "100";
            capped.SubtitleMaxWidthText = "300";
            capped.SaveCommand.Execute(null);
            capped.Error.Should().BeNull();
            Dispatcher.UIThread.RunJobs();
            overlay.Width.Should().BeApproximately(300, 0.5);
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void Settings_editors_drive_the_layout_draft_and_save()
    {
        var (window, vm, _) = BuildWindow();
        SettingsViewModel draft = vm.CreateSettings();
        var settings = new SettingsWindow(draft) { Height = 900 };
        try
        {
            settings.Show();
            Dispatcher.UIThread.RunJobs();
            settings.FindControl<NumericUpDown>("SubtitleWidthEditor")!.Text = "40";
            settings.FindControl<NumericUpDown>("SubtitleMaxWidthEditor")!.Text = "500";
            settings.FindControl<NumericUpDown>("SubtitleDistanceEditor")!.Text = "11";
            CheckBox ignore = settings.FindControl<CheckBox>("IgnoreLineBreakEditor")!;
            ignore.IsChecked = true;
            ComboBox alignment = settings.FindControl<ComboBox>("SubtitleAlignmentEditor")!;
            alignment.SelectedItem = "Left";
            Dispatcher.UIThread.RunJobs();
            settings.FindControl<Button>("SaveButton")!.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            draft.Error.Should().BeNull();
            vm.SubtitleWidthPercent.Should().Be(40);
            vm.SubtitleMaxWidth.Should().Be(500);
            vm.SubtitlePrimarySecondaryDistance.Should().Be(11);
            vm.SubtitleIgnoreLineBreak.Should().BeTrue();
            vm.SubtitleLineAlignment.Should().Be("Left");
            vm.SubtitleTextAlignment.Should().Be(TextAlignment.Left);
        }
        finally
        {
            settings.Close();
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void Overlay_box_is_recomputed_on_resize_and_when_the_sidebar_toggles()
    {
        var (window, vm, _) = BuildWindow();
        try
        {
            StackPanel overlay = window.FindControl<StackPanel>("SubtitleOverlay")!;
            Panel videoHost = window.FindControl<Panel>("VideoHost")!;
            double visibleWidth = videoHost.Bounds.Width;

            window.Width = 800;
            Dispatcher.UIThread.RunJobs();
            videoHost.Bounds.Width.Should().BeLessThan(visibleWidth);
            overlay.Width.Should().BeApproximately(MainWindowViewModel.ComputeSubtitleBoxWidth(videoHost.Bounds.Width, 100, 0), 0.5);

            vm.ToggleSidebarCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            overlay.Width.Should().BeApproximately(MainWindowViewModel.ComputeSubtitleBoxWidth(videoHost.Bounds.Width, 100, 0), 0.5);
            videoHost.Bounds.Width.Should().BeGreaterThan(1);
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
}
