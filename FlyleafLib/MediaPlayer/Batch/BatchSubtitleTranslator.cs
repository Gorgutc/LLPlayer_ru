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

        // Indices (into the full list) of the lines that still need translation. Indices — not a filtered
        // sublist — so the ContextWindow neighbours can be read from the full, in-order list (including lines
        // that were already translated or skipped).
        List<int> targetIndices = new();
        for (int i = 0; i < subtitles.Count; i++)
        {
            SubtitleData s = subtitles[i];
            if (!s.IsTranslated && !string.IsNullOrWhiteSpace(s.Text))
            {
                targetIndices.Add(i);
            }
        }

        // When re-segmentation is on, keep the translated text within the same at-most-2-line shape as the
        // (already re-segmented) source cue. Null = leave the translation as the model returned it.
        SubtitleSegmentOptions? wrapOpt = _config.ResegmentSubtitles ? _config.SubtitleSegmentOptions : null;

        bool useWindow = service.ServiceType.IsLLM() &&
                         _config.TranslateChatConfig.TranslateMethod == ChatTranslateMethod.ContextWindow;

        int concurrency = _config.TranslateMaxConcurrency;
        if (concurrency > 1 &&
            service.ServiceType.IsLLM() &&
            _config.TranslateChatConfig.TranslateMethod
                is ChatTranslateMethod.KeepContext or ChatTranslateMethod.ContextWindow)
        {
            // KeepContext must be sequential to preserve chat-history order; ContextWindow is kept sequential so
            // a single local LLM is not hit with concurrent requests (the optional grammar pass already doubles
            // the per-line request count).
            concurrency = 1;
        }

        if (concurrency <= 1)
        {
            foreach (int i in targetIndices)
            {
                token.ThrowIfCancellationRequested();
                await TranslateSubAsync(service, subtitles, i, _config, useWindow, wrapOpt, token);
            }

            return;
        }

        ParallelOptions parallelOptions = new()
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = concurrency
        };

        await Parallel.ForEachAsync(
            targetIndices,
            parallelOptions,
            async (i, ct) => await TranslateSubAsync(service, subtitles, i, _config, useWindow, wrapOpt, ct));
    }

    private static async Task TranslateSubAsync(
        ITranslateService service,
        IList<SubtitleData> subtitles,
        int index,
        Config.SubtitlesConfig config,
        bool useWindow,
        SubtitleSegmentOptions? wrapOpt,
        CancellationToken token)
    {
        SubtitleData sub = subtitles[index];
        Debug.Assert(!string.IsNullOrWhiteSpace(sub.Text));

        string text = SubtitleTextUtil.FlattenText(sub.Text!);
        SubtitleTranslationContext context = useWindow
            ? BuildContext(subtitles, index, text, config.TranslateChatConfig)
            : new SubtitleTranslationContext { Text = text };

        try
        {
            string translated = await service.TranslateAsync(context, token);

            // Parity with interactive SubTranslator: never cache an empty/whitespace reply as a successful
            // translation. Leaving TranslatedText unset keeps IsTranslated false, so the writer falls back to
            // the source line instead of emitting a blank line.
            if (string.IsNullOrWhiteSpace(translated))
            {
                return;
            }

            sub.TranslatedText = wrapOpt != null
                ? SubtitleSegmenter.WrapTwoLines(translated, wrapOpt)
                : translated;
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

    // Builds the read-only ContextWindow window (surrounding source lines) for the focal subtitle at `index`,
    // drawn from the full in-order list. Mirrors the interactive path (SubTranslator.BuildTranslationContext).
    private static SubtitleTranslationContext BuildContext(
        IList<SubtitleData> subtitles, int index, string focalText, TranslateChatConfig chat)
    {
        int before = Math.Max(0, chat.ContextWindowBefore);
        int after = Math.Max(0, chat.ContextWindowAfter);

        return new SubtitleTranslationContext
        {
            Text = focalText,
            Before = Collect(subtitles, index - before, index - 1),
            After = Collect(subtitles, index + 1, index + after),
        };

        static List<string> Collect(IList<SubtitleData> subs, int from, int to)
        {
            List<string> list = new();
            from = Math.Max(0, from);
            to = Math.Min(subs.Count - 1, to);
            for (int i = from; i <= to; i++)
            {
                string? t = subs[i].Text;
                if (string.IsNullOrWhiteSpace(t))
                    continue;
                list.Add(SubtitleTextUtil.FlattenText(t));
            }
            return list;
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
