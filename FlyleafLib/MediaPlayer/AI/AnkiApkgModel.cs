using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>One note row destined for the Anki <c>notes</c> table.</summary>
public sealed record ApkgNote(long Id, string Guid, long Mid, long Mod, string Tags, string Flds, string Sfld, long Csum);

/// <summary>One card row destined for the Anki <c>cards</c> table.</summary>
public sealed record ApkgCard(long Id, long Nid, long Did, int Ord, long Mod, long Due);

/// <summary>
/// Everything the SQLite writer needs to materialize a valid <c>collection.anki2</c>: the final <c>col</c>
/// column values (already merged) plus the note/card rows. Pure data — no SQLite, no file IO, no WPF.
/// </summary>
public sealed record ApkgContent(
    long Crt,
    long Mod,
    long Scm,
    int Ver,
    string ConfJson,
    string ModelsJson,
    string DecksJson,
    string DConfJson,
    string TagsJson,
    IReadOnlyList<ApkgNote> Notes,
    IReadOnlyList<ApkgCard> Cards);

/// <summary>
/// Pure builder of Anki <c>.apkg</c> content (the hard, correctness-critical part: stable note GUIDs,
/// field checksums, the model/deck/conf JSON blobs and the schema), kept free of any SQLite/zip/WPF
/// dependency so it is fully unit-testable in FlyleafLibTests. The thin WPF-side writer
/// (<c>AnkiApkgWriter</c>) turns an <see cref="ApkgContent"/> into the zipped package.
/// Schema, default <c>conf</c>/<c>decks</c>/<c>dconf</c> and the GUID/base91 algorithm mirror the
/// battle-tested genanki (MIT) so the output imports cleanly into Anki 2.1.
/// </summary>
public static class AnkiApkgModel
{
    /// <summary>Stable note-type name shared by the .apkg and the AnkiConnect paths.</summary>
    public const string ModelName = "LLPlayer";

    /// <summary>Fixed ids so re-exports update (rather than duplicate) the same model/deck in Anki.</summary>
    public const long ModelId = 1_740_000_000_001L;
    public const long DeckId = 1_740_000_000_002L;

    /// <summary>The five card fields, in order (field 0 = sort field = Term).</summary>
    public static readonly string[] FieldNames = ["Term", "Reading", "Translation", "Definition", "Example"];

    public const string CardFront = "{{Term}}<br>{{Reading}}";
    public const string CardBack = "{{FrontSide}}<hr id=\"answer\">{{Translation}}<br>{{Definition}}<br><i>{{Example}}</i>";
    public const string CardCss = ".card { font-family: arial; font-size: 20px; text-align: center; color: black; background-color: white; }";

    /// <summary>The Anki collection DDL (genanki apkg_schema, verbatim). Used by the SQLite writer.</summary>
    public const string SchemaSql = """
CREATE TABLE col (
    id integer primary key, crt integer not null, mod integer not null, scm integer not null,
    ver integer not null, dty integer not null, usn integer not null, ls integer not null,
    conf text not null, models text not null, decks text not null, dconf text not null, tags text not null
);
CREATE TABLE notes (
    id integer primary key, guid text not null, mid integer not null, mod integer not null,
    usn integer not null, tags text not null, flds text not null, sfld integer not null,
    csum integer not null, flags integer not null, data text not null
);
CREATE TABLE cards (
    id integer primary key, nid integer not null, did integer not null, ord integer not null,
    mod integer not null, usn integer not null, type integer not null, queue integer not null,
    due integer not null, ivl integer not null, factor integer not null, reps integer not null,
    lapses integer not null, left integer not null, odue integer not null, odid integer not null,
    flags integer not null, data text not null
);
CREATE TABLE revlog (
    id integer primary key, cid integer not null, usn integer not null, ease integer not null,
    ivl integer not null, lastIvl integer not null, factor integer not null, time integer not null,
    type integer not null
);
CREATE TABLE graves (usn integer not null, oid integer not null, type integer not null);
CREATE INDEX ix_notes_usn on notes (usn);
CREATE INDEX ix_cards_usn on cards (usn);
CREATE INDEX ix_revlog_usn on revlog (usn);
CREATE INDEX ix_cards_nid on cards (nid);
CREATE INDEX ix_cards_sched on cards (did, queue, due);
CREATE INDEX ix_revlog_cid on revlog (cid);
CREATE INDEX ix_notes_csum on notes (csum);
""";

    private static readonly JsonSerializerOptions Json = new(); // emit nulls (Anki tmpl did:null)

    // Anki base91 alphabet (genanki BASE91_TABLE), exactly 91 chars: a-z A-Z 0-9 then 29 symbols.
    private const string Base91 =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789" +
        "!#$%&()*+,-./:;<=>?@[]^_`{|}~";

    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);

    /// <summary>
    /// Builds the full package content. Deduplicates words by Term (case-insensitive, first-wins), assigns
    /// stable per-Term GUIDs, sequential note/card ids from <paramref name="nowUnixMs"/>, and the merged
    /// col JSON columns. Anki time units (matching genanki/Anki 2.1): <paramref name="nowUnixSec"/> seeds
    /// <c>col.crt</c> and each <c>note.mod</c>/<c>card.mod</c> (epoch SECONDS); <paramref name="nowUnixMs"/>
    /// seeds <c>col.mod</c> and <c>col.scm</c> (epoch MILLISECONDS — Anki stores those two in ms). Both are
    /// caller-supplied for hermetic tests.
    /// </summary>
    public static ApkgContent BuildContent(
        IReadOnlyList<SavedWord> words, string deckName, long nowUnixSec, long nowUnixMs)
    {
        string deck = string.IsNullOrWhiteSpace(deckName) ? "LLPlayer" : deckName.Trim();

        List<ApkgNote> notes = [];
        List<ApkgCard> cards = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        long id = nowUnixMs;
        long due = 1;

        foreach (SavedWord w in words)
        {
            string term = (w.Term ?? "").Trim();
            if (term.Length == 0 || !seen.Add(term.ToLowerInvariant()))
            {
                continue;
            }

            // Anki joins note fields with the Unit Separator (U+001F) — the literal below is that char.
            string flds = string.Join('',
                term, w.Reading ?? "", w.Translation ?? "", w.Definition ?? "", w.Example ?? "");

            long noteId = id++;
            notes.Add(new ApkgNote(noteId, GuidFor(term), ModelId, nowUnixSec, "", flds, term, FieldChecksum(term)));
            cards.Add(new ApkgCard(id++, noteId, DeckId, 0, nowUnixSec, due++));
        }

        return new ApkgContent(
            Crt: nowUnixSec,
            Mod: nowUnixMs,
            Scm: nowUnixMs,
            Ver: 11,
            ConfJson: BuildConfJson(),
            ModelsJson: BuildModelsJson(deck, nowUnixSec),
            DecksJson: BuildDecksJson(deck, nowUnixSec),
            DConfJson: BuildDConfJson(),
            TagsJson: "{}",
            Notes: notes,
            Cards: cards);
    }

    /// <summary>Stable note GUID: base91 of the first 8 bytes of SHA-256(term), matching genanki's guid_for.</summary>
    public static string GuidFor(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        ulong h = 0;
        for (int i = 0; i < 8; i++)
        {
            h = (h << 8) | hash[i];
        }
        if (h == 0)
        {
            return Base91[0].ToString();
        }

        StringBuilder sb = new();
        ulong b = (ulong)Base91.Length;
        while (h > 0)
        {
            sb.Insert(0, Base91[(int)(h % b)]);
            h /= b;
        }
        return sb.ToString();
    }

    /// <summary>Anki field checksum: int of the first 8 hex chars of SHA-1(HTML-stripped sort field).</summary>
    public static long FieldChecksum(string sortField)
    {
        string stripped = HtmlTag.Replace(sortField ?? "", "");
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(stripped));
        string hex = Convert.ToHexString(hash); // upper hex; case-insensitive for parse
        return Convert.ToInt64(hex[..8], 16);
    }

    private static string BuildModelsJson(string deckName, long nowSec)
    {
        object[] flds = FieldNames.Select((name, ord) => (object)new
        {
            name,
            ord,
            font = "Liberation Sans",
            media = Array.Empty<string>(),
            rtl = false,
            size = 20,
            sticky = false
        }).ToArray();

        var model = new Dictionary<string, object?>
        {
            ["css"] = CardCss,
            ["did"] = DeckId,
            ["flds"] = flds,
            ["id"] = ModelId.ToString(),
            ["latexPost"] = "\\end{document}",
            ["latexPre"] = "\\documentclass[12pt]{article}\n\\special{papersize=3in,5in}\n" +
                           "\\usepackage[utf8]{inputenc}\n\\usepackage{amssymb,amsmath}\n\\pagestyle{empty}\n" +
                           "\\setlength{\\parindent}{0in}\n\\begin{document}\n",
            ["latexsvg"] = false,
            ["mod"] = nowSec,
            ["name"] = ModelName,
            // Card 0 generates whenever the sort field (Term) is non-empty.
            ["req"] = new object[] { new object[] { 0, "all", new[] { 0 } } },
            ["sortf"] = 0,
            ["tags"] = Array.Empty<string>(),
            ["tmpls"] = new object[]
            {
                new
                {
                    name = "Card 1",
                    ord = 0,
                    qfmt = CardFront,
                    afmt = CardBack,
                    bqfmt = "",
                    bafmt = "",
                    bfont = "",
                    bsize = 0,
                    did = (long?)null
                }
            },
            ["type"] = 0,
            ["usn"] = -1,
            ["vers"] = Array.Empty<string>()
        };

        return JsonSerializer.Serialize(
            new Dictionary<string, object> { [ModelId.ToString()] = model }, Json);
    }

    private static string BuildDecksJson(string deckName, long nowSec)
    {
        var defaultDeck = new
        {
            collapsed = false, conf = 1, desc = "", dyn = 0, extendNew = 10, extendRev = 50, id = 1,
            lrnToday = new[] { 0, 0 }, mod = nowSec, name = "Default", newToday = new[] { 0, 0 },
            revToday = new[] { 0, 0 }, timeToday = new[] { 0, 0 }, usn = 0
        };
        var ourDeck = new
        {
            collapsed = false, conf = 1, desc = "", dyn = 0, extendNew = 10, extendRev = 50, id = DeckId,
            lrnToday = new[] { 0, 0 }, mod = nowSec, name = deckName, newToday = new[] { 0, 0 },
            revToday = new[] { 0, 0 }, timeToday = new[] { 0, 0 }, usn = -1
        };

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["1"] = defaultDeck,
            [DeckId.ToString()] = ourDeck
        }, Json);
    }

    private static string BuildConfJson() => JsonSerializer.Serialize(new
    {
        activeDecks = new[] { 1 },
        addToCur = true,
        collapseTime = 1200,
        curDeck = 1,
        curModel = ModelId.ToString(),
        dueCounts = true,
        estTimes = true,
        newBury = true,
        newSpread = 0,
        nextPos = 1,
        sortBackwards = false,
        sortType = "noteFld",
        timeLim = 0
    }, Json);

    private static string BuildDConfJson() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["1"] = new
        {
            autoplay = true,
            id = 1,
            lapse = new { delays = new[] { 10 }, leechAction = 0, leechFails = 8, minInt = 1, mult = 0 },
            maxTaken = 60,
            mod = 0,
            name = "Default",
            @new = new { bury = true, delays = new[] { 1, 10 }, initialFactor = 2500, ints = new[] { 1, 4, 7 }, order = 1, perDay = 20, separate = true },
            replayq = true,
            rev = new { bury = true, ease4 = 1.3, fuzz = 0.05, ivlFct = 1, maxIvl = 36500, minSpace = 1, perDay = 100 },
            timer = 0,
            usn = 0
        }
    }, Json);
}
