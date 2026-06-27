using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FlyleafLib.MediaPlayer.AI;
using Microsoft.Data.Sqlite;

namespace LLPlayer.Services;

/// <summary>
/// Materializes an Anki <c>.apkg</c> (a zip of <c>collection.anki2</c> SQLite + an empty <c>media</c>) from a
/// word list. The correctness-critical content (schema, GUIDs, checksums, model/deck JSON) is computed by the
/// pure, unit-tested <see cref="AnkiApkgModel"/>; this writer only performs the SQLite + zip plumbing, which
/// needs the native Microsoft.Data.Sqlite dependency and so lives in the WPF layer. Fail-soft: any failure
/// (e.g. missing native e_sqlite3.dll) returns <c>null</c> and the caller falls back to TSV / AnkiConnect.
/// </summary>
public static class AnkiApkgWriter
{
    public static byte[]? TryBuild(IReadOnlyList<SavedWord> words, string deckName)
    {
        try
        {
            return Build(words, deckName);
        }
        catch
        {
            // Native lib missing / disk error / SQLite failure → fail-soft; caller shows a known-error popup.
            return null;
        }
    }

    private static byte[] Build(IReadOnlyList<SavedWord> words, string deckName)
    {
        long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ApkgContent content = AnkiApkgModel.BuildContent(words, deckName, nowSec, nowMs);

        string dbPath = Path.Combine(Path.GetTempPath(), $"llplayer-apkg-{Guid.NewGuid():N}.anki2");
        try
        {
            WriteDatabase(dbPath, content);

            using MemoryStream ms = new();
            using (ZipArchive zip = new(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                zip.CreateEntryFromFile(dbPath, "collection.anki2");

                ZipArchiveEntry media = zip.CreateEntry("media");
                using StreamWriter sw = new(media.Open(), new UTF8Encoding(false));
                sw.Write("{}");
            }
            return ms.ToArray();
        }
        finally
        {
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* best-effort temp cleanup */ }
        }
    }

    private static void WriteDatabase(string dbPath, ApkgContent c)
    {
        // Pooling=False so the file handle is released immediately on Dispose (we then read/delete the file).
        using SqliteConnection conn = new($"Data Source={dbPath};Pooling=False");
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();

        Exec(conn, tx, AnkiApkgModel.SchemaSql);

        using (SqliteCommand col = conn.CreateCommand())
        {
            col.Transaction = tx;
            col.CommandText =
                "INSERT INTO col (id,crt,mod,scm,ver,dty,usn,ls,conf,models,decks,dconf,tags) " +
                "VALUES (1,$crt,$mod,$scm,$ver,0,0,0,$conf,$models,$decks,$dconf,$tags);";
            Add(col, "$crt", c.Crt);
            Add(col, "$mod", c.Mod);
            Add(col, "$scm", c.Scm);
            Add(col, "$ver", c.Ver);
            Add(col, "$conf", c.ConfJson);
            Add(col, "$models", c.ModelsJson);
            Add(col, "$decks", c.DecksJson);
            Add(col, "$dconf", c.DConfJson);
            Add(col, "$tags", c.TagsJson);
            col.ExecuteNonQuery();
        }

        using (SqliteCommand note = conn.CreateCommand())
        {
            note.Transaction = tx;
            note.CommandText =
                "INSERT INTO notes (id,guid,mid,mod,usn,tags,flds,sfld,csum,flags,data) " +
                "VALUES ($id,$guid,$mid,$mod,-1,'',$flds,$sfld,$csum,0,'');";
            SqliteParameter id = note.Parameters.Add("$id", SqliteType.Integer);
            SqliteParameter guid = note.Parameters.Add("$guid", SqliteType.Text);
            SqliteParameter mid = note.Parameters.Add("$mid", SqliteType.Integer);
            SqliteParameter mod = note.Parameters.Add("$mod", SqliteType.Integer);
            SqliteParameter flds = note.Parameters.Add("$flds", SqliteType.Text);
            SqliteParameter sfld = note.Parameters.Add("$sfld", SqliteType.Text);
            SqliteParameter csum = note.Parameters.Add("$csum", SqliteType.Integer);

            foreach (ApkgNote n in c.Notes)
            {
                id.Value = n.Id;
                guid.Value = n.Guid;
                mid.Value = n.Mid;
                mod.Value = n.Mod;
                flds.Value = n.Flds;
                sfld.Value = n.Sfld;
                csum.Value = n.Csum;
                note.ExecuteNonQuery();
            }
        }

        using (SqliteCommand card = conn.CreateCommand())
        {
            card.Transaction = tx;
            card.CommandText =
                "INSERT INTO cards (id,nid,did,ord,mod,usn,type,queue,due,ivl,factor,reps,lapses,left,odue,odid,flags,data) " +
                "VALUES ($id,$nid,$did,$ord,$mod,-1,0,0,$due,0,0,0,0,0,0,0,0,'');";
            SqliteParameter id = card.Parameters.Add("$id", SqliteType.Integer);
            SqliteParameter nid = card.Parameters.Add("$nid", SqliteType.Integer);
            SqliteParameter did = card.Parameters.Add("$did", SqliteType.Integer);
            SqliteParameter ord = card.Parameters.Add("$ord", SqliteType.Integer);
            SqliteParameter mod = card.Parameters.Add("$mod", SqliteType.Integer);
            SqliteParameter due = card.Parameters.Add("$due", SqliteType.Integer);

            foreach (ApkgCard cd in c.Cards)
            {
                id.Value = cd.Id;
                nid.Value = cd.Nid;
                did.Value = cd.Did;
                ord.Value = cd.Ord;
                mod.Value = cd.Mod;
                due.Value = cd.Due;
                card.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand cmd, string name, object value)
    {
        SqliteParameter p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
