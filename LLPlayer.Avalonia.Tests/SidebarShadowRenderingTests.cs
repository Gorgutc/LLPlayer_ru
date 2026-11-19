using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public sealed class SidebarShadowRenderingTests
{
    [AvaloniaFact]
    public void Switching_to_light_removes_the_sidebar_shadow_and_keeps_both_video_shadows()
    {
        var player = new FakePlaybackController
        {
            IsOpened = true,
            PrimaryText = "Primary video subtitle",
            SecondaryText = "Secondary video subtitle",
        };
        player.Cues[0].Add(new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(2), "Sidebar subtitle"));
        var window = new MainWindow(new KeyMapper(), null) { RequestedThemeVariant = ThemeVariant.Dark };
        using var vm = new MainWindowViewModel(player, new AppPrefs { SidebarVisible = true }.Normalize(),
            _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        vm.Sidebar.Reload();
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            SubtitleText sidebar = Assert.Single(window.FindControl<ListBox>("CueList")!
                .GetVisualDescendants().OfType<SubtitleText>());
            SubtitleText primary = window.FindControl<SubtitleText>("PrimarySubtitle")!;
            SubtitleText secondary = window.FindControl<SubtitleText>("SecondarySubtitle")!;

            Assert.Equal(ThemeVariant.Dark, sidebar.ActualThemeVariant);
            Assert.True(DrawsShadow(sidebar));
            Assert.True(DrawsShadow(primary));
            Assert.True(DrawsShadow(secondary));

            window.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ThemeVariant.Light, sidebar.ActualThemeVariant);
            Assert.False(DrawsShadow(sidebar), "Light sidebar text must render without the offset drop shadow.");
            Assert.True(DrawsShadow(primary));
            Assert.True(DrawsShadow(secondary));

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.True(DrawsShadow(sidebar));
        }
        finally
        {
            window.Close();
        }
    }

    static bool DrawsShadow(SubtitleText text)
    {
        var group = new DrawingGroup();
        using (DrawingContext context = group.Open())
            text.Render(context);

        GeometryDrawing[] glyphs = GeometryDrawings(group).ToArray();
        Assert.Contains(glyphs, glyph => glyph.Brush != null && glyph.Pen == null);
        return glyphs.Any(glyph => glyph.Brush != null && glyph.Pen != null);
    }

    static IEnumerable<GeometryDrawing> GeometryDrawings(DrawingGroup group)
    {
        foreach (Drawing drawing in group.Children)
        {
            if (drawing is GeometryDrawing geometry)
                yield return geometry;
            else if (drawing is DrawingGroup nested)
                foreach (GeometryDrawing child in GeometryDrawings(nested))
                    yield return child;
        }
    }
}
