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

    // Context-window translation prompt (the 0.3.6 default). Each subtitle line is translated on its own (so the
    // model returns exactly one line — robust 1-in/1-out), but the surrounding lines are supplied as read-only
    // context. This is what fixes literal, context-blind output (e.g. an infinitive where the running sentence
    // needs an inflected form): the model can see how the sentence continues across cues and choose the correct
    // grammatical form. The window text itself is built in code (see OpenAIBaseTranslateService.BuildContextWindowUserMessage).
    public const string DefaultPromptContextWindow =
        """
        You are a professional subtitle translator. You translate ONE subtitle line at a time, but you are given the surrounding lines as context.

        Each message has three sections:
        - "Context before": the lines that come BEFORE the line to translate.
        - "Line to translate": the ONE line you must translate.
        - "Context after": the lines that come AFTER it.

        Translate ONLY the "Line to translate" from {source_lang} to {target_lang}.

        Rules:
        - Use "Context before" and "Context after" ONLY as context. Do NOT translate or output them.
        - A single sentence is often split across several lines. Read the context so the line you output continues that sentence naturally — use the grammatical form the context requires (correct verb form/aspect, tense, case, gender, number, agreement). Do NOT fall back to dictionary or base forms.
        - Translate the MEANING, not word for word. Produce natural, fluent {target_lang} that a native speaker would actually say.
        - Preserve the speaker's tone and register (formal/informal, polite/rude, slang, humor, emotion).
        - Render idioms and figures of speech with an equivalent {target_lang} expression; do not translate them literally.
        - Keep proper nouns (names, places, brands) and recurring terms consistent with the context.
        - Keep the translation about as short as the source line so it fits on screen; do not add words not implied by the source.
        - Output ONLY the {target_lang} translation of the "Line to translate" — no labels, no section names, no source text, no quotes, no notes, no romanization.
        - Do not censor or soften the content.
        """;

    // Optional second-pass proof-reader prompt (0.3.6). Runs after the first translation when GrammarCheck is on:
    // a fresh request that fixes target-language grammar/agreement/fluency without changing meaning. Kept separate
    // from the translation prompts so it can be tuned independently; the source line is supplied for reference.
    public const string DefaultPromptGrammarCheck =
        """
        You are a meticulous {target_lang} proof-reader for video subtitles. You are given a machine translation of one subtitle line (in {target_lang}) together with its {source_lang} source. Return a corrected {target_lang} line.

        Rules:
        - Fix grammar: agreement of case, gender, number, and verb form/aspect/tense.
        - Fix awkward word-for-word phrasing and word order; make it read like a native {target_lang} speaker.
        - Fix obvious spelling and typographical errors.
        - Preserve the original meaning and keep about the same length so it still fits on screen.
        - Do NOT add, remove, explain, or comment on anything.
        - Output ONLY the corrected {target_lang} line — no source text, no quotes, no notes, no romanization. If the line is already correct, output it unchanged.
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

    public string PromptContextWindow { get; set => Set(ref field, value); } = DefaultPromptContextWindow.ReplaceLineEndings("\n");

    public string PromptGrammarCheck { get; set => Set(ref field, value); } = DefaultPromptGrammarCheck.ReplaceLineEndings("\n");

    // Default since 0.3.6: ContextWindow (surrounding-line context, one line of output). Existing configs that
    // still carry the prior default (KeepContext) are migrated once in Config.UpdateDefault; an explicit OneByOne
    // choice is preserved.
    public ChatTranslateMethod TranslateMethod { get; set => Set(ref field, value); } = ChatTranslateMethod.ContextWindow;

    public int SubtitleContextCount { get; set => Set(ref field, value); } = 6;

    public ChatContextRetainPolicy ContextRetainPolicy { get; set => Set(ref field, value); } = ChatContextRetainPolicy.Reset;

    // Number of surrounding subtitle lines supplied as read-only context in ContextWindow mode. Before/after are
    // separate so the window can be asymmetric; 0 disables that side. Additive/absent-defaulting.
    public int ContextWindowBefore { get; set => Set(ref field, value); } = 6;

    public int ContextWindowAfter { get; set => Set(ref field, value); } = 6;

    // Optional second-pass grammar/fluency proof-read of the translated line (LLM ContextWindow mode only).
    // Default ON: a dedicated request corrects target-language grammar/agreement after the first translation.
    // If the correction fails or comes back empty/degenerate, the first-pass translation is kept (never lost).
    public bool GrammarCheckEnabled { get; set => Set(ref field, value); } = true;

    public bool IncludeTargetLangRegion { get; set => Set(ref field, value); } = true;
}

public enum ChatTranslateMethod
{
    ContextWindow,
    KeepContext,
    OneByOne
}

public enum ChatContextRetainPolicy
{
    Reset,
    KeepSize
}
