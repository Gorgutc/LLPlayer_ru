using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlyleafLib.MediaPlayer.AI;
using LLPlayer.ControlApi;
using LLPlayer.Core.Services;
using LLPlayer.Services;

namespace LLPlayer.Core.Automation;

public sealed record ControlWordList(string? Filter = null, int Offset = 0, int Limit = 64, string? Revision = null);
public sealed record ControlWordPage(string Revision, IReadOnlyList<SavedWord> Words, int TotalCount, int? NextOffset);
public sealed record ControlWordsSend(string? Deck = null, IReadOnlyList<string>? Terms = null, string? Revision = null);
public enum ControlWordsFormat { Tsv, Apkg }
public sealed record ControlWordsExport(string Path, ControlWordsFormat Format, bool Overwrite = false,
    string? Deck = null, IReadOnlyList<string>? Terms = null, string? Revision = null);
public sealed record ControlWordsExportResult(string Path, ControlWordsFormat Format, int Count);

/// <summary>Reads the existing library as a detached snapshot before export or submission to Anki.</summary>
public sealed class ControlWordOperations(string path, AnkiConnectSender? sender = null)
{
    private sealed record ExportCheckpoint(string StageSha256);
    private readonly AnkiConnectSender anki = sender ?? new();

    public ControlWordLookupResult Lookup(ControlWordLookup args)
    {
        SavedWord[] words = ReadWords();
        string revision = Digest(words);
        RequireRevision(args.Revision, revision);
        SavedWord? found = Find(words, args.Term);
        return new(found is not null, found is null ? null : new(found.Term, found.Reading, found.Translation, found.Definition,
            found.Example, found.SourceLanguage, found.TargetLanguage), revision);
    }

    public static ControlWordsChanged SaveWord(WordLibrary library, ControlWordSave args, CancellationToken token = default) =>
        Change(library, args.Revision, store => store.TryAdd(FromInput(args.Word, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)))
            ? 1 : throw Error("word_conflict", "The word is already in the library."), token);

    public static ControlWordsChanged UpdateWord(WordLibrary library, ControlWordUpdate args, CancellationToken token = default) => Change(library, args.Revision, store =>
    {
        SavedWord old = Find(store.GetAll(), args.OldTerm) ?? throw Error("unknown_word", "The word is no longer in the library.");
        SavedWord edited = FromInput(args.Word, old.SavedAtUtc) with { Source = old.Source };
        if (edited == old) return 0;
        return store.Update(old, edited) ? 1 : throw Error("word_conflict", "The new term already exists in the library.");
    }, token);

    public static ControlWordsChanged RemoveWord(WordLibrary library, ControlWordRemove args, CancellationToken token = default) => Change(library, args.Revision, store =>
    {
        SavedWord word = Find(store.GetAll(), args.Term) ?? throw Error("unknown_word", "The word is no longer in the library.");
        return store.Remove(word) ? 1 : 0;
    }, token);

    public static ControlWordsChanged ClearWords(WordLibrary library, ControlWordClear args, CancellationToken token = default) => Change(library, args.Revision, store =>
    {
        int count = store.Count;
        if (count > 0) store.Clear();
        return count;
    }, token);

    public static ControlWordsChanged MergeVocabulary(WordLibrary library, ControlVocabularyMerge args, CancellationToken token = default) => Change(library, args.Revision,
        store => store.MergeVocabulary(args.Vocabulary.Select(item => new VocabularyEntry(item.Term, item.Reading,
            item.Translation, item.Definition, item.Example)).ToArray(), args.SourceLanguage, args.TargetLanguage,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)), token);

    public ControlWordPage List(ControlWordList args)
    {
        SavedWord[] all = ReadWords();
        string revision = Digest(all);
        RequireRevision(args.Revision, revision);
        string filter = args.Filter?.Trim() ?? "";
        var words = filter.Length == 0 ? all : all.Where(word =>
            word.Term.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            word.Reading.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            word.Translation.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            word.Definition.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            word.Example.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (args.Limit is < 1 or > 128 || args.Offset < 0 || args.Offset > words.Length ||
            (args.Offset != 0 && args.Revision is null))
            throw Error("invalid_arguments", "The word page range is invalid or the cursor's revision is missing.");
        int count = Math.Min(words.Length - args.Offset, args.Limit);
        int next = args.Offset + count;
        return new(revision, words.AsSpan(args.Offset, count).ToArray(), words.Length, next < words.Length ? next : null);
    }

    public async Task<ControlJobOutcome> SendAsync(ControlJobContext context, ControlWordsSend args, CancellationToken token)
    {
        const string effectId = "anki.add_notes";
        SavedWord[] words = Select(args.Terms, args.Revision);
        context.VerifyIdentity("word_selection", Digest(words));
        if (context.GetExternalEffectReceipt(effectId) is JsonElement existing)
            return new(ControlJobStatus.Completed, existing);
        var outcome = await anki.PushWithTrackedNotesAsync(words, Deck(args.Deck), async (send, ct) =>
        {
            JsonElement receipt = await context.RunExternalEffectAsync(effectId, async dispatchToken =>
            {
                AnkiAddNotesOutcome confirmed;
                try { confirmed = await send(dispatchToken).ConfigureAwait(false); }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or JsonException)
                { throw UnknownEffect(); }
                if (confirmed.Error is not null) throw UnknownEffect();
                return ControlProtocol.Encode(confirmed);
            }, ct).ConfigureAwait(false);
            return receipt.Deserialize<AnkiAddNotesOutcome>(ControlProtocol.Json)!;
        }, token).ConfigureAwait(false);
        if (outcome.Error is not null)
            throw new ControlJobException("anki_unavailable", "AnkiConnect could not prepare the target deck and model.", retryable: true);
        return new(ControlJobStatus.Completed, ControlProtocol.Encode(outcome));
    }

    public async Task<ControlJobOutcome> ExportAsync(ControlJobContext context, ControlWordsExport args, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(args.Path) || !Enum.IsDefined(args.Format))
            throw Error("invalid_arguments", "A destination path and a supported word export format are required.");
        SavedWord[] words = Select(args.Terms, args.Revision);
        context.VerifyIdentity("word_selection", Digest(words));
        string destination = Path.GetFullPath(args.Path);
        string stage = Path.Combine(Path.GetDirectoryName(destination)!, $".llplayer-words-{context.JobId:N}.tmp");
        bool recoverable = false;
        bool ownsStage = false;
        try
        {
            if (File.Exists(stage))
            {
                string? expectedHash = context.Checkpoint?.Deserialize<ExportCheckpoint>(ControlProtocol.Json)?.StageSha256;
                await using var existing = new FileStream(stage, FileMode.Open, FileAccess.Read, FileShare.Read, 4_096, useAsync: true);
                string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(existing, token).ConfigureAwait(false));
                if (!context.IsRecovery || expectedHash != actualHash || File.GetAttributes(stage).HasFlag(FileAttributes.ReparsePoint))
                    throw new ControlJobException("staging_changed", "The word export staging file does not match its retained checkpoint.",
                        ControlJobStatus.Interrupted);
                ownsStage = true;
            }
            else
            {
                byte[] output = args.Format == ControlWordsFormat.Tsv
                    ? Encoding.UTF8.GetBytes(VocabularyParser.ToTsv(words.Select(word => word.ToVocabularyEntry()).ToArray()))
                    : AnkiApkgWriter.TryBuild(words, Deck(args.Deck)) ?? throw Error("apkg_failed", "The Anki package could not be created.");
                context.SaveCheckpoint(ControlProtocol.Encode(new ExportCheckpoint(Convert.ToHexString(SHA256.HashData(output)))));
                await StageAsync(stage, output, token).ConfigureAwait(false);
                ownsStage = true;
            }
            await context.PublishFileAsync("words_export", stage, destination, args.Overwrite, token).ConfigureAwait(false);
        }
        catch (ControlJobException e) when (e.Status == ControlJobStatus.Interrupted)
        {
            recoverable = true;
            throw;
        }
        finally
        {
            if (ownsStage && !recoverable) DeleteStagingFile(stage);
        }
        return new(ControlJobStatus.Completed, ControlProtocol.Encode(new ControlWordsExportResult(destination, args.Format, words.Length)));
    }

    private SavedWord[] Select(IReadOnlyList<string>? terms, string? expectedRevision)
    {
        SavedWord[] all = ReadWords();
        RequireRevision(expectedRevision, Digest(all));
        if (terms is not null)
        {
            if (terms.Count is < 1 or > 4_096 || terms.Any(string.IsNullOrWhiteSpace))
                throw Error("invalid_arguments", "Provide a bounded list of nonempty terms.");
            var selected = terms.Select(term => term.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            all = all.Where(word => selected.Contains(word.DedupeKey)).ToArray();
            if (all.Length != selected.Count) throw Error("unknown_word", "A selected term is no longer in the word library.");
        }
        if (all.Length == 0) throw Error("no_words", "The word library has no selected entries.");
        return all;
    }

    private SavedWord[] ReadWords()
    {
        var library = new WordLibrary(path);
        library.Load();
        if (library.LoadWarning is not null) throw Error("word_library_unreadable", library.LoadWarning);
        return library.GetAll().ToArray();
    }

    private static async Task StageAsync(string stage, byte[] bytes, CancellationToken token)
    {
        string temporary = stage + $".{Guid.NewGuid():N}";
        bool ownsTemporary = false;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4_096, useAsync: true))
            {
                ownsTemporary = true;
                await stream.WriteAsync(bytes.AsMemory(), token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, stage, overwrite: false);
        }
        finally { if (ownsTemporary) DeleteStagingFile(temporary); }
    }

    private static void DeleteStagingFile(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Digest(IReadOnlyList<SavedWord> words) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(words, ControlProtocol.Json)));

    private static ControlWordsChanged Change(WordLibrary library, string? expectedRevision, Func<WordListStore, int> action,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            return library.Commit(store =>
            {
                token.ThrowIfCancellationRequested();
                RequireRevision(expectedRevision, Digest(store.GetAll()));
                int count = action(store);
                token.ThrowIfCancellationRequested();
                return (new ControlWordsChanged(count > 0, Digest(store.GetAll()), count), count > 0);
            });
        }
        catch (IOException) when (library.LoadWarning is not null)
        { throw Error("word_library_unreadable", library.LoadWarning); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw Error("word_library_io", "The word library could not be saved. Check that it is writable and try again.", retryable: true); }
    }

    private static SavedWord? Find(IReadOnlyList<SavedWord> words, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) throw Error("invalid_arguments", "A nonempty word term is required.");
        string key = term.Trim().ToLowerInvariant();
        return words.FirstOrDefault(word => word.DedupeKey == key);
    }

    private static SavedWord FromInput(ControlWordInput word, string savedAt)
    {
        if (word is null || string.IsNullOrWhiteSpace(word.Term)) throw Error("invalid_arguments", "A nonempty word term is required.");
        return new(word.Term.Trim(), word.Reading, word.Translation, word.Definition, word.Example,
            word.SourceLanguage, word.TargetLanguage, WordSource.WordClick, savedAt);
    }

    private static void RequireRevision(string? expected, string actual)
    {
        if (expected is not null && expected != actual)
            throw Error("revision_conflict", "The word library changed since it was read.", retryable: true);
    }

    private static string Deck(string? deck) => string.IsNullOrWhiteSpace(deck) ? "LLPlayer" : deck.Trim();

    private static ControlException Error(string code, string message, bool retryable = false) => new(new(code, message, retryable));

    private static ControlJobException UnknownEffect() => new("external_effect_unknown",
        "Anki may have added notes before the response was lost. Confirm whether to repeat the submission.", ControlJobStatus.Interrupted);
}
