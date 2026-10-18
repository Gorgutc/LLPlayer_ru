namespace LLPlayer.Core.Services;

/// <summary>Detached click metadata; save never reads the current media language or settings.</summary>
public sealed record WordClickSnapshot(string Term, string Context, int Slot, string SourceLanguage, string TargetLanguage, bool IsTranslated);
