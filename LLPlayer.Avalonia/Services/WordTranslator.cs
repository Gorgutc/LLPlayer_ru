using System.Collections.Concurrent;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace LLPlayer.Avalonia.Services;

public enum WordTranslationStatus
{
    Translated,
    /// <summary>No usable translator: service off, unknown / same source language, or a configuration error.</summary>
    NotConfigured,
    Failed,
}

public sealed record WordTranslationResult(WordTranslationStatus Status, string? Text, string? Detail = null);

/// <summary>Translates a clicked subtitle word (word popup).</summary>
public interface IWordTranslator
{
    Task<WordTranslationResult> TranslateAsync(string word, int slot, CancellationToken token);
}

/// <summary>
/// Word translation through FlyleafLib's translation stack (<see cref="TranslateServiceFactory"/>, the word service
/// of <c>Config.Subtitles</c>), mirroring the WPF WordPopup: the source language is the slot's subtitle language, the
/// target is <c>Config.Subtitles.TranslateTargetLanguage</c>, results are cached per lower-cased word and the service is
/// created lazily once. "Off" (app preference) or an unconfigurable service reports <see cref="WordTranslationStatus.NotConfigured"/>.
/// </summary>
public sealed class FlyleafWordTranslator(Player player, Func<string> serviceName) : IWordTranslator, IDisposable
{
    readonly ConcurrentDictionary<string, string> cache = new(StringComparer.Ordinal);
    readonly SemaphoreSlim gate = new(1, 1);
    ITranslateService? service;
    string? serviceKey;

    public async Task<WordTranslationResult> TranslateAsync(string word, int slot, CancellationToken token)
    {
        string name = serviceName();
        if (string.Equals(name, "Off", StringComparison.OrdinalIgnoreCase) || !Enum.TryParse(name, true, out TranslateServiceType type))
            return new(WordTranslationStatus.NotConfigured, null, "Word translation is off (WordTranslationService in the preferences file).");

        Language? src = player.SubtitlesManager[slot].Language;
        TargetLanguage target = player.Config.Subtitles.TranslateTargetLanguage;
        if (src == null || src == Language.Unknown)
            return new(WordTranslationStatus.NotConfigured, null, "The subtitle language is unknown.");
        if (src.ISO6391 == target.ToISO6391())
            return new(WordTranslationStatus.NotConfigured, null, $"The subtitles are already in the target language ({target}).");

        string key = $"{type}|{src.ISO6391}|{target}|{word.ToLowerInvariant()}";
        if (cache.TryGetValue(key, out string? cached))
            return new(WordTranslationStatus.Translated, cached);

        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string wantedKey = $"{type}|{src.ISO6391}|{target}";
            if (service == null || serviceKey != wantedKey)
            {
                service?.Dispose();
                service = null;
                ITranslateService created = new TranslateServiceFactory(player.Config.Subtitles).GetService(type, wordMode: true);
                try
                {
                    created.Initialize(src, target);
                }
                catch
                {
                    created.Dispose();
                    throw;
                }
                service = created;
                serviceKey = wantedKey;
            }

            string result = await service.TranslateAsync(word, token).ConfigureAwait(false);
            cache[key] = result;
            return new(WordTranslationStatus.Translated, result);
        }
        catch (TranslationConfigException ex)
        {
            return new(WordTranslationStatus.NotConfigured, null, ex.Message);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(WordTranslationStatus.Failed, null, ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        service?.Dispose();
        service = null;
        gate.Dispose();
    }
}
