namespace FlyleafLib.MediaPlayer;

#nullable enable

public readonly record struct SrtSpeechEndEvidence(int CueIndex, TimeSpan Start, TimeSpan OnsetPrefixEnd, TimeSpan End);
