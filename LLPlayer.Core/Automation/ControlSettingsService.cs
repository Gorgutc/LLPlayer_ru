using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using LLPlayer.Avalonia.Services;
using LLPlayer.ControlApi;
using LLPlayer.Core.Batch;
using LLPlayer.Core.Services;

namespace LLPlayer.Core.Automation;

public sealed record ControlSettingsFamilyRequest(string Family);
public sealed record ControlSettingsPatch(string Family, Dictionary<string, JsonElement> Fields);
public sealed record ControlCredentialSet(string Family, string Name, string Value);
public sealed record ControlCredentialClear(string Family, string Name);
public sealed record ControlCredentialPatch(string Family, Dictionary<string, JsonElement> Fields,
    Dictionary<string, string?> Credentials);
public sealed record ControlCredentialStatus(bool Present, string? Mask);
public sealed record ControlSettingField(JsonElement Schema, bool SavedForNextStart);
public sealed record ControlSettingsSchema(string Family, IReadOnlyDictionary<string, ControlSettingField> Fields,
    IReadOnlyList<string> Credentials);
public sealed record ControlSettingsSnapshot(string Family, long Revision, IReadOnlyDictionary<string, JsonElement> Requested,
    IReadOnlyDictionary<string, JsonElement> Effective, IReadOnlyList<string> Pending,
    IReadOnlyDictionary<string, ControlCredentialStatus> Credentials, string? Warning);

/// <summary>One serialized settings owner for automation and the attached desktop.</summary>
public sealed partial class ControlSettingsService
{
    static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    readonly Func<byte[], byte[]> fingerprint;
    readonly object gate = new();
    readonly Dictionary<string, ISettingsFamily> families = new(StringComparer.Ordinal);
    readonly SettingsFamily<RecognitionSettings> recognition;
    readonly SettingsFamily<TranslationSettings> translation;
    readonly SettingsFamily<BatchSettings> batch;
    readonly SettingsFamily<DubbingSettings> dubbing;

    public ControlSettingsService(string configDir, Func<RecognitionSettings>? recognitionDefaults = null,
        Func<(string Provider, string? Target)>? legacyTranslation = null, Action<BatchSettings>? applyBatch = null,
        Func<byte[], byte[]>? fingerprintSettings = null)
    {
        byte[] localRevisionKey = RandomNumberGenerator.GetBytes(32);
        fingerprint = fingerprintSettings ?? (value => HMACSHA256.HashData(localRevisionKey, value));
        string root = Path.GetFullPath(configDir);
#if !WINDOWS
        prefsStore = new PrefsStore(Path.Combine(root, "LLPlayer.Avalonia.json"));
        app = Add("app", AppFields(), () => (prefsStore.Load(preserveCorruptCopy: false), SafeWarning(prefsStore.LoadWarning)),
            prefsStore.Write, ValidateApp, PrefsStore.JsonOptions);
        legacyTranslation ??= () =>
        {
            var prefs = app.Load().Settings;
            return (prefs.WordTranslationService, prefs.TranslateTargetLanguage);
        };
#endif
        var recognitionStore = new RecognitionSettingsStore(Path.Combine(root, "LLPlayer.Recognition.json"));
        recognition = Add("recognition", RecognitionFields(),
            () => (recognitionStore.Load() ?? recognitionDefaults?.Invoke() ?? new RecognitionSettings(), SafeWarning(recognitionStore.LoadWarning)),
            recognitionStore.Save, value => value.Validate(), RecognitionSettings.JsonOptions);

        var translationStore = new TranslationSettingsStore(Path.Combine(root, "LLPlayer.Translation.json"));
        translation = Add("translation", TranslationFields(), () =>
        {
            var (provider, target) = legacyTranslation?.Invoke() ?? ("GoogleV1", null);
            return (translationStore.Load(provider, target), SafeWarning(translationStore.LoadWarning));
        }, translationStore.Save, value => value.Validate(), TranslationSettings.JsonOptions);

        var batchStore = new BatchSettingsStore(Path.Combine(root, "LLPlayer.Batch.json"));
        batch = Add("batch", BatchFields(), () => (batchStore.Load(), SafeWarning(batchStore.LoadWarning)),
            batchStore.Save, value => value.Validate(), WireJson, applyBatch);

        var dubbingStore = new DubbingSettingsStore(Path.Combine(root, "LLPlayer.Dubbing.json"));
        dubbing = Add("dubbing", DubbingFields(), () => (dubbingStore.Load(), SafeWarning(dubbingStore.LoadWarning)),
            dubbingStore.Save, value => value.Validate(), DubbingSettings.JsonOptions);

        var pdicStore = new PdicSettingsStore(Path.Combine(root, "LLPlayer.Pdic.json"));
        pdic = Add("pdic", PdicFields(), () => (pdicStore.Load(), SafeWarning(pdicStore.LoadWarning)),
            pdicStore.Save, value => value.Validate(), WireJson);
#if !WINDOWS
        var onlineVideoStore = new OnlineVideoSettingsStore(Path.Combine(root, "LLPlayer.OnlineVideo.json"));
        onlineVideo = Add("online_video", OnlineVideoFields(), () => (onlineVideoStore.Load(), SafeWarning(onlineVideoStore.LoadWarning)),
            onlineVideoStore.Save, value => value.Validate(), WireJson);

        playbackStore = new PlaybackSettingsStore(Path.Combine(root, "LLPlayer.Playback.json"));
        playback = Add("playback", PlaybackFields(), () => (playbackStore.Load(), SafeWarning(playbackStore.LoadWarning)),
            value => SavePlayback(value, prefsStore), value => value.Validate(), PlaybackSettingsStore.JsonOptions);
#endif
    }

    public IReadOnlyList<string> Families => families.Keys.Order(StringComparer.Ordinal).ToArray();
    public ControlSettingsSchema Schema(string family) => GetFamily(family).Schema();

    public ControlSettingsSnapshot Read(string family)
    {
        lock (gate) return GetFamily(family).Read();
    }

    /// <summary>Full private settings identity for persisted recovery checks. The host supplies its journal key.</summary>
    public string GetIdentity(params string[] names)
    {
        lock (gate)
            return CombineIdentities(names.Order(StringComparer.Ordinal).Select(name => GetFamily(name).GetIdentity()));
    }

    static string CombineIdentities(IEnumerable<byte[]> identities)
    {
        using var combined = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var identity in identities) combined.AppendData(identity);
        return Convert.ToHexString(combined.GetHashAndReset());
    }

    public ControlSettingsSnapshot Validate(ControlSettingsPatch request, long? expectedRevision = null)
        => Edit(request, expectedRevision, persist: false);

    /// <summary>Reject unknown fields, credentials, and malformed values before a request reaches the durable journal.</summary>
    public void ValidatePatchAdmission(ControlSettingsPatch request) => _ = Validate(request);

    public void ValidateCredentialPatchAdmission(ControlCredentialPatch request) => _ = EditCredentials(request, null, persist: false);

    public ControlSettingsSnapshot ApplyCredentialPatch(ControlCredentialPatch request, long? expectedRevision = null)
        => EditCredentials(request, expectedRevision, persist: true);

    public ControlSettingsSnapshot Apply(ControlSettingsPatch request, long? expectedRevision = null)
        => Edit(request, expectedRevision, persist: true);

    ControlSettingsSnapshot Edit(ControlSettingsPatch request, long? expectedRevision, bool persist)
    {
        if (request.Fields is null || request.Fields.Count == 0)
            throw Error("invalid_settings", "Provide at least one supported field to change.");
        lock (gate)
        {
#if !WINDOWS
            using var lease = persist && request.Family == "app" ? prefsStore.AcquireWriteLease() : null;
#endif
            return GetFamily(request.Family).Edit(request.Fields, expectedRevision, persist);
        }
    }

    ControlSettingsSnapshot EditCredentials(ControlCredentialPatch request, long? expectedRevision, bool persist)
    {
        if (request.Family is not ("translation" or "online_video") || request.Fields is null || request.Credentials is null
            || request.Fields.Count + request.Credentials.Count == 0)
            throw Error("invalid_settings", "Provide a translation or online-video field or credential to change.");
        foreach (string? value in request.Credentials.Values)
            if (value != null) ValidateCredentialValue(value);
        lock (gate) return GetFamily(request.Family).Edit(request.Fields, expectedRevision, persist, request.Credentials);
    }

    public ControlSettingsSnapshot SetCredential(ControlCredentialSet request, long? expectedRevision = null)
    {
        ValidateCredentialValue(request.Value);
        lock (gate) return GetFamily(request.Family).EditCredential(request.Name, request.Value, expectedRevision);
    }

    static void ValidateCredentialValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16384 || value.Any(c => c != '\t' && char.IsControl(c)))
            throw Error("invalid_credential", "Provide a nonempty, single-line credential.");
    }

    public ControlSettingsSnapshot ClearCredential(ControlCredentialClear request, long? expectedRevision = null)
    {
        lock (gate) return GetFamily(request.Family).EditCredential(request.Name, null, expectedRevision);
    }

    public RecognitionSettings CaptureRecognition() { lock (gate) return recognition.Load().Settings; }
    public TranslationSettings CaptureTranslation() { lock (gate) return translation.Load().Settings; }
    public BatchSettings CaptureBatch() { lock (gate) return batch.Load().Settings; }
    public DubbingSettings CaptureDubbing() { lock (gate) return dubbing.Load().Settings; }

    ISettingsFamily GetFamily(string name)
        => name is not null && families.TryGetValue(name, out var family) ? family
            : throw Error("unknown_settings_family", "Choose a settings family from the capability catalog.");

    SettingsFamily<T> Add<T>(string name, SettingsFields<T> fields, Func<(T Settings, string? Warning)> load,
        Action<T> save, Action<T> validate, JsonSerializerOptions persistenceJson, Action<T>? afterSave = null)
    {
        var family = new SettingsFamily<T>(name, fields, load, save, validate, persistenceJson, fingerprint, afterSave);
        families.Add(name, family);
        return family;
    }

    static string? SafeWarning(string? warning) => warning is null ? null :
        "Settings could not be loaded. The original file was preserved and the displayed defaults are in use.";

    static ControlException Error(string code, string message, bool retryable = false) => new(new(code, message, retryable));

    interface ISettingsFamily
    {
        ControlSettingsSchema Schema();
        ControlSettingsSnapshot Read();
        byte[] GetIdentity();
        ControlSettingsSnapshot Edit(Dictionary<string, JsonElement> changes, long? expected, bool persist,
            Dictionary<string, string?>? credentials = null);
        ControlSettingsSnapshot EditCredential(string name, string? value, long? expected);
    }

    sealed class SettingsFamily<T>(string name, SettingsFields<T> fields, Func<(T Settings, string? Warning)> load,
        Action<T> save, Action<T> validate, JsonSerializerOptions persistenceJson, Func<byte[], byte[]> fingerprint,
        Action<T>? afterSave) : ISettingsFamily
    {
        // Config owner DTOs also expose calculated display/engine status fields, which can change during inference.
        readonly JsonSerializerOptions identityJson = new(persistenceJson) { IgnoreReadOnlyProperties = true };
        Dictionary<string, JsonElement>? startup;

        public (T Settings, string? Warning) Load() => load();
        public void SetStartup(T value) => startup = CaptureStartup(value);

        public byte[] GetIdentity() => Identity(Load().Settings);
        public byte[] GetIdentity(T value) => Identity(value);

        public ControlSettingsSchema Schema() => new(name,
            fields.Options.ToDictionary(item => item.Key, item => item.Value.Descriptor), fields.Credentials.Keys.ToArray());

        public ControlSettingsSnapshot Read()
        {
            var (value, warning) = Load();
            return Snapshot(value, value, Revision(value), warning);
        }

        public ControlSettingsSnapshot Edit(Dictionary<string, JsonElement> changes, long? expected, bool persist,
            Dictionary<string, string?>? credentials = null)
        {
            var (original, warning) = Load();
            long revision = ValidateRevision(original, expected);
            T candidate = Clone(original);
            foreach (var (fieldName, value) in changes)
            {
                if (!fields.Options.TryGetValue(fieldName, out var field))
                    throw Error("unknown_settings_field", "Choose a writable field from the settings schema. Use credential operations for secrets.");
                try { (credentials != null ? field.WritePrivate : field.Write)(candidate, value); }
                catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
                { throw Error("invalid_settings", $"The value for '{fieldName}' is invalid."); }
            }
            if (credentials != null)
                foreach (var (credential, value) in credentials)
                    SetCredentialValue(candidate, credential, value);
            ValidateCandidate(candidate);
            if (!persist) return Snapshot(candidate, original, revision, warning);
            return Persist(original, candidate);
        }

        public ControlSettingsSnapshot EditCredential(string credential, string? value, long? expected)
        {
            var (original, _) = Load();
            ValidateRevision(original, expected);
            T candidate = Clone(original);
            SetCredentialValue(candidate, credential, value);
            ValidateCandidate(candidate);
            return Persist(original, candidate);
        }

        void SetCredentialValue(T candidate, string credential, string? value)
        {
            if (credential is null || !fields.Credentials.TryGetValue(credential, out var secret))
                throw Error("unknown_credential", "Choose a credential name from the settings schema.");
            secret.Write(candidate, value);
        }

        ControlSettingsSnapshot Persist(T original, T candidate)
        {
            startup ??= CaptureStartup(original);
            try { save(candidate); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            { throw Error("settings_persistence_failed", "Settings could not be saved. No live change was applied.", retryable: true); }
            try { afterSave?.Invoke(candidate); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
            { throw Error("settings_live_apply_failed", "Settings were saved, but the running settings owner could not apply them."); }
            return Snapshot(candidate, candidate, Revision(candidate), null);
        }

        void ValidateCandidate(T value)
        {
            try { validate(value); }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            { throw Error("invalid_settings", "The candidate settings did not pass the owner's validation."); }
        }

        long ValidateRevision(T value, long? expected)
        {
            long actual = Revision(value);
            if (expected.HasValue && expected.Value != actual)
                throw Error("revision_conflict", "Settings changed. Read the current revision before submitting the edit again.");
            return actual;
        }

        // Keep revision tokens exactly representable in JavaScript clients.
        long Revision(T value) => BinaryPrimitives.ReadInt64BigEndian(Identity(value)) & ((1L << 53) - 1);

        byte[] Identity(T value)
        {
            byte[] settings = JsonSerializer.SerializeToUtf8Bytes(value, identityJson);
            // The Family marker binds the identity to a settings owner even when two owners serialize equal values.
            return fingerprint(JsonSerializer.SerializeToUtf8Bytes(new { family = name, settings }));
        }

        T Clone(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, persistenceJson), persistenceJson)!;

        Dictionary<string, JsonElement> CaptureStartup(T value)
            => fields.Options.Where(pair => pair.Value.Descriptor.SavedForNextStart)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Read(value));

        ControlSettingsSnapshot Snapshot(T requested, T effective, long revision, string? warning)
        {
            startup ??= CaptureStartup(effective);
            var desired = fields.Options.ToDictionary(item => item.Key, item => item.Value.Read(requested));
            var active = fields.Options.ToDictionary(item => item.Key, item => item.Value.Read(effective));
            foreach (var (id, initial) in startup) active[id] = initial;
            var pending = desired.Where(pair => !JsonElement.DeepEquals(pair.Value, active[pair.Key]))
                .Select(pair => pair.Key).ToArray();
            var credentials = fields.Credentials.ToDictionary(item => item.Key, item =>
            {
                bool present = !string.IsNullOrEmpty(item.Value.Read(requested));
                return new ControlCredentialStatus(present, present ? "********" : null);
            });
            return new(name, revision, desired, active, pending, credentials, warning);
        }
    }

    sealed record Field<T>(ControlSettingField Descriptor, Func<T, JsonElement> Read, Action<T, JsonElement> Write,
        Func<T, JsonElement> ReadPrivate, Action<T, JsonElement> WritePrivate);
    sealed record Credential<T>(Func<T, string?> Read, Action<T, string?> Write);

    sealed class SettingsFields<T>
    {
        public Dictionary<string, Field<T>> Options { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Credential<T>> Credentials { get; } = new(StringComparer.Ordinal);

        public void Add<TValue>(string name, Func<T, TValue> read, Action<T, TValue> write,
            bool allowsNull = false, bool savedForNextStart = false,
            Func<T, TValue>? readPrivate = null, Action<T, TValue>? writePrivate = null)
        {
            var schema = WireJson.GetJsonSchemaAsNode(typeof(TValue), new() { TreatNullObliviousAsNonNullable = !allowsNull });
            TValue Parse(JsonElement json)
            {
                if (json.ValueKind == JsonValueKind.Null && !allowsNull) throw new JsonException();
                return json.Deserialize<TValue>(WireJson)!;
            }
            Options.Add(name, new(new(JsonSerializer.SerializeToElement(schema), savedForNextStart),
                source => JsonSerializer.SerializeToElement(read(source), WireJson), (target, json) => write(target, Parse(json)),
                source => JsonSerializer.SerializeToElement((readPrivate ?? read)(source), WireJson),
                (target, json) => (writePrivate ?? write)(target, Parse(json))));
        }

        public void AddCredential(string name, Func<T, string?> read, Action<T, string?> write)
            => Credentials.Add(name, new(read, write));
    }
}
