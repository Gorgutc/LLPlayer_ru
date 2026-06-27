using System.Diagnostics;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using FlyleafLib.MediaPlayer;
using LLPlayer.Extensions;
using LLPlayer.Services;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace LLPlayer.ViewModels;

public class SubtitlesExportDialogVM : Bindable, IDialogAware
{
    public FlyleafManager FL { get; }

    public SubtitlesExportDialogVM(FlyleafManager fl)
    {
        FL = fl;

        IsUtf8Bom = FL.Config.Subs.SubsExportUTF8WithBom;
    }

    public List<int> SubIndexList { get; } = [0, 1];
    public int SelectedSubIndex
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(SubManager));
            }
        }
    }

    public SubManager SubManager => FL.Player.SubtitlesManager[SelectedSubIndex];

    public bool IsUtf8Bom { get; set => Set(ref field, value); }

    public TranslateExportOptions SelectedTranslateOpts { get; set => Set(ref field, value); }

    public IReadOnlyList<SubtitleExportFormat> FormatList { get; } =
        [SubtitleExportFormat.Srt, SubtitleExportFormat.Vtt, SubtitleExportFormat.Txt];

    public SubtitleExportFormat SelectedFormat { get; set => Set(ref field, value); } = SubtitleExportFormat.Srt;

    public DelegateCommand CmdExport => field ??= new(() =>
    {
        var playlist = FL.Player.Playlist.Selected;
        if (playlist == null)
        {
            return;
        }

        bool exportOriginal = SelectedTranslateOpts == TranslateExportOptions.Original;

        List<SubtitleExportLine> lines = FL.Player.SubtitlesManager[SelectedSubIndex].Subs
            .Where(s =>
            {
                if (!s.IsText)
                {
                    return false;
                }

                if (SelectedTranslateOpts == TranslateExportOptions.TranslatedOnly)
                {
                    return s.IsTranslated;
                }

                return true;
            })
            .Select(s => new SubtitleExportLine(
                s.StartTime,
                s.EndTime,
                (exportOriginal ? s.Text : s.DisplayText)!,
                // SubStyles offsets index into the original Text; they no longer match once translated, so only
                // carry them when exporting the original text (italic tags are emitted for SRT/VTT from these).
                exportOriginal ? s.SubStyles : null))
            .ToList();

        if (lines.Count == 0)
        {
            ErrorDialogHelper.ShowKnownErrorPopup("There were no subtitles to export.", "Export");
            return;
        }

        string? initDir = null;
        string fileName;

        if (FL.Player.Playlist.Url != null && File.Exists(playlist.Url))
        {
            fileName = Path.GetFileNameWithoutExtension(playlist.Url);

            // If there is currently an open file, set that folder as the base folder
            initDir = Path.GetDirectoryName(playlist.Url);
        }
        else
        {
            // If live video, use title instead
            fileName = playlist.Title;
        }

        string ext = SubtitleExporter.Extension(SelectedFormat);
        string filterName = SelectedFormat switch
        {
            SubtitleExportFormat.Srt => "SubRip subtitle",
            SubtitleExportFormat.Vtt => "WebVTT subtitle",
            SubtitleExportFormat.Txt => "Plain text",
            _ => "Subtitle"
        };

        if (!exportOriginal)
        {
            fileName += ".translated";
        }

        SaveFileDialog saveFileDialog = new()
        {
            Filter = $"{filterName} (*{ext})|*{ext}|All files (*.*)|*.*",
            FileName = fileName + ext,
            InitialDirectory = initDir
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            File.WriteAllText(
                saveFileDialog.FileName,
                SubtitleExporter.Build(lines, SelectedFormat),
                new UTF8Encoding(IsUtf8Bom));

            // open saved file in explorer
            Process.Start("explorer.exe", $@"/select,""{saveFileDialog.FileName}""");
        }
    });

    #region IDialogAware
    public string Title { get; set => Set(ref field, value); }
        = $"Subtitles Exporter - {App.Name}";
    public double WindowWidth { get; set => Set(ref field, value); } = 350;
    public double WindowHeight { get; set => Set(ref field, value); } = 280;

    public DialogCloseListener RequestClose { get; }

    public bool CanCloseDialog()
    {
        return true;
    }
    public void OnDialogClosed() { }

    public void OnDialogOpened(IDialogParameters parameters) { }
    #endregion

    public enum TranslateExportOptions
    {
        Original,
        TranslatedOnly,
        TranslatedWithFallback
    }
}
