# LLPlayer_ru — Task Backlog (рабочий бэклог)

> **Назначение:** единый, максимально подробный список незакрытых задач для работы в будущих сессиях.
> Каждая задача имеет стабильный ID (`B-`/`F-`/`T-`), описание, файлы, ссылки, важность, сложность, статус
> и мои рассуждения. В конце — два ранжирования: **по важности** и **по сложности**.
>
> Создан 2026-06-25 (сессия-анализ). Жив (living) — обновлять по мере закрытия задач.
> Дополняет, а не заменяет: `docs/agent/*-contract.md` (frozen-контракты), второй мозг
> `Improvements.md` + `Sessions/2026-06-25-handoff-competitive-analysis-roadmap.md`, авто-память.
> Перед изменением ПОВЕДЕНИЯ — сверяться с frozen-контрактами (не трогать без явного запроса владельца).

## 0. Как пользоваться этим файлом / ссылки на репозитории

**Наш форк:** <https://github.com/Gorgutc/LLPlayer_ru>
**Upstream (источник кода плеера):** <https://github.com/umlx5h/LLPlayer> ·
[Wiki](https://github.com/umlx5h/LLPlayer/wiki) · [Issues](https://github.com/umlx5h/LLPlayer/issues) ·
[Translation-Engine wiki](https://github.com/umlx5h/LLPlayer/wiki/Translation-Engine) ·
[Whisper-Engine wiki](https://github.com/umlx5h/LLPlayer/wiki/Whisper-Engine) ·
issues: [#12 export SRT (done)](https://github.com/umlx5h/LLPlayer/issues/12),
[#13 Yomitan/10ten in-player](https://github.com/umlx5h/LLPlayer/issues/13)

**Конкуренты (для заимствования идей/кода):**
- SubtitleEdit (редактор, **GPL-3.0 = наша лицензия → код заимствуем легально**):
  <https://github.com/SubtitleEdit/subtitleedit> · сайт <https://www.nikse.dk/subtitleedit/> ·
  [Speech-to-Text](https://subtitleedit.github.io/subtitleedit/features/speech-to-text.html) ·
  [What's new in SE5](https://subtitleedit.github.io/subtitleedit/features/whats-new-in-se5.html)
- Buzz (транскрайбер, MIT): <https://github.com/chidiwilliams/buzz>
- decipher (Whisper-CLI, MIT): <https://github.com/dsymbol/decipher>

**Движки/библиотеки (для фич-задач):**
- FlyleafLib (ядро плеера): <https://github.com/SuRGeoNix/Flyleaf>
- whisper.cpp <https://github.com/ggerganov/whisper.cpp> · Whisper.net
  <https://github.com/sandrohanea/whisper.net> · faster-whisper
  <https://github.com/SYSTRAN/faster-whisper> · Purfview standalone
  <https://github.com/Purfview/whisper-standalone-win>
- Диаризация: pyannote-audio <https://github.com/pyannote/pyannote-audio>
- Speech separation / денойз: Demucs <https://github.com/adefossez/demucs> (vocal isolation)
- Дубляж TTS: CosyVoice <https://github.com/FunAudioLLM/CosyVoice> (уже используется в `dub_sidecar/`)

**Второй мозг (Obsidian, локально, не в репо):**
`C:\Users\Maxim\Desktop\Second_brain\1-Projects\LLPlayer_ru\` — `_INDEX.md`, `Improvements.md`,
`Conventions.md` 🔒, `Sessions/`. **Авто-память:**
`C:\Users\Maxim\.claude\projects\C--Users-Maxim-Documents-GitHub-LLPlayer-ru\memory\MEMORY.md`.

**Статусы:** `TODO` (не начато) · `IN-PROGRESS` · `BLOCKED` · `DEFERRED` (отложено владельцем) · `DONE`.
**Важность:** 🔴 высокая · 🟠 средне-высокая · 🟡 средняя · 🟢 низкая/стратегическая.
**Сложность:** ⓢ тривиальная · Ⓜ средняя · Ⓛ крупная · ⓍⓁ очень крупная.

---

## 1. 🐛 БАГИ (подтверждённые)

### B-01 — Краш `ProductVersion is invalid` (брит­кий парсинг версии) 🔴 ⓢ · ✅ **DONE (PR #46, merge `73e95fa`, v0.3.8, 2026-06-25)** · был NEW (скриншот владельца)
> ✅ **Закрыт.** Толерантный парсер вынесен в `FlyleafLib/Utils/InformationalVersion.cs` (`Parse`: `Split('+', 2)`,
> commitHash → "" без суффикса, не бросает); `App.GetVersion` делегирует ему, `App.Version` для нормальных сборок
> байт-идентичен прежнему. Гейты build -warnaserror 0/0 + тесты 199/199 + verify-frozen/doc-coverage/plugin green;
> 4-линзовое adversarial `/code-review` (0 Critical/Important); `.exe` launch-тест на ТОЧНОМ условии бага (сборка без
> `+sha`, ProductVersion `"0.3.8"`) — окно плеера открывается, без краша. **Часть 2 (инъекция git-SHA при publish)
> НЕ понадобилась:** SHA встраивается автоматически при publish из git-чекаута (About уже показывает коммит); крашили
> именно НЕ-git сборки, которые теперь обрабатываются безопасно. Детали: второй мозг `Sessions/2026-06-25-handoff-b01-productversion-fix.md`.
**Симптом (скриншот):** диалог «Batch subtitles Unknown Error → Cannot save batch subtitle defaults:
ProductVersion is invalid: 0.3.7» при сохранении дефолтов в батч-диалоге.
**Файлы:** [`LLPlayer/App.xaml.cs:223-237`](../../LLPlayer/App.xaml.cs) (метод `GetVersion`),
триггер — [`LLPlayer/ViewModels/BatchSubtitlesDialogVM.cs:632-660`](../../LLPlayer/ViewModels/BatchSubtitlesDialogVM.cs)
(`PersistBatchDefaults` → `new AppConfig()` / `AppConfig.Load` / `Save`), которое через
[`LLPlayer/Services/AppConfig.cs:68`](../../LLPlayer/Services/AppConfig.cs) (`Version = App.Version`)
дёргает `GetVersion`. Версионирование: [`LLPlayer/LLPlayer.csproj:19`](../../LLPlayer/LLPlayer.csproj)
(`<Version>0.3.7</Version>`, БЕЗ инъекции git-SHA).
**Корневая причина:** `GetVersion()` делает `fvi.ProductVersion.Split("+")` и **бросает
`InvalidOperationException`, если частей не ровно 2** (т.е. нет суффикса `+commitHash`).
`AssemblyInformationalVersion` (=`ProductVersion`) получает `+{SourceRevisionId}` только когда сборка
встраивает git-SHA (SourceLink / `dotnet publish /p:SourceRevisionId=…`). Наши локальные/агентские
publish-сборки SHA НЕ встраивают → `ProductVersion = "0.3.7"` → краш.
**Где ещё читается `App.Version`/`CommitHash`** (та же хрупкость): миграции конфига
[`FlyleafLoader.cs:22,62`](../../LLPlayer/Services/FlyleafLoader.cs),
[`FlyleafManager.cs:44,57`](../../LLPlayer/Services/FlyleafManager.cs),
[`AppActions.cs:697,700`](../../LLPlayer/Services/AppActions.cs); экран About
[`SettingsAbout.xaml.cs:116`](../../LLPlayer/Controls/Settings/SettingsAbout.xaml.cs) и
[`ErrorDialogVM.cs:85`](../../LLPlayer/ViewModels/ErrorDialogVM.cs).
**⚠️ Эскалация серьёзности (мои рассуждения):** на старте `FlyleafLoader.StartEngine`/`CreateFlyleafPlayer`
сравнивают `config.Version != App.Version` ВНУТРИ `try/catch`, который при исключении делает
**`Environment.Exit(1)`** ([`FlyleafLoader.cs:42,83`](../../LLPlayer/Services/FlyleafLoader.cs)) с
сообщением «Cannot load … Please review the settings or delete the config file». Значит на сборке
без SHA, где конфиги УЖЕ существуют, приложение может **вообще не стартовать** (вводя в заблуждение, что
виноват конфиг). На свежей распакованной сборке (конфигов нет) старт проходит, но падают батч-сохранение
и About. **Гипотеза для проверки:** version-gated миграции конфига (`if (config.Version != App.Version)…`)
на таких сборках молча НЕ выполняются (исключение глотается на старте) → **возможная связь с открытым
багом «гигантский субтитр / тумблер ResegmentSubtitles не применяется»** (см. F-01). Проверить при фиксе.
**Решение (двусоставное):**
1. **Робастность (главное, дёшево):** в `GetVersion` не бросать — `var parts =
   fvi.ProductVersion.Split('+'); return (parts[0], parts.Length > 1 ? parts[1] : "");` (commitHash → "" /
   "dev"/"unknown" при отсутствии). Снимает краш во ВСЕХ путях (батч, About, миграции, startup).
2. **Сборка (чтобы About показывал коммит):** встраивать SHA в `InformationalVersion` —
   `dotnet publish /p:SourceRevisionId=<sha>` или `<SourceRevisionId>` в csproj, или Microsoft.SourceLink.
   Проверить `.github/actions/build-package/action.yml` и нашу publish-команду.
**Тесты:** unit на `GetVersion`-эквивалент с/без `+`; ручной smoke — собрать без SHA, открыть батч →
сохранить дефолты → нет попапа; открыть About → версия видна, commit пуст/`dev`.
**Заметка:** код хрупкого парсинга — из upstream (umlx5h/LLPlayer) → стоит завести и upstream-issue/PR.

### B-02 — `SubtitleSegmenter.MergeTooShort` не сливает слишком короткую ПЕРВУЮ реплику 🟠 ⓢ · TODO · chip `task_e97d7f20`
**Файл:** [`FlyleafLib/Utils/SubtitleSegmenter.cs:490-501`](../../FlyleafLib/Utils/SubtitleSegmenter.cs)
(гейт `merged.Count>0` на `:492`; контракт в docstring `:28-29` и комментарии `:481`).
**Проблема:** merge идёт только НАЗАД (в предыдущую реплику). Для первой реплики `merged` пуст → лидирующая
cue < `MinCueDurationSec` отдаётся verbatim, нарушая контракт «никогда не выдавать сливер». Подтверждено
3/3 скептиками code-review 2026-06-25, эмпирически воспроизведено компиляцией сегментера. Триггер узкий
(короткий первый токен перед длинным неразрывным куском, напр. вырожденный ASR/URL); косметический
flash-frame, без потери текста/краша → понижено high→medium.
**Решение:** после backward-merge — forward-merge головы: если `cues.Count>1` и первая cue всё ещё < min,
слить её во вторую (комбинировать `StripBreaks`+re-`Wrap`, `Start` = первой cue → сохранить
`first.Start==start`, `End` второй). + регресс-тест `Resegment("x "+200×'y'+" z", 0s, 8s, Min=1.0)`:
все длительности ≥ `MinCueDurationSec`.

### B-03 — `perLine` не клампится `Math.Max(1,…)` 🟡 ⓢ · TODO · chip `task_e97d7f20`
**Файл:** [`SubtitleSegmenter.cs:59,78`](../../FlyleafLib/Utils/SubtitleSegmenter.cs) (асимметрия с
`maxLines = Math.Max(1, …)` на `:60,:79`).
**Проблема:** `MaxCharsPerLine`/`MaxCjkCharsPerLine` = 0 проходит через UI Settings (TextBox
`OnlyNumeric="Uint"` принимает «0») → «по токену на строку». Только невалидный конфиг, без краша/потери.
**Решение:** `int perLine = Math.Max(1, IsCjkScript(norm) ? opt.MaxCjkCharsPerLine : opt.MaxCharsPerLine);`
на `:59` и `:78` + тест с `MaxCharsPerLine=0`.

> B-02 и B-03 в одном файле → бандлить вместе (и/или в PR F-01). Чип: `task_e97d7f20`.

### B-04 — LM Studio / локальный LLM: таймаут 60s мал для reasoning-моделей 🟠 ⓢ-Ⓜ · TODO · **NEW (скриншот владельца)**
**Симптом (скриншот):** «Cannot request to LMStudio: The request was canceled due to the configured
HttpClient.Timeout of 60 [seconds]». Владелец гоняет перевод через **reasoning-режим** LLM → модель «думает»
дольше 60s до выдачи перевода.
**Файлы:** [`ITranslateSettings.cs:508,524,542`](../../FlyleafLib/MediaPlayer/Translation/Services/ITranslateSettings.cs)
— `TimeoutMs = 60000` (дефолт локальных LLM: LM Studio / Ollama / KoboldCpp; базовый дефолт 10000 на `:34` и
др.). `GetHttpClient` (`:378`) ставит `Timeout = TimeSpan.FromMilliseconds(TimeoutMs)` — это **overall**-таймаут
(connect+read), не per-read.
**Решение:** (1) **быстрый win** — поднять дефолт локальных LLM в **2-3×** (120000-180000 ms), как просил
владелец; (2) **лучше** — для reasoning-моделей перейти на streaming + **скользящий read-timeout** (сбрасывается
при получении данных), т.к. reasoning думает произвольно долго и фиксированный overall-таймаут принципиально
неверен; (3) UX — таймаут уже редактируется в Settings ▸ Translate, но дефолт занижен → повысить + подсказка
про reasoning. **Связано:** max_tokens fallback (#42, reasoning-retry получает cap×2), `TimeoutHealthMs` (`:256`).
**Рассуждение:** мелкий, но юзер уже упирается; перевод через reasoning будет всё популярнее → дефолт должен
это учитывать. Сделать аккуратно (не сломать детект «cancel vs timeout», см. DeepLX/Microsoft `:101/:173`).

---

## 2. 🎯 ФИЧИ / ТРЕКИ (приоритизированный roadmap из конкурентного анализа + upstream)

Полное обоснование — `Sessions/2026-06-25-handoff-competitive-analysis-roadmap.md` (раздел E).
Принцип upstream: «специализированный плеер для изучения языка, НЕ универсальный» → паритет редактора
субтитров НЕ берём.

### F-01 — Универсальная ре-сегментация загруженных/sidecar/встроенных субтитров 🔴 Ⓜ · TODO · **(топ near-term, решено владельцем)**
**Проблема:** `SubtitleReader.ReadAll` (в [`SubtitlesManager.cs`](../../FlyleafLib/MediaPlayer/SubtitlesManager.cs))
минует `Resegment` → загруженный `.srt` с гигантскими репликами (~6 строк) показывается неразбитым, даже
если `ResegmentSubtitles=ON`. Сейчас `Resegment` применяется только к ASR
([`SubtitlesASR.cs:226`](../../FlyleafLib/MediaPlayer/SubtitlesASR.cs)) и батч-ASR; перевод — только wrap.
**Решение:** в `ReadAll` для ТЕКСТОВЫХ субтитров применять `SubtitleSegmenter.Resegment(text,start,end,opt)`
под гейтом `ResegmentSubtitles` (дефолт ON), **исключить bitmap/PGS**. Пользовательские `.srt` ре-таймятся
пропорционально (владелец согласовал). `Resegment` уже чистая; выход отсортирован → инвариант бинарного
поиска `Subs` цел (media-runtime-contract).
**Бандлить B-02 + B-03** (тот же файл). **Сначала проверить B-01** — миграции/тумблер могут не применяться.
**Рассуждение:** низкий риск (аддитивно, гейт ON, чистая функция); высокий UX-выигрыш (главная жалоба).

### F-02 — Точность ASR на шумном аудио / под музыку: speech separation / денойз 🟠 Ⓛ · TODO
**Идея от Buzz** (speech separation перед транскрипцией). Бьёт по нашей известной боли «речь съедается под
музыку» (частично закрыто anti-hallucination флагами в #42). **Решение:** опц. предобработка аудио
вокал-изоляцией (Demucs/аналог) в сайдкаре по образцу дубляжа (`dub_sidecar/`), opt-in. **Рассуждение:**
высокая ценность для качества субтитров; крупно (native-зависимости, сайдкар, GPU-no-overlap инвариант).

### F-03 — Диаризация (speaker ID) 🟡 Ⓛ · TODO
**Идея от Buzz.** Метки говорящих → лучше форматирование диалогов и понимание. **Решение:** сайдкар
pyannote-audio или возможности faster-whisper-XXL; метки в `SubtitleData`. **Рассуждение:** mission-fit
средний-высокий, крупно; фазами; согласуется с двойными субтитрами/диалогами.

### F-17 — ASR: дрейф языка (вкрапления чужого языка в русских субтитрах) 🟠 Ⓜ · TODO · **NEW (скриншот владельца)**
**Симптом (скриншот):** в русских субтитрах вылезают латинские/чужие токены — «Почему мы светило**ho**ти»,
«Я внов **tendencies**?», «Почему мы светило**hoти**». Владелец: реальных таких слов в аудио не было.
**Корень** (подтверждено экосистемой): известная проблема Whisper/faster-whisper —
[faster-whisper#869](https://github.com/SYSTRAN/faster-whisper/issues/869),
[whisper disc.#2285](https://github.com/openai/whisper/discussions/2285),
[disc.#2009](https://github.com/openai/whisper/discussions/2009). Whisper определяет язык по первым ~30s и/или
дрейфует на неуверенных/шумных участках, эмитя токены чужого языка/романизацию. **ВАЖНО:** наши
anti-hallucination флаги ставят `--condition_on_previous_text False` (#42) → меньше контекста-якоря → может
**усиливать** дрейф (trade-off против repetition-loop). Связано с T-10 (язык пиннится на первом сегменте) и
F-02 (денойз тоже снизит дрейф на музыке).
**Решение (кандидаты, из ресёрча):** (1) **жёстко задавать `--language ru`** (lock, не auto-detect), когда язык
известен — в UI есть «Audio Language», но дефолт = auto ([`WhisperConfig.cs:53,158-163`](../../FlyleafLib/Engine/WhisperConfig.cs));
(2) **`initial_prompt`/`Prompt`** ([`WhisperConfig.cs:137,202`](../../FlyleafLib/Engine/WhisperConfig.cs),
`WithPrompt` / faster-whisper `--initial_prompt`) с нормальным русским текстом — биасит и язык, и регистр (см. F-18);
(3) опц. `--suppress_tokens` для не-целевого письма (агрессивно); (4) пост-проход: детект cue со смешанным
письмом (кириллица+латиница в одном слове) → флаг/ре-ASR. **Проверить** подходы SubtitleEdit/faster-whisper-XXL.
**Рассуждение:** дёшево начать с (1)+(2); проверить, не конфликтует ли с anti-hallucination (возможно, для
reasoning/чистого аудио стоит вернуть `condition_on_previous_text True`). Источник: [memo.ac/whisper-hallucinations](https://memo.ac/blog/whisper-hallucinations).

### F-18 — ASR: субтитры пишутся КАПСОМ (ALL-CAPS) 🟠 ⓢ-Ⓜ · TODO · **NEW (скриншот владельца)**
**Симптом (скриншот):** целые куски капсом — «В СЛЕДУЮЩЕЙ СЕРИИ / ТА САМАЯ ИГРУШКА НА РАДИОУПРАВЛЕНИИ… / О,
БОЖЕ! / У НАС ПРОБЛЕМА» (фрагмент-превью «в следующей серии»).
**Корень** (экосистема): известная проблема faster-whisper(-XXL) — случайная капитализация, особенно на
превью/трейлерах/merged-строках; частично связывают с режимами форматирования XXL
([SubtitleEdit#9035](https://github.com/SubtitleEdit/subtitleedit/issues/9035),
[memo.ac/whisper-hallucinations](https://memo.ac/blog/whisper-hallucinations)).
**Решение:** (1) **аудит наших дефолтных `ExtraArguments`** faster-whisper на флаги, влияющие на регистр
(`--standard`/`--sentence` и т.п.); (2) **`initial_prompt`** нормального регистра биасит casing (общее с F-17);
(3) **пост-проход нормализации:** детектить ALL-CAPS cue (доля заглавных > порога) и приводить к sentence-case
(lowercase + капитализация после `.?!…`) — ровно фича SubtitleEdit «Tools ▸ Fix common errors ▸ Fix uppercase»
(**GPL-3.0 = наша → заимствуем легально**). Для русского риск только с аббревиатурами/именами, но капс-cue
заведомо неверны. Встроить как опц. ASR-постобработку рядом с `Resegment`.
**Рассуждение:** (3) — самый надёжный, не зависит от поведения движка; (1) проверить первым (может, мы сами
включили XXL-флаг). F-17+F-18 имеют общий рычаг (initial_prompt) → разумно делать вместе.

### F-04 — ASR pause/resume 🟠 Ⓜ · TODO · (upstream Roadmap «Now»)
**Файл/TODO:** [`SubtitlesASR.cs:27`](../../FlyleafLib/MediaPlayer/SubtitlesASR.cs) («TODO: L: Pause and
resume ASR»). **Решение:** управление состоянием ASR-задачи. **Рассуждение:** явный UX-win на длинных видео,
в дорожной карте upstream; средняя сложность.

### F-05 — Языковые предпочтения primary/secondary + авто-открытие 🟠 Ⓜ · TODO · (upstream «Now»)
**Решение:** расширить конфиг и логику открытия субтитров (per-slot язык, автоподбор внешних).
**Рассуждение:** ядро изучения языка; в upstream Roadmap «Now»; средняя сложность.

### F-06 — Экспорт транскрипта в TXT / VTT 🟡 ⓢ-Ⓜ · TODO
Сейчас экспорт только SRT ([`SrtExporter.cs`](../../LLPlayer/Services/SrtExporter.cs),
[`SubtitlesExportDialogVM.cs`](../../LLPlayer/ViewModels/SubtitlesExportDialogVM.cs)). Buzz/decipher/SE
умеют TXT/VTT. **Решение:** добавить writer'ы TXT (plain) и WebVTT + выбор формата в диалоге. **Рассуждение:**
низкий effort, средняя ценность (учащиеся выгружают транскрипты/субтитры для других инструментов).

### F-07 — AI-summary / извлечение лексики из транскрипта 🟡 Ⓜ · TODO
**Идея-плагин от Buzz** (AI summary). У нас уже есть LLM-интеграция (12 движков) и `PluginBase`. **Решение:**
действие «суммаризировать транскрипт» + «извлечь ключевую лексику». **Рассуждение:** сильный mission-fit,
мост к F-10 (Word Management/Anki — upstream «Future» LingQ/Language Reactor).

### F-08 — Хелпер синхронизации (shift-all / sync-to-current) 🟡 ⓢ-Ⓜ · TODO
**Идея от SubtitleEdit** (НЕ полный редактор). Сейчас только delay/offset
([`SubtitlesManager.cs` Delay](../../FlyleafLib/MediaPlayer/SubtitlesManager.cs)). **Решение:** «сдвинуть все
реплики на X», «синхронизировать по текущей позиции». **Рассуждение:** маленький, помогает рассинхрону
загруженных субтитров; mission-fit для просмотра.

### F-09 — Watch-folder авто-batch 🟢 ⓢ-Ⓜ · TODO
**Идея от Buzz.** Расширение существующего батча (`Batch*`-классы). **Решение:** режим слежения за папкой →
авто-обработка новых файлов. **Рассуждение:** низкий effort, удобство для пакетной обработки.

### F-10 — Anki-интеграция / Word Management 🟢 Ⓛ · TODO · (upstream «Future» LingQ/Language Reactor)
**Решение:** персистентные списки слов, экспорт в Anki-колоды/SRS. **Рассуждение:** высокий mission-fit,
крупно; строится на F-07.

### F-11 — Dictionary API (англ./яп. и др.) 🟢 Ⓛ · TODO · (upstream «Later»)
Сейчас только перевод слова, не словарные определения (FAQ README). **Решение:** интеграция словарных API.
**Рассуждение:** высокий mission-fit, сложно (много языков) — потому upstream отложил.

### F-12 — Аудио-waveform (визуализация) 🟢 Ⓛ · TODO
**Идея от SubtitleEdit.** **Решение:** рендер waveform из аудио FlyleafLib для точного A-B/sync.
**Рассуждение:** средний mission-fit, крупный effort; не топ для плеера.

### F-13 — Кросс-платформенность (Avalonia, Linux/Mac) 🟢 ⓍⓁ · DEFERRED · (upstream «Future»)
SE5 и Buzz уже кросс-платформенны → наш Windows-only = конкурентный минус. **Решение:** порт UI на Avalonia
(движок FlyleafLib + WPF-слой). **Рассуждение:** огромная работа (фактически переписывание UI), стратегическая
цель; не трогать инцидентно.

### F-14 — Расширенный локальный поиск субтитров 🟢 ⓢ-Ⓜ · TODO · (upstream «Now»)
**Решение:** улучшить инкрементальный поиск в `SubtitlesSidebar`
([`SubtitlesSidebarVM.cs`](../../LLPlayer/ViewModels/SubtitlesSidebarVM.cs)). **Рассуждение:** удобство,
небольшой; в upstream Roadmap.

### F-15 — Yomitan / 10ten в плеере 🟡 Ⓜ-Ⓛ · TODO · ([upstream issue #13](https://github.com/umlx5h/LLPlayer/issues/13), «Later»)
Сейчас только через буфер обмена (FAQ). **Решение:** встроенный мост к словарным браузер-расширениям.
**Рассуждение:** ценно для японского/анки-воркфлоу; средне-крупно.

### F-16 — Дубляж: расширение голосов/качества (фазы 1-6) 🟢 Ⓛ · TODO
Дубляж — Phase 0 (PR #35 влит, CosyVoice2 в `dub_sidecar/`). SE предлагает много TTS (Edge/Kokoro/OmniVoice
voice-cloning). **Решение:** фазы 1-6 из [[2026-06-23-handoff-dubbing-mvp]] (мульти-голос, качество,
diarization-aware). **Рассуждение:** крупно; держать как продолжение существующей фичи.

---

## 3. 🧰 ТЕХДОЛГ / ИНФРАСТРУКТУРА / МЕЛКИЕ TODO

### T-01 — Рассинхрон FFmpeg-биндингов (8.0.1 vs 7.1.1) 🟠 Ⓜ · TODO
`LLPlayer` ссылается на `Flyleaf.FFmpeg.Bindings 8.0.1` ([`LLPlayer.csproj:33`](../../LLPlayer/LLPlayer.csproj)),
а `FlyleafLib` — на `7.1.1` (см. `docs/agent/dependency-baseline.md`). Известный baseline-warning,
потенц. рантайм-несовместимость декодирования/рендера. **Решение:** выровнять версии, прогнать
`scripts/codex/verify.ps1` + ручной smoke воспроизведения, обновить `dependency-baseline.md`.

### T-02 — Ранняя диагностика VC++ Redistributable 🟠 ⓢ-Ⓜ · TODO
Без VC++ 2022+ приложение стартует, но падает при включении ASR/OCR (README/FAQ). **Решение:** усилить
раннюю диагностику/понятное сообщение до включения ASR/OCR. **Рассуждение:** молчаливый краш = плохой UX.

### T-03 — Расширение тестового покрытия 🟡 Ⓜ · ONGOING
189 тестов, но крупные области без юнитов. **Решение:** покрыть парсинг субтитров, перевод (моки сети),
ASR/OCR (где детерминируемо), playlist/demuxer-утилиты. Связано с фиксами B-01/B-02/B-03 (добавить регресс).

### T-04 — Whisper-квантизация (q8_0/q5_0) в UI 🟡 ⓢ-Ⓜ · TODO
whisper.cpp/Whisper.net поддерживают квантизованные модели, но в UI не выведено. **Решение:** дать выбор
квантизации (лучший безопасный выигрыш скорости). См. References whisper-research во втором мозге.

### T-05 — Судьба M3-редизайна (PR #31) 🟢 — · DEFERRED
[PR #31](https://github.com/Gorgutc/LLPlayer_ru/pull/31) (`claude/modest-brown-29ced0`, Material 3 re-skin) —
OPEN, **отложен владельцем**. **Решение:** решить — мерджить, доработать (глоб. M3-скроллбары/focus-ring) или
закрыть в пользу нового фреймворка. **Рассуждение:** не трогать без явного решения владельца.

### T-06 — Дрейф документации форка vs upstream 🟢 ⓢ · TODO
Суффикс `_ru` без локализации приложения; README — английский upstream. **Решение:** явно зафиксировать цель
форка (агентская инфраструктура vs локализация) либо начать RU-локализацию ресурсов.

### T-07 — `SrtExporter`: поддержка тегов `<i>` 🟢 ⓢ · TODO
[`SrtExporter.cs:8`](../../LLPlayer/Services/SrtExporter.cs) TODO. Сейчас экспорт теряет курсив/стили.

### T-08 — ASR fold-back при перемотке назад 🟢 Ⓜ · TODO
[`SubtitlesASR.cs:610`](../../FlyleafLib/MediaPlayer/SubtitlesASR.cs) TODO («Fold back and allow the first
half to run as well»). При seek назад первая половина может не обработаться.

### T-09 — ASR: дробление чанков по тишине 🟢 Ⓜ · TODO
[`SubtitlesASR.cs:759`](../../FlyleafLib/MediaPlayer/SubtitlesASR.cs) TODO («split at the silent part»).
Сейчас чанки режутся по размеру/времени, не по тишине → возможны разрывы фраз.

### T-10 — Per-segment language detection 🟢 Ⓜ · TODO
[`SubtitlesASR.cs:~1059`](../../FlyleafLib/MediaPlayer/SubtitlesASR.cs) TODO. Язык пиннится на первом
непустом сегменте; смешанный по языку контент не дораспознаётся.

### T-11 — Sandbox `dotnet`/Windows SDK + нет .NET 10 SDK у владельца 🟢 ⓢ · DOC
Sandbox `dotnet` иногда падает при чтении Windows SDK из AppData; у Maxim нет .NET 10 SDK (есть 8/9/11-preview;
11-preview собирает net10.0). **Решение:** задокументировать процедуру эскалации/окружения.

---

## 4. 📊 РАНЖИРОВАНИЕ ПО ВАЖНОСТИ (убыв.)

| # | ID | Задача | Важн. | Сложн. |
|---|----|--------|:---:|:---:|
| 1 | **B-01** | Краш ProductVersion (юзер-facing, ломает save + может блокировать старт + миграции) | 🔴 | ⓢ |
| 2 | **F-01** | Универсальная ре-сегментация (чинит «гигантский субтитр», решено владельцем) | 🔴 | Ⓜ |
| 3 | **T-01** | Рассинхрон FFmpeg-биндингов (риск декодирования) | 🟠 | Ⓜ |
| 4 | **F-02** | ASR денойз/speech-separation (точность под музыку) | 🟠 | Ⓛ |
| 5 | **F-05** | Языковые префы primary/secondary | 🟠 | Ⓜ |
| 6 | **B-02** | Сегментер: короткая первая реплика | 🟠 | ⓢ |
| 7 | **F-04** | ASR pause/resume | 🟠 | Ⓜ |
| 8 | **T-02** | Ранняя диагностика VC++ | 🟠 | ⓢ-Ⓜ |
| 9 | **F-06** | Экспорт TXT/VTT | 🟡 | ⓢ-Ⓜ |
| 10 | **F-07** | AI-summary / лексика | 🟡 | Ⓜ |
| 11 | **F-15** | Yomitan/10ten в плеере | 🟡 | Ⓜ-Ⓛ |
| 12 | **F-03** | Диаризация | 🟡 | Ⓛ |
| 13 | **T-03** | Тестовое покрытие | 🟡 | Ⓜ |
| 14 | **F-08** | Sync-хелпер (shift-all) | 🟡 | ⓢ-Ⓜ |
| 15 | **B-03** | Сегментер: кламп perLine | 🟡 | ⓢ |
| 16 | **T-04** | Whisper-квантизация в UI | 🟡 | ⓢ-Ⓜ |
| 17 | **F-14** | Расширенный локальный поиск | 🟢 | ⓢ-Ⓜ |
| 18 | **F-09** | Watch-folder авто-batch | 🟢 | ⓢ-Ⓜ |
| 19 | **F-10** | Anki / Word Management | 🟢 | Ⓛ |
| 20 | **F-11** | Dictionary API | 🟢 | Ⓛ |
| 21 | **F-16** | Дубляж фазы 1-6 | 🟢 | Ⓛ |
| 22 | **F-12** | Аудио-waveform | 🟢 | Ⓛ |
| 23 | **T-07** | SrtExporter теги `<i>` | 🟢 | ⓢ |
| 24 | **T-08/09/10** | ASR TODO (fold-back / silence-split / per-seg lang) | 🟢 | Ⓜ |
| 25 | **T-06** | Дрейф документации | 🟢 | ⓢ |
| 26 | **T-05** | Решение по M3-редизайну (PR #31) | 🟢 | — |
| 27 | **F-13** | Кросс-платформенность Avalonia | 🟢 | ⓍⓁ |
| 28 | **T-11** | Sandbox/SDK гряз (doc) | 🟢 | ⓢ |

## 5. 🛠️ РАНЖИРОВАНИЕ ПО СЛОЖНОСТИ (возр. — самое лёгкое сверху)

| # | ID | Задача | Сложн. | Важн. |
|---|----|--------|:---:|:---:|
| 1 | **B-03** | Кламп perLine (≈2 строки) | ⓢ | 🟡 |
| 2 | **T-11** | Sandbox/SDK doc | ⓢ | 🟢 |
| 3 | **T-06** | Дрейф документации | ⓢ | 🟢 |
| 4 | **T-07** | SrtExporter теги `<i>` | ⓢ | 🟢 |
| 5 | **B-01** | Фикс ProductVersion (≈5 строк + сборка/тест) | ⓢ | 🔴 |
| 6 | **B-02** | Сегментер: forward-merge головы (+тест) | ⓢ | 🟠 |
| 7 | **T-02** | Ранняя диагностика VC++ | ⓢ-Ⓜ | 🟠 |
| 8 | **F-06** | Экспорт TXT/VTT | ⓢ-Ⓜ | 🟡 |
| 9 | **F-09** | Watch-folder | ⓢ-Ⓜ | 🟢 |
| 10 | **T-04** | Whisper-квантизация UI | ⓢ-Ⓜ | 🟡 |
| 11 | **F-14** | Локальный поиск | ⓢ-Ⓜ | 🟢 |
| 12 | **F-08** | Sync-хелпер | ⓢ-Ⓜ | 🟡 |
| 13 | **F-01** | Универсальная ре-сегментация (+ре-тайминг, тесты, контракт) | Ⓜ | 🔴 |
| 14 | **F-04** | ASR pause/resume | Ⓜ | 🟠 |
| 15 | **F-05** | Языковые префы | Ⓜ | 🟠 |
| 16 | **T-01** | FFmpeg-биндинги (выравнивание + smoke) | Ⓜ | 🟠 |
| 17 | **T-03** | Тестовое покрытие (ongoing) | Ⓜ | 🟡 |
| 18 | **F-07** | AI-summary / лексика | Ⓜ | 🟡 |
| 19 | **T-08/09/10** | ASR TODO | Ⓜ | 🟢 |
| 20 | **F-15** | Yomitan/10ten мост | Ⓜ-Ⓛ | 🟡 |
| 21 | **F-02** | ASR денойз (сайдкар) | Ⓛ | 🟠 |
| 22 | **F-03** | Диаризация (сайдкар) | Ⓛ | 🟡 |
| 23 | **F-16** | Дубляж фазы 1-6 | Ⓛ | 🟢 |
| 24 | **F-10** | Anki / Word Management | Ⓛ | 🟢 |
| 25 | **F-11** | Dictionary API | Ⓛ | 🟢 |
| 26 | **F-12** | Аудио-waveform | Ⓛ | 🟢 |
| 27 | **F-13** | Avalonia (переписывание UI) | ⓍⓁ | 🟢 |
| — | **T-05** | M3-редизайн — решение владельца (не оценивается) | — | 🟢 |

---

## 5b. Δ Ранжирование с учётом задач из скриншотов 2026-06-25 (B-04 / F-17 / F-18)
Чтобы не переписывать таблицы выше, фиксирую позиции трёх новых задач:
- **По важности:** `B-04` (LM Studio timeout) ≈ **#5** (🟠, юзер упирается прямо сейчас, фикс мелкий);
  `F-17` (дрейф языка) ≈ **#6** (🟠, портит субтитры); `F-18` (капс) ≈ **#8** (🟠).
- **По сложности (легче → тяжелее):** `B-04` ≈ **#7** (ⓢ-Ⓜ; быстрый win = поднять дефолт);
  `F-18` ≈ **#9** (ⓢ-Ⓜ; пост-проход case-fix); `F-17` ≈ **#13** (Ⓜ; language-lock + initial_prompt + проверка
  конфликта с anti-hallucination).
- **Группировки:** `F-17`+`F-18` имеют общий рычаг (`initial_prompt` нормального регистра) → делать вместе,
  одним «ASR-quality» PR. `B-04` — можно приклеить к быстрому PR `B-01`.

## 6. 🧭 Рекомендуемая последовательность ближайших сессий (мои рассуждения)
1. ~~**B-01** — отдельным быстрым PR~~ ✅ **СДЕЛАНО (PR #46, v0.3.8, 2026-06-25).** Гипотеза подтверждена: на старте
   `FlyleafLoader` читает `App.Version` в `try/catch` с `Environment.Exit(1)` → краш мог блокировать запуск на
   сборке без SHA с существующим конфигом. Фикс снят. (SHA-инъекция при publish оказалась автоматической на git-сборках.)
2. **F-01 + B-02 + B-03** — один PR по `SubtitleSegmenter`/`ReadAll` (универсальная ре-сегментация + 2 фикса).
3. Затем по важности: **T-01** (FFmpeg), **F-05/F-04** (upstream «Now»), **F-06** (быстрый win), далее Tier-1/2.
**Координация:** ветка дубляжа и PR #31 — не конфликтовать; перед поведенческими правками сверяться с
frozen-контрактами; гейты `scripts/codex/verify.ps1` (build -warnaserror 0/0 + xUnit) + launch-test `.exe`.

## 7. ⚙️ Процессные заметки (грабли инфры — для будущих сессий)
- **`/deep-research` харнесс упал ДВАЖДЫ** (auth 403 → server rate-limit, 0 источников): параллельный веер
  десятков веб-фетчей бьётся о лимиты. **Для конкретных репо — фетчить первоисточники самому `WebFetch`
  основным циклом** (он стабилен), generic-харнесс беречь для широкого поиска.
- **Многоагентные веера теряют верификаторы на rate-limit** (code-review подтвердил только сегментацию —
  у остальных измерений верификаторы упали). При нестабильности — снижать concurrency / верифицировать
  ключевые находки основным циклом.
- **Build-гряз:** наши локальные publish-сборки не встраивают git-SHA → см. B-01.
