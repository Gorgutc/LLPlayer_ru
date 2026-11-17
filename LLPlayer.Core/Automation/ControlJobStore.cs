using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LLPlayer.ControlApi;
using Microsoft.Data.Sqlite;

namespace LLPlayer.Core.Automation;

internal sealed class ControlJobStore
{
    private const int SchemaVersion = 1;
    private const string Columns = "ordinal, state_json, request_json, fingerprint, secret, journal_json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string connectionString;

    public ControlJobStore(string directory)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
        {
            const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(directory, ownerOnly);
            if ((File.GetUnixFileMode(directory) & ~ownerOnly) != 0)
                throw new UnauthorizedAccessException("The automation job journal directory must be private.");
        }

        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "jobs.sqlite"),
            Pooling = false
        }.ToString();
        using var connection = Open();
        using (var wal = Command(connection, "PRAGMA journal_mode = WAL"))
            _ = wal.ExecuteScalar();
        using var transaction = connection.BeginTransaction();
        using var version = Command(connection, "PRAGMA user_version", transaction);
        switch (Convert.ToInt32(version.ExecuteScalar()))
        {
            case 0:
                using (var schema = Command(connection, """
                    CREATE TABLE job (
                        ordinal INTEGER PRIMARY KEY AUTOINCREMENT,
                        profile_id TEXT NOT NULL,
                        request_id TEXT NOT NULL,
                        job_id TEXT NOT NULL UNIQUE,
                        session_id TEXT,
                        status TEXT NOT NULL,
                        revision INTEGER NOT NULL,
                        state_json TEXT NOT NULL,
                        request_json TEXT NOT NULL,
                        fingerprint BLOB NOT NULL,
                        secret INTEGER NOT NULL CHECK(secret IN (0, 1)),
                        journal_json TEXT NOT NULL,
                        UNIQUE(profile_id, request_id)
                    );
                    CREATE INDEX job_by_scope ON job(profile_id, session_id, ordinal DESC);
                    CREATE TABLE job_event (
                        sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                        profile_id TEXT NOT NULL,
                        session_id TEXT,
                        state_json TEXT NOT NULL
                    );
                    CREATE INDEX events_by_scope ON job_event(profile_id, sequence);
                    CREATE TABLE metadata (name TEXT PRIMARY KEY, value BLOB NOT NULL);
                    INSERT INTO metadata(name, value) VALUES('fingerprint-key', $key);
                    PRAGMA user_version = 1;
                    """, transaction, ("$key", RandomNumberGenerator.GetBytes(32))))
                    schema.ExecuteNonQuery();
                break;
            case SchemaVersion:
                break;
            default:
                throw new InvalidDataException("The automation job journal has an unsupported schema version.");
        }
        using var key = Command(connection, "SELECT value FROM metadata WHERE name = 'fingerprint-key'", transaction);
        FingerprintKey = key.ExecuteScalar() as byte[] is { Length: 32 } value
            ? value : throw new InvalidDataException("The automation job journal has no fingerprint key.");
        transaction.Commit();
    }

    internal byte[] FingerprintKey { get; }

    internal StoredControlJob Admit(ControlJob state, ControlRequest persistedRequest, byte[] fingerprint, bool secret,
        JsonElement? checkpoint = null)
    {
        var job = new StoredControlJob(0, state, persistedRequest, fingerprint, secret, new() { Checkpoint = checkpoint });
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = Command(connection, """
            INSERT INTO job (profile_id, request_id, job_id, session_id, status, revision,
                state_json, request_json, fingerprint, secret, journal_json)
            VALUES ($profile, $request, $job, $session, $status, $revision,
                $state, $requestJson, $fingerprint, $secret, $journal)
            RETURNING ordinal;
            """, transaction,
            ("$profile", state.Scope.ProfileId), ("$request", persistedRequest.RequestId.ToString("D")),
            ("$job", state.JobId.ToString("D")), ("$session", state.Scope.SessionId?.ToString("D")),
            ("$status", state.Status.ToString()), ("$revision", state.Revision),
            ("$state", Write(state)), ("$requestJson", Write(persistedRequest)),
            ("$fingerprint", fingerprint), ("$secret", secret), ("$journal", Write(job.Journal)));
        long ordinal = (long)(command.ExecuteScalar() ?? throw new InvalidDataException("Missing job ordinal."));
        AppendEvent(connection, transaction, state);
        transaction.Commit();
        return job with { Ordinal = ordinal };
    }

    internal StoredControlJob Save(StoredControlJob oldJob, ControlJob state, ControlJobJournal? journal = null)
    {
        state = state with { Revision = checked(oldJob.State.Revision + 1), UpdatedAt = DateTimeOffset.UtcNow };
        journal ??= oldJob.Journal;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = Command(connection, """
            UPDATE job SET state_json = $state, journal_json = $journal, status = $status, revision = $revision
            WHERE ordinal = $id AND revision = $previous;
            """, transaction, ("$state", Write(state)), ("$journal", Write(journal)), ("$status", state.Status.ToString()),
            ("$revision", state.Revision), ("$id", oldJob.Ordinal), ("$previous", oldJob.State.Revision));
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Concurrent automation journal update.");
        AppendEvent(connection, transaction, state);
        transaction.Commit();
        return oldJob with { State = state, Journal = journal };
    }

    internal StoredControlJob? Get(ControlScope scope, Guid id) => ReadSingle(
        "profile_id = $profile AND job_id = $id AND ($session IS NULL OR session_id = $session)",
        ("$profile", scope.ProfileId), ("$id", id.ToString("D")), ("$session", scope.SessionId?.ToString("D")));

    internal StoredControlJob? Get(Guid id) => ReadSingle("job_id = $id", ("$id", id.ToString("D")));

    internal StoredControlJob? GetByRequest(string profileId, Guid id) => ReadSingle(
        "profile_id = $profile AND request_id = $id", ("$profile", profileId), ("$id", id.ToString("D")));

    internal List<StoredControlJob> List(ControlScope scope, long before, int limit) => ReadList(
        "profile_id = $profile AND ($session IS NULL OR session_id = $session) AND ordinal < $before",
        limit, ("$profile", scope.ProfileId), ("$session", scope.SessionId?.ToString("D")), ("$before", before));

    internal List<StoredControlJob> Unfinished() => ReadList(
        "status NOT IN ('Completed', 'Cancelled', 'Failed')", -1);

    internal ControlJobChange[] Changes(ControlScope scope, long after, int limit)
    {
        using var connection = Open();
        using var command = Command(connection, """
            SELECT sequence, state_json FROM job_event
            WHERE profile_id = $profile AND ($session IS NULL OR session_id = $session) AND sequence > $after
            ORDER BY sequence LIMIT $limit;
            """, null, ("$profile", scope.ProfileId), ("$session", scope.SessionId?.ToString("D")),
            ("$after", after), ("$limit", limit));
        using var reader = command.ExecuteReader();
        List<ControlJobChange> changes = [];
        while (reader.Read())
            changes.Add(new(reader.GetInt64(0), Read<ControlJob>(reader.GetString(1))));
        return changes.ToArray();
    }

    private static void AppendEvent(SqliteConnection connection, SqliteTransaction transaction, ControlJob state)
    {
        using var command = Command(connection, """
            INSERT INTO job_event (profile_id, session_id, state_json) VALUES ($profile, $session, $state);
            """, transaction, ("$profile", state.Scope.ProfileId), ("$session", state.Scope.SessionId?.ToString("D")),
            ("$state", Write(state)));
        command.ExecuteNonQuery();
    }

    public ControlJob[] ListActive(ControlScope scope)
    {
        using var connection = Open();
        using var command = Command(connection, """
            SELECT state_json FROM job
            WHERE profile_id = $profile AND ($session IS NULL OR session_id = $session)
              AND status NOT IN ('Completed', 'Failed', 'Cancelled', 'Interrupted')
            ORDER BY ordinal DESC;
            """, null, ("$profile", scope.ProfileId), ("$session", scope.SessionId?.ToString("D")));
        using var reader = command.ExecuteReader();
        List<ControlJob> result = [];
        while (reader.Read())
            result.Add(Read<ControlJob>(reader.GetString(0)));
        return result.ToArray();
    }

    private StoredControlJob? ReadSingle(string where, params (string, object?)[] args) =>
        ReadList(where, 1, args).SingleOrDefault();

    private List<StoredControlJob> ReadList(string where, int limit, params (string, object?)[] args)
    {
        using var connection = Open();
        using var command = Command(connection, $"SELECT {Columns} FROM job WHERE {where} ORDER BY ordinal DESC LIMIT $limit",
            null, args);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        List<StoredControlJob> jobs = [];
        while (reader.Read())
            jobs.Add(new(reader.GetInt64(0), Read<ControlJob>(reader.GetString(1)),
                Read<ControlRequest>(reader.GetString(2)), reader.GetFieldValue<byte[]>(3),
                reader.GetBoolean(4), Read<ControlJobJournal>(reader.GetString(5))));
        return jobs;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            using var command = Command(connection, "PRAGMA synchronous = FULL");
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] args)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in args)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static T Read<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, Json) ?? throw new InvalidDataException("Invalid control job journal row.");
}

internal sealed record StoredControlJob(long Ordinal, ControlJob State, ControlRequest Request, byte[] Fingerprint,
    bool Secret, ControlJobJournal Journal);

internal sealed record ControlJobJournal
{
    public int Attempt { get; init; } = 1;
    public bool DiscardOnRestart { get; init; }
    public JsonElement? Checkpoint { get; init; }
    public Dictionary<string, string> Identities { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, ControlJobEffect> Effects { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, FlyleafLib.MediaPlayer.EngineProcessIdentity> EngineProcesses { get; init; } = [];
}

internal sealed record ControlJobEffect(string Kind, bool Completed, string? Path = null, string? Hash = null,
    string? PreviousHash = null, bool Overwrite = false, JsonElement? Receipt = null, string? StagedPath = null);
