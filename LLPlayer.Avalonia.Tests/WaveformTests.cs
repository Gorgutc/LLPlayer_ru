using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using AwesomeAssertions;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

/// <summary>
/// F-12 waveform half in the shared Avalonia transport: engine-owned peaks exposed by the adapter, a mirrored
/// auto-gained envelope control and the persisted ShowWaveform toggle (default off, no decode work while off).
/// </summary>
public class WaveformTests
{
    [Fact]
    public void Envelope_layout_is_empty_for_missing_or_degenerate_input()
    {
        WaveformEnvelopeLayout.Compute(null, 400, 24).Should().BeEmpty();
        WaveformEnvelopeLayout.Compute([], 400, 24).Should().BeEmpty();
        WaveformEnvelopeLayout.Compute([0.4f, 0.8f], 0, 24).Should().BeEmpty();
        WaveformEnvelopeLayout.Compute([0.4f, 0.8f], 400, 0).Should().BeEmpty();
    }

    [Fact]
    public void Envelope_layout_uses_one_column_per_pixel_and_display_gain()
    {
        float[] peaks = [0.5f, 0.5f, 0.5f, 0.5f];
        double[] amps = WaveformEnvelopeLayout.Compute(peaks, 4, 24);
        amps.Should().HaveCount(4);
        // max=0.5 -> gain=0.95/0.5=1.9; amp=min(h/2, 0.5*1.9*h/2)=h/2*0.95
        amps.Should().OnlyContain(a => Math.Abs(a - 12 * 0.95) < 0.001);

        double[] wide = WaveformEnvelopeLayout.Compute(peaks, 400, 24);
        wide.Should().HaveCount(4, "one column per peak at most");
    }

    [Fact]
    public void Envelope_layout_collapses_a_silent_file_to_the_centre_line()
    {
        double[] amps = WaveformEnvelopeLayout.Compute([0f, 0f, 0f], 100, 24);
        amps.Should().HaveCount(3, "the helper still returns one column per peak for a silent file");
        amps.Should().OnlyContain(a => a == 0);
    }

    [Fact]
    public void Envelope_layout_reduces_each_column_to_its_loudest_peak_and_scales_with_height()
    {
        // 4 peaks -> 2 columns: column 0 covers [0.1, 0.9], column 1 covers [0.2, 0.3]; max overall 0.9.
        double[] amps = WaveformEnvelopeLayout.Compute([0.1f, 0.9f, 0.2f, 0.3f], 2, 24);
        amps.Should().HaveCount(2);
        double halfH = 12;
        double gain = 0.95 / 0.9;
        amps[0].Should().BeApproximately(Math.Min(halfH, 0.9 * gain * halfH), 0.001);
        amps[1].Should().BeApproximately(Math.Min(halfH, 0.3 * gain * halfH), 0.001);

        double[] taller = WaveformEnvelopeLayout.Compute([0.1f, 0.9f, 0.2f, 0.3f], 2, 48);
        taller[0].Should().BeApproximately(amps[0] * 2, 0.001, "the envelope scales with the available height");
    }

    [Fact]
    public void Envelope_layout_sanitizes_non_finite_or_out_of_range_peaks()
    {
        double[] amps = WaveformEnvelopeLayout.Compute([float.NaN, float.PositiveInfinity, 2.0f, -1.0f], 4, 24);
        amps.Should().HaveCount(4);
        amps.Should().OnlyContain(a => double.IsFinite(a) && a >= 0 && a <= 12);
        amps[2].Should().BeApproximately(12 * 0.95, 0.001, "out-of-range peaks clamp to the engine's 0..1 contract");
        amps[0].Should().Be(0);
        amps[1].Should().Be(0);
        amps[3].Should().Be(0);
    }

    [Fact]
    public void ShowWaveform_defaults_to_off_and_roundtrips_through_preferences()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        AppPrefs prefs = new AppPrefs().Normalize();
        prefs.ShowWaveform.Should().BeFalse("the engine must not decode audio while the waveform is off");
        prefs.ShowWaveform = true;
        store.Save(prefs);
        store.Load().ShowWaveform.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ToggleWaveform_applies_to_the_engine_and_persists()
    {
        FakePlaybackController player = new() { IsOpened = true };
        AppPrefs prefs = new AppPrefs().Normalize();
        List<AppPrefs> saved = [];
        using MainWindowViewModel vm = CreateVm(player, prefs, saved.Add);
        vm.ShowWaveform.Should().BeFalse();
        player.WaveformEnabled.Should().BeFalse();

        vm.ToggleWaveformCommand.Execute(null);
        vm.ShowWaveform.Should().BeTrue();
        player.WaveformEnabled.Should().BeTrue();
        saved.Should().HaveCount(1);
        saved[0].ShowWaveform.Should().BeTrue();

        vm.ToggleWaveformCommand.Execute(null);
        vm.ShowWaveform.Should().BeFalse();
        player.WaveformEnabled.Should().BeFalse();
        saved.Should().HaveCount(2);
    }

    [AvaloniaFact]
    public void Restart_restores_ShowWaveform_and_enables_the_engine_decode()
    {
        FakePlaybackController player = new() { IsOpened = true };
        AppPrefs prefs = new AppPrefs().Normalize();
        prefs.ShowWaveform = true;
        using MainWindowViewModel vm = CreateVm(player, prefs);
        vm.ShowWaveform.Should().BeTrue();
        player.WaveformEnabled.Should().BeTrue("the persisted toggle must re-enable the engine waveform on startup");
    }

    [AvaloniaFact]
    public void Player_peaks_are_mirrored_into_the_view_model()
    {
        FakePlaybackController player = new() { IsOpened = true };
        using MainWindowViewModel vm = CreateVm(player, new AppPrefs().Normalize());
        vm.WaveformPeaks.Should().BeNull();
        vm.WaveformActive.Should().BeFalse();

        player.WaveformPeaks = [0.1f, 0.7f, 0.3f];
        Dispatcher.UIThread.RunJobs();
        vm.WaveformPeaks.Should().Equal(0.1f, 0.7f, 0.3f);
        vm.WaveformActive.Should().BeTrue();

        player.WaveformPeaks = null;
        Dispatcher.UIThread.RunJobs();
        vm.WaveformActive.Should().BeFalse();
    }

    [AvaloniaFact]
    public void ToggleWaveform_keeps_the_live_switch_when_persisting_fails()
    {
        FakePlaybackController player = new() { IsOpened = true };
        AppPrefs prefs = new AppPrefs().Normalize();
        int saves = 0;
        using MainWindowViewModel vm = new(player, prefs,
            _ => { saves++; throw new IOException("disk full"); }, new FakeTranslator(), new FakeShell());
        vm.ToggleWaveformCommand.Execute(null); // must not throw; SavePrefs reports the failure via toast
        vm.ShowWaveform.Should().BeTrue();
        player.WaveformEnabled.Should().BeTrue("the live switch is applied before the failed preference write");
        saves.Should().Be(1);
    }

    static MainWindowViewModel CreateVm(FakePlaybackController player, AppPrefs prefs, Action<AppPrefs>? save = null)
        => new(player, prefs, save ?? (_ => { }), new FakeTranslator(), new FakeShell());

    [AvaloniaFact]
    public void Transport_shows_the_envelope_only_while_peaks_are_active()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var player = new FakePlaybackController { IsOpened = true, PrimaryText = "Primary" };
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 700 };
        MainWindowViewModel vm = CreateVm(player, new AppPrefs().Normalize());
        window.Attach(vm, renderer: null);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            WaveformEnvelope envelope = window.FindControl<WaveformEnvelope>("WaveformEnvelope")!;
            envelope.IsVisible.Should().BeFalse();

            vm.ToggleWaveformCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            envelope.IsVisible.Should().BeFalse("enabling the switch alone paints nothing until peaks exist");

            player.WaveformPeaks = [0.2f, 0.5f, 1.0f];
            Dispatcher.UIThread.RunJobs();
            envelope.IsVisible.Should().BeTrue();
            envelope.Peaks.Should().Equal(0.2f, 0.5f, 1.0f);

            vm.ToggleWaveformCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            envelope.IsVisible.Should().BeFalse("disabling the switch clears the engine peaks");
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void Waveform_envelope_actually_renders_pixels_when_peaks_exist()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var player = new FakePlaybackController { IsOpened = true };
        var window = new MainWindow(new KeyMapper(), null) { Width = 1000, Height = 700 };
        MainWindowViewModel vm = CreateVm(player, new AppPrefs().Normalize());
        window.Attach(vm, renderer: null);
        window.Show();
        try
        {
            vm.ToggleWaveformCommand.Execute(null);
            // A zero-amplitude file keeps the control active (layout/bounds) while painting only the centre line, so the
            // same sample can be read before and after a full-scale peak is applied.
            player.WaveformPeaks = [0f, 0f, 0f, 0f];
            Dispatcher.UIThread.RunJobs();
            WaveformEnvelope envelope = window.FindControl<WaveformEnvelope>("WaveformEnvelope")!;
            envelope.Bounds.Width.Should().BeGreaterThan(0);
            envelope.Bounds.Height.Should().BeGreaterThan(0);
            envelope.IsHitTestVisible.Should().BeFalse("the seek slider must keep every pointer action");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();

            // Sample above the 4-DIP slider track (and clear of the centred thumb), so the pixel proves the envelope
            // itself painted rather than the slider chrome — an unpainted/`Fill=null` control would keep this background.
            Point sample = envelope.TranslatePoint(
                new Point(envelope.Bounds.Width / 2, envelope.Bounds.Height / 2 - 6), window)!.Value;
            Color background = PixelAt(window, sample);
            player.WaveformPeaks = [1.0f, 1.0f, 1.0f, 1.0f];
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();
            Color painted = PixelAt(window, sample);
            painted.Should().NotBe(background, "a full-scale peak must paint the envelope outside the slider track");
            painted.Should().NotBe(Colors.Black, "the envelope fill must be visible over the dark transport bar");
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    static Color PixelAt(TopLevel window, Point point)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        using ILockedFramebuffer fb = frame.Lock();
        unsafe
        {
            byte* pixel = (byte*)fb.Address + (int)point.Y * fb.RowBytes + (int)point.X * 4;
            return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
        }
    }
}
