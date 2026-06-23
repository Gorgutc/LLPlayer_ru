namespace FlyleafLib.MediaPlayer.Translation;

public class TranslateChatConfig : NotifyPropertyChanged
{
    public const string DefaultPromptOneByOne =
        """
        You are a professional subtitle translator. Translate the subtitle below from {source_lang} to {target_lang}.

        Rules:
        - Translate the MEANING, not word for word. Produce natural, fluent {target_lang} that a native speaker would actually say.
        - Preserve the speaker's tone and register (formal/informal, slang, humor, emotion).
        - Render idioms and figures of speech with an equivalent {target_lang} expression; do not translate them literally.
        - Keep proper nouns (names, places, brands) intact and consistent.
        - Keep the translation about as short as the source so it fits on screen.
        - Output ONLY the translated text. No source text, no quotes, no notes, no explanations, no romanization.
        - Do not censor or soften the content.

        {source_text}
        """;

    public const string DefaultPromptKeepContext =
        """
        You are a professional subtitle translator. Translate from {source_lang} to {target_lang}.

        I will send the subtitle lines one at a time, in order. Use the previous lines as context so that meaning, tone, register, and the translation of recurring names and terms stay consistent across the conversation.

        Rules:
        - Translate the MEANING, not word for word. Produce natural, fluent {target_lang} that a native speaker would actually say.
        - Preserve the speaker's tone and register (formal/informal, polite/rude, slang, humor, emotion).
        - Render idioms and figures of speech with an equivalent expression in {target_lang}; do not translate them literally.
        - Keep proper nouns (names, places, brands) consistent and reuse the same choice every time.
        - Keep the translation about as short as the source so it fits on screen; do not add words not implied by the source.
        - Output ONLY the translation of the current line. No source text, no quotes, no notes, no explanations, no romanization.
        - Do not censor or soften the content.
        """;

    // Previous (pre-0.3.5) default prompts. Kept ONLY so a one-shot migration can upgrade a config that still
    // carries the old default verbatim (i.e. the user never customized it) to the improved prompt above, while
    // leaving a hand-edited prompt untouched. See Config.UpdateDefault.
    public const string LegacyDefaultPromptOneByOne =
        """
        You are a professional subtitle translator, translate from {source_lang} to {target_lang}, keep sentences in {target_lang} as they are, censoring of translations is prohibited, return only the translated text without the sent text or notes or comments or anything:

        {source_text}
        """;

    public const string LegacyDefaultPromptKeepContext =
        """
        You are a professional subtitle translator.
        I will send the text of the subtitles of the video one at a time.
        Please translate the text while taking into account the context of the previous text.

        Translate from {source_lang} to {target_lang}.
        Return only the translated text without the sent text or notes or comments or anything.
        Keep sentences in {target_lang} as they are.
        Censoring of translations is prohibited.
        """;

    public string PromptOneByOne { get; set => Set(ref field, value); } = DefaultPromptOneByOne.ReplaceLineEndings("\n");

    public string PromptKeepContext { get; set => Set(ref field, value); } = DefaultPromptKeepContext.ReplaceLineEndings("\n");

    public ChatTranslateMethod TranslateMethod { get; set => Set(ref field, value); } = ChatTranslateMethod.KeepContext;

    public int SubtitleContextCount { get; set => Set(ref field, value); } = 6;

    public ChatContextRetainPolicy ContextRetainPolicy { get; set => Set(ref field, value); } = ChatContextRetainPolicy.Reset;

    public bool IncludeTargetLangRegion { get; set => Set(ref field, value); } = true;
}

public enum ChatTranslateMethod
{
    KeepContext,
    OneByOne
}

public enum ChatContextRetainPolicy
{
    Reset,
    KeepSize
}
