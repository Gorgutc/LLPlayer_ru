using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Views;

/// <summary>Friendly start-up error (FFmpeg libraries missing, engine failed to start).</summary>
public partial class ErrorWindow : Window
{
    public ErrorWindow() : this("Error", "", "")
    {
    }

    public ErrorWindow(string title, string message, string details)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        DetailsText.Text = details;
    }

    public string Details => DetailsText.Text ?? "";

    /// <summary>The window shown when the FFmpeg shared libraries cannot be found.</summary>
    public static ErrorWindow ForMissingFFmpeg(FFmpegLocation location)
    {
        string source = location.Source switch
        {
            FFmpegDirSource.CommandLine => "--ffmpeg-dir",
            FFmpegDirSource.Environment => "$" + FFmpegLocator.EnvironmentVariable,
            _ => "the app folder",
        };

        string details =
            $"Folder ({source}): {location.Directory}\n" +
            $"Missing: {string.Join(", ", location.MissingLibraries)}\n\n" +
            "Fix: put the FFmpeg 8.x shared libraries (libavcodec.so.62, libavformat.so.62, libavutil.so.60, ...) in\n" +
            "<app folder>/FFmpeg, or start with --ffmpeg-dir <dir> / set LLPLAYER_FFMPEG_DIR=<dir>.";

        return new ErrorWindow(
            "FFmpeg libraries not found",
            "LLPlayer plays media through FFmpeg 8 and could not find its shared libraries.",
            details);
    }

    void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard)
            _ = clipboard.SetTextAsync(Details);
    }

    void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
