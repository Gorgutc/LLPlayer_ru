using System.Diagnostics;
using System.Linq;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

public sealed class BatchSubtitleTranslator : IBatchSubtitleTranslator
{
    private readonly Config.SubtitlesConfig _config;
    private readonly Func<ITranslateService> _createService;

    public BatchSubtitleTranslator(Config.SubtitlesConfig config)
    {
        _config = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config);
        _createService = () => new TranslateServiceFactory(_config).GetService(_config.TranslateServiceType, false);
    }

    public BatchSubtitleTranslator(Config.SubtitlesConfig config, Func<ITranslateService> createService)
    {
        _config = config;
        _createService = createService;
    }

    public async Task TranslateAsync(IList<SubtitleData> subtitles, Language sourceLanguage, CancellationToken token)
    {
        if (sourceLanguage == Language.Unknown)
            throw new TranslationConfigException("source language is unknown");

        if (sourceLanguage.ISO6391 == TargetLanguage.Russian.ToISO6391())
            return;

        using ITranslateService service = _createService();
        service.Initialize(sourceLanguage, TargetLanguage.Russian);

        List<SubtitleData> translateSubs = subtitles
            .Where(s => !s.IsTranslated && !string.IsNullOrWhiteSpace(s.Text))
            .ToList();

        int concurrency = _config.TranslateMaxConcurrency;
        if (concurrency > 1 &&
            service.ServiceType.IsLLM() &&
            _config.TranslateChatConfig.TranslateMethod == ChatTranslateMethod.KeepContext)
        {
            concurrency = 1;
        }

        if (concurrency <= 1)
        {
            foreach (SubtitleData sub in translateSubs)
            {
                token.ThrowIfCancellationRequested();
                await TranslateSubAsync(service, sub, token);
            }

            return;
        }

        ParallelOptions parallelOptions = new()
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = concurrency
        };

        await Parallel.ForEachAsync(
            translateSubs,
            parallelOptions,
            async (sub, ct) => await TranslateSubAsync(service, sub, ct));
    }

    private static async Task TranslateSubAsync(
        ITranslateService service,
        SubtitleData sub,
        CancellationToken token)
    {
        Debug.Assert(!string.IsNullOrWhiteSpace(sub.Text));

        string text = SubtitleTextUtil.FlattenText(sub.Text!);
        try
        {
            string translated = await service.TranslateAsync(text, token);

            // Parity with interactive SubTranslator: never cache an empty/whitespace reply as a successful
            // translation. Leaving TranslatedText unset keeps IsTranslated false, so the writer falls back to
            // the source line instead of emitting a blank line.
            if (string.IsNullOrWhiteSpace(translated))
            {
                return;
            }

            sub.TranslatedText = translated;
        }
        // A per-line CONTENT failure (a degenerate/looping reply, a truncated reply, or an empty/null reply
        // from a server that DID respond) must not fail the whole file: leave the source text for this single
        // line and keep going — exactly as interactive playback does (SubTranslator.TranslateSubAsync). This
        // also honours the product contract, which says a still-looping reply "falls back to the source text
        // for that line". Network/timeout/HTTP errors (Kind=Generic), config/auth errors
        // (TranslationConfigException) and cancellation (OperationCanceledException) are deliberately NOT caught
        // here, so they still propagate and the batch processor marks the file Failed/Canceled instead of
        // silently writing an all-source output. A positive allow-list is used so any future failure kind
        // defaults to propagate (fail-safe), not to swallow.
        catch (TranslationException ex) when (IsRecoverableContentFailure(ex.Kind))
        {
            // Intentionally swallowed: TranslatedText is left unset so the writer falls back to the source
            // line for this subtitle, and the batch run continues to the next line. (Not logged here to keep
            // the batch core decoupled from the WPF-coupled Logger; the degraded-line trade-off is documented
            // in product-behavior-contract.md.)
        }
    }

    // Per-line content failures that fall back to the source text for that one line instead of failing the
    // whole file. Everything else (Generic network/HTTP errors, config/auth, cancellation) propagates.
    private static bool IsRecoverableContentFailure(TranslationFailureKind kind) =>
        kind is TranslationFailureKind.Degenerate
             or TranslationFailureKind.Truncated
             or TranslationFailureKind.EmptyResponse
             or TranslationFailureKind.NullContent;
}
