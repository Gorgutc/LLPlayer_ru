namespace LLPlayer.Core.Services;

/// <summary>Detached, safely captured transcript and provenance for one explicit AI request.</summary>
public sealed record InsightsTranscriptSnapshot(
    long MediaGeneration, string MediaCaption, DateTimeOffset CapturedAtUtc,
    int Slot, bool UseTranslated, string? SourceLanguageName, string SourceLanguageCode,
    string TargetLanguageName, string TargetLanguageCode, IReadOnlyList<string?> CueTexts,
    bool IsGenerating = false);
