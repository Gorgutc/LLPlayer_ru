using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;
using FlyleafLib.MediaPlayer.AI;
using LLPlayer.Extensions;
using LLPlayer.Services;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace LLPlayer.ViewModels;

/// <summary>
/// Word Manager (F-10): view / edit / delete the global cumulative word list and export it to Anki as a TSV
/// file, an .apkg deck, or a live AnkiConnect push. The list itself (dedup, persistence) is the pure
/// <see cref="WordListStore"/>; this VM is the WPF shell. Edits are written through to the store immediately.
/// The grid reloads when the store changes from another surface (word-click popup / AI Insights) while open.
/// </summary>
public class WordManagerDialogVM : Bindable, IDialogAware
{
    public FlyleafManager FL { get; }

    private readonly WordListStore _store;
    private readonly AnkiConnectSender _ankiConnect = new();

    // Set while this VM mutates the store itself, so the Changed handler does not reload (and disrupt) the grid
    // for our own edits/deletes — only EXTERNAL changes (popup / AI Insights) trigger a reload.
    private bool _suppressReload;

    public WordManagerDialogVM(FlyleafManager fl, WordListStore store)
    {
        FL = fl;
        _store = store;

        WordsView = CollectionViewSource.GetDefaultView(Words);
        WordsView.Filter = FilterPredicate;
    }

    public ObservableCollection<WordRowVM> Words { get; } = new();

    public ICollectionView WordsView { get; }

    public WordRowVM? SelectedWord { get; set => Set(ref field, value); }

    public string DeckName { get; set => Set(ref field, value); } = "LLPlayer";

    public string FilterText
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                WordsView.Refresh();
            }
        }
    } = string.Empty;

    public bool IsBusy
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(CanPush));
            }
        }
    }

    /// <summary>Gate for the async AnkiConnect push so a double-click cannot start two concurrent pushes.</summary>
    public bool CanPush => !IsBusy;

    public int WordCount => Words.Count;

    private bool FilterPredicate(object item)
    {
        if (item is not WordRowVM w)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }
        string f = FilterText.Trim();
        return Contains(w.Term, f)
               || Contains(w.Reading, f)
               || Contains(w.Translation, f)
               || Contains(w.Definition, f)
               || Contains(w.Example, f);

        static bool Contains(string s, string f) =>
            (s ?? "").Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    private void Reload()
    {
        Words.Clear();
        foreach (SavedWord w in _store.GetAll())
        {
            Words.Add(new WordRowVM(w, CommitRowEdit, RefreshFilterAfterEdit));
        }
        OnPropertyChanged(nameof(WordCount));
    }

    private void RefreshFilterAfterEdit()
    {
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            WordsView.Refresh();
        }
    }

    // A word was added/removed from another surface while this dialog is open → refresh (unless it was us).
    private void OnStoreChanged()
    {
        if (_suppressReload)
        {
            return;
        }
        Reload();
    }

    // Write-through for an in-place row edit; runs under the suppress flag so the store's Changed event does not
    // rebuild the grid mid-edit. Returns false only on a (UI-unreachable) rename collision / missing entry.
    private bool CommitRowEdit(SavedWord oldWord, SavedWord newWord)
    {
        _suppressReload = true;
        try
        {
            if (_store.Update(oldWord, newWord))
            {
                _store.Save();
                return true;
            }
            return false;
        }
        finally
        {
            _suppressReload = false;
        }
    }

    public DelegateCommand CmdDelete => field ??= new(() =>
    {
        if (SelectedWord == null)
        {
            return;
        }
        _suppressReload = true;
        try
        {
            if (_store.Remove(SelectedWord.Model))
            {
                _store.Save();
                Words.Remove(SelectedWord);
                OnPropertyChanged(nameof(WordCount));
            }
        }
        finally
        {
            _suppressReload = false;
        }
    });

    public DelegateCommand CmdExportTsv => field ??= new(() =>
    {
        var words = _store.GetAll();
        if (words.Count == 0)
        {
            ErrorDialogHelper.ShowKnownErrorPopup("The word list is empty.", "Word Manager");
            return;
        }

        string tsv = VocabularyParser.ToTsv(words.Select(w => w.ToVocabularyEntry()).ToList());

        SaveFileDialog dialog = new()
        {
            Title = "Export word list (TSV)",
            Filter = "TSV (*.tsv)|*.tsv|All files (*.*)|*.*",
            FileName = "llplayer-wordlist.tsv",
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dialog.FileName, tsv, new UTF8Encoding(false));
                Process.Start("explorer.exe", $@"/select,""{dialog.FileName}""");
            }
            catch (Exception ex)
            {
                ErrorDialogHelper.ShowKnownErrorPopup($"Could not save the file: {ex.Message}", "Word Manager");
            }
        }
    });

    public DelegateCommand CmdExportApkg => field ??= new(() =>
    {
        var words = _store.GetAll();
        if (words.Count == 0)
        {
            ErrorDialogHelper.ShowKnownErrorPopup("The word list is empty.", "Word Manager");
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Export Anki deck (.apkg)",
            Filter = "Anki deck (*.apkg)|*.apkg|All files (*.*)|*.*",
            FileName = $"{SanitizeFileName(DeckName)}.apkg",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        byte[]? bytes = AnkiApkgWriter.TryBuild(words, DeckName);
        if (bytes == null)
        {
            ErrorDialogHelper.ShowKnownErrorPopup(
                "Could not build the .apkg deck. You can still export TSV (File > Import in Anki) " +
                "or push to a running Anki via AnkiConnect.", "Word Manager");
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, bytes);
            Process.Start("explorer.exe", $@"/select,""{dialog.FileName}""");
        }
        catch (Exception ex)
        {
            ErrorDialogHelper.ShowKnownErrorPopup($"Could not save the file: {ex.Message}", "Word Manager");
        }
    });

    public AsyncDelegateCommand CmdPushAnkiConnect => field ??= new AsyncDelegateCommand(async () =>
    {
        var words = _store.GetAll();
        if (words.Count == 0)
        {
            ErrorDialogHelper.ShowKnownErrorPopup("The word list is empty.", "Word Manager");
            return;
        }

        IsBusy = true;
        try
        {
            AnkiAddNotesOutcome result = await _ankiConnect.PushAsync(words, DeckName);
            if (result.Error != null)
            {
                ErrorDialogHelper.ShowKnownErrorPopup(result.Error, "AnkiConnect");
                return;
            }

            string msg = $"Pushed {result.Added} card(s) to Anki deck \"{DeckName}\"" +
                         (result.Skipped > 0 ? $" ({result.Skipped} skipped as duplicates)" : "");
            FL.MessageQueue.Enqueue(msg);
        }
        finally
        {
            IsBusy = false;
        }
    }).ObservesCanExecute(() => CanPush);

    public DelegateCommand CmdClearAll => field ??= new(() =>
    {
        if (Words.Count == 0)
        {
            return;
        }
        var result = System.Windows.MessageBox.Show(
            $"Remove all {Words.Count} words from the list? This cannot be undone.",
            "Word Manager", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }
        _suppressReload = true;
        try
        {
            _store.Clear();
            _store.Save();
        }
        finally
        {
            _suppressReload = false;
        }
        Words.Clear();
        OnPropertyChanged(nameof(WordCount));
    });

    private static string SanitizeFileName(string name)
    {
        string s = string.IsNullOrWhiteSpace(name) ? "LLPlayer" : name.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            s = s.Replace(c, '_');
        }
        return s;
    }

    #region IDialogAware
    public string Title { get; set => Set(ref field, value); }
        = $"Word Manager - {App.Name}";
    public double WindowWidth { get; set => Set(ref field, value); } = 900;
    public double WindowHeight { get; set => Set(ref field, value); } = 600;

    public DialogCloseListener RequestClose { get; }

    public bool CanCloseDialog() => true;

    public void OnDialogClosed()
    {
        _store.Changed -= OnStoreChanged;
    }

    public void OnDialogOpened(IDialogParameters parameters)
    {
        Reload();
        _store.Changed += OnStoreChanged;
    }
    #endregion
}

/// <summary>
/// Editable row over an immutable <see cref="SavedWord"/>. Term is read-only (the dedup key); editing any of
/// the other fields commits a replacement through to the store (via the VM's write-through callback).
/// </summary>
public sealed class WordRowVM : Bindable
{
    private readonly Func<SavedWord, SavedWord, bool> _commit;
    private readonly Action _afterEdit;
    private SavedWord _model;

    public WordRowVM(SavedWord model, Func<SavedWord, SavedWord, bool> commit, Action afterEdit)
    {
        _model = model;
        _commit = commit;
        _afterEdit = afterEdit;
    }

    public SavedWord Model => _model;

    public string Term => _model.Term;

    public string Reading
    {
        get => _model.Reading;
        set => Edit(_model with { Reading = value ?? "" });
    }

    public string Translation
    {
        get => _model.Translation;
        set => Edit(_model with { Translation = value ?? "" });
    }

    public string Definition
    {
        get => _model.Definition;
        set => Edit(_model with { Definition = value ?? "" });
    }

    public string Example
    {
        get => _model.Example;
        set => Edit(_model with { Example = value ?? "" });
    }

    public string Source => _model.Source.ToString();

    public string Languages =>
        string.IsNullOrEmpty(_model.SourceLanguage)
            ? string.Empty
            : $"{_model.SourceLanguage} → {_model.TargetLanguage}";

    public string Saved => FormatSaved(_model.SavedAtUtc);

    private void Edit(SavedWord updated)
    {
        bool committed = _commit(_model, updated);
        if (committed)
        {
            _model = updated;
        }
        OnPropertyChanged(string.Empty); // record replaced (or edit rejected) — refresh all bound columns
        if (committed)
        {
            _afterEdit();
        }
    }

    private static string FormatSaved(string iso)
    {
        return DateTimeOffset.TryParse(iso, out DateTimeOffset dt)
            ? dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : iso ?? string.Empty;
    }
}
