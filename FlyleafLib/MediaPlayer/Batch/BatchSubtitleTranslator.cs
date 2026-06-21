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
        sub.TranslatedText = await service.TranslateAsync(text, token);
    }
}
