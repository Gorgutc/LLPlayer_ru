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

### B-02 — `SubtitleSegmenter.MergeTooShort` не сливает слишком короткую ПЕРВУЮ реплику 🟠 ⓢ · ✅ **DONE (codex PR #48, merge `37ea200`; тесты усилены PR #49, merge `284da38`)**
> ✅ **Закрыт 2026-06-26.** Forward-merge короткой первой реплики добавлен в `MergeTooShort` (codex PR #48). Продакшн-фикс корректен — подтверждено 4-агентным adversarial-ревью + fuzz 4000 входов (old=51 sliver-cue, new=0), termination/no-text-loss/contiguity доказаны. **Но тест из #48 был ВАКУУМНЫМ** (вход `"x "+80×"readable"+" z"` не изолировал короткую первую cue) → заменён на предложенный здесь `Resegment("x "+200×'y'+" z", 0s, 8s)` (pre-fix первая cue = 40мс) в тестовом PR #49, проверено RED-without-fix → GREEN-with-fix. Детали: второй мозг `Sessions/2026-06-26-handoff-sync-codex-pr48.md`.
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

### B-03 — `perLine` не клампится `Math.Max(1,…)` 🟡 ⓢ · ✅ **DONE (codex PR #48, merge `37ea200`)**
> ✅ **Закрыт 2026-06-26.** Новый `GetEffectivePerLine` клампит `perLine = Math.Max(1, …)` на обоих сайтах (`:59`, `:78`) — codex PR #48. Кламп защитный (наблюдаемый эффект маскируется уже-существующими гейтами `CeilDiv(Math.Max(1,budget))` и пост-merge); тест на `MaxCharsPerLine=0` усилен до наблюдаемой формы в PR #49.
**Файл:** [`SubtitleSegmenter.cs:59,78`](../../FlyleafLib/Utils/SubtitleSegmenter.cs) (асимметрия с
`maxLines = Math.Max(1, …)` на `:60,:79`).
**Проблема:** `MaxCharsPerLine`/`MaxCjkCharsPerLine` = 0 проходит через UI Settings (TextBox
`OnlyNumeric="Uint"` принимает «0») → «по токену на строку». Только невалидный конфиг, без краша/потери.
**Решение:** `int perLine = Math.Max(1, IsCjkScript(norm) ? opt.MaxCjkCharsPerLine : opt.MaxCharsPerLine);`
на `:59` и `:78` + тест с `MaxCharsPerLine=0`.

> B-02 и B-03 в одном файле → бандлить вместе (и/или в PR F-01). Чип: `task_e97d7f20`.

### B-04 — LM Studio / локальный LLM: таймаут 60s мал для reasoning-моделей 🟠 ⓢ-Ⓜ · ✅ **DONE (PR #51, merge `fa40c45`, v0.3.9, 2026-06-26)**
> ✅ **Закрыт.** Дефолт `TimeoutMs 60000→180000` для Ollama/LM Studio/KoboldCpp (ctor'ы) + **миграция** существующих
> конфигов (`Config.UpdateDefault`, гейт `<= 0.3.8`, one-shot через bump `0.3.9`, сохраняет явные значения; вынесена в
> тестируемый `MigrateLocalLlmTimeoutDefault`). Гейты 0/0, тесты **208/208** (+6, mutation-verified), verify-frozen
> (+2 гейта), 4-линзовое adversarial `/code-review` (0 Critical/Important), `.exe` launch-тест 0.3.9 зелёный. **Принят
> «быстрый win» (raise+migration); принципиальное решение (streaming + скользящий read-timeout) — отдельная будущая
> задача.** Follow-up из ревью: `LiteLLM`/`OpenAILike` остались на базовом `15000` (endpoint может быть облачным) —
> при нужде поднять headroom и для локально-направленных endpoint'ов. Детали: второй мозг `Sessions/2026-06-26-handoff-b04-llm-timeout.md`.
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

### F-01 — Универсальная ре-сегментация загруженных/sidecar/встроенных субтитров 🔴 Ⓜ · ✅ **DONE (PR #53, merge `e9f92f3`, v0.3.10, 2026-06-27)**
> ✅ **Закрыт.** Новый `SubManager.ResegmentLoaded` в коллбэке `ReadAll` ре-сегментирует загруженные ТЕКСТОВЫЕ
> реплики под гейтом `ResegmentSubtitles` (дефолт ON). **Исключены** bitmap/PGS И **стилизованные ASS** (несут
> `SubStyles` — переразбивка инвалидировала бы оффсеты; это сверх спеки бэклога, которая требовала только
> bitmap/PGS — добавлено для корректности). **Решение владельца (AskUserQuestion):** загруженные субтитры дробятся
> **только по переполнению строк/символов, НЕ по длительности** (`MaxCueDurationSec=0` для загруженных) — чтобы
> намеренно-долгую авторскую реплику не фрагментировать; гигантский 6-строчный блок чинится переполнением строк.
> Инвариант сортировки `Subs` цел. Гейты 0/0, тесты **216/216** (+8), 4-линзовое adversarial `/code-review`
> (0 Critical/Important), `.exe` launch-тест 0.3.10 зелёный. Детали: второй мозг `Sessions/2026-06-27-handoff-f01-universal-resegment.md`.
> **Известное следствие:** экспорт загруженных субтитров (SrtExporter) теперь выгружает ре-сегментированную версию (файл на диске не трогается).
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

### F-02 — Точность ASR на шумном аудио / под музыку: speech separation / денойз 🟠 Ⓛ · ⚙️ **СРЕЗ DONE (PR #76, merge `b5f9221`, v0.3.23, 2026-06-27); полный Demucs ОСТАЁТСЯ TODO**
> ⚙️ **Лёгкий срез отгружен (PR #76):** opt-in `Subtitles.ASRDenoise` (default OFF → byte-identical) чистит аудио,
> подаваемое в Whisper, на одном seam в продюсере (`SubtitlesASR.ResampleTo`) → покрывает **оба движка**
> (whisper.cpp + faster-whisper, общий `waveStream`) **и батч**. Решение владельца (AskUserQuestion): «Оба» —
> **managed high-pass** (`FlyleafLib/Utils/AsrDenoise.cs` `AsrHighPassFilter`, RBJ biquad Butterworth 80Hz, чистый/
> тестируемый, 10 юнит-тестов) **+ нативный FFmpeg `afftdn`** (изолированный avfilter-граф в `AudioReader`, зеркало
> `AudioDecoder.Filters.cs`; graph-per-pass, flush в конце прохода + перед reachedStop-резом; **fail-soft** →
> managed-high-pass-only при недоступности afftdn). Config `ASRDenoise` (+ батч-снапшот reflection-guard) + UI-тумблер
> «Denoise ASR Audio» + frozen `media-runtime-contract` +1 предложение. Дизайн-панель (3) + adversarial review
> (5 линз: native-memory-safety SHIP 0 находок; 1 minor fold-back-стык исправлен; 2 нита acceptable). Гейты 0/0 ×3 +
> verify.ps1 + тесты **499/499** (+10), `.exe` launch 0.3.23 чистый. **Честно: срез бьёт по СТАЦИОНАРНОМУ шуму**
> (хисс/гул/рокот), **НЕ разделяет речь/музыку.** afftdn-качество/стыки — owner manual-smoke. Детали: второй мозг
> `Sessions/2026-06-27-handoff-f02-asr-denoise.md`.
>
> ### ⚠️ АЛЬТЕРНАТИВА / ЭСКАЛАЦИЯ (полный F-02 — Demucs-сайдкар) — STANDBY, в работу ПО ТРИГГЕРУ
> **ТРИГГЕР (владелец 2026-06-27):** если лёгкий срез выше НЕ устроит — **музыка всё равно перебивает речь** —
> переходим с него на Demucs-сайдкар. (Полный готовый план + сохранённые рассуждения: второй мозг
> `Sessions/2026-06-27-f02-full-demucs-escalation-plan.md`; авто-память `llplayer-f02-full-demucs-escalation.md`.)
> - **Почему срез не чинит музыку:** `afftdn` = денойзер СТАЦИОНАРНОГО шума (вычитает усреднённый спектральный
>   пол) → бьёт хисс/гул/вентилятор; **музыка нестационарна, широкополосна, перекрывает речь** → afftdn её не
>   убирает; high-pass режет лишь суб-бас. Вытащить речь из-под музыки может ТОЛЬКО ML source separation (Demucs).
> - **Решение:** Python-сайдкар по образцу `dub_sidecar/` (stdlib-HTTP, ленивые torch/demucs-импорты, `--mock`,
>   parent-watchdog) с **Demucs** (`htdemucs`) → стем `vocals`; C#-супервизор зеркалит `DubSidecarHost.cs` (Job
>   Object). Demucs = **MIT** (совместимо с GPL-3.0); `check-dub-licenses.ps1` валидирует деп; `dub_sidecar/
>   pyproject.toml` уже пинит torch на **cu128** (RTX 5090 / sm_120) — переиспользовать.
> - **Seam (рекомендуется): pre-ASR pre-pass** — разово отделить аудио в `<video>.vocals.wav` (по образцу
>   пре-рендера дубляжа `video.ru.dub.flac`), затем ASR `AudioReader` читает ЭТОТ трек. Pre-pass делает Demucs и
>   ASR ПОСЛЕДОВАТЕЛЬНЫМИ по построению → снимает GPU-контеншен. (Per-chunk сепарация — тяжелее, не для v1.)
> - **⚠️ Главная сложность — GPU no-overlap:** Demucs+whisper+CosyVoice-дубляж на одной GPU; координационного
>   примитива ещё НЕТ (`DubSidecarHost` без mutex/lease). Pre-pass обходит это; иначе нужен общий GPU-lease
>   (прецеденты: батч «serialize ASR/translation», «CPU-fallback-while-active»).
> - **Миграция флага:** `bool ASRDenoise` → enum `{ Off, Lite (high-pass+afftdn = текущее), Demucs }` (срез
>   остаётся как Lite). Чистая логика → FlyleafLib (тестируемо); Demucs/HTTP — owner ear-test на RTX 5090.
> - **Эффорт: multi-session (Ⓛ→ⓍⓁ)**; фазами (mock pre-pass → реальный Demucs+ear-test → потоковая → выбор
>   модели). Общая инфра с F-16 (дубляж) и F-03 (диаризация) — те же сайдкар/GPU-вопросы.

**Идея от Buzz** (speech separation перед транскрипцией). Бьёт по нашей известной боли «речь съедается под
музыку» (частично закрыто anti-hallucination флагами в #42 + теперь стационарный денойз срезом). **Решение
(полное):** опц. предобработка аудио вокал-изоляцией (Demucs/аналог) в сайдкаре по образцу дубляжа
(`dub_sidecar/`), opt-in. **Рассуждение:** высокая ценность для качества субтитров; крупно.

### F-03 — Диаризация (speaker ID) 🟡 Ⓛ · IN-PROGRESS (prep-срез ✅ PR #102 v0.3.33; диаризация = GPU-сайдкар TODO)
> ⚙️ **Prep-срез отгружен (PR [#102](https://github.com/Gorgutc/LLPlayer_ru/pull/102), merge `710bc70`, v0.3.33, 2026-06-29).**
> GPU-free задел схемы по паттерну T-10: новое inert nullable-поле `SubtitleData.SpeakerId` (`string?`, default null →
> **byte-identical** — ничего не пишет, диаризация = будущий GPU-сайдкар). Переносится в `SubtitleData.Clone()` И на
> split-cue на ОБОИХ сайтах ре-сегментации (`BatchAsrTranscriber`, `SubtitlesManager.ResegmentLoaded`) — попутно
> закрыт **латентный gap T-10**, где per-cue `Language` терялся при ре-сегментации (inert сегодня → byte-identical).
> **Решение владельца (AskUserQuestion ×2):** взять F-03 prep + форма схемы = простой `string` id (не record
> `Speaker{Id,Name,Gender,Language}`). Тесты +3 (1035→**1038**); `media-runtime-contract` аддитивный буллет;
> `config-data` НЕ тронут (нет персист-ключа). Многоагентно: верификация (`w1ac9316y`, 6 агентов) → adversarial-ревью
> (`wondq7zcl`, 4 линзы+триаж: SHIP, 0 must-fix; seam-completeness находка ПРИМЕНЕНА = 2 правки переноса) → `/code-review high`
> Approve. Гейты build `-warnaserror` **0/0** + **1038/1038** + verify.ps1 green; **`.exe` launch 0.3.33 чистый**
> (жив 13с, без crash.log, FFmpeg+e_sqlite3). **Разблокирует F-16 per-line/per-speaker дубляж.** **Остаток F-03:**
> сама диаризация (pyannote-audio сайдкар по образцу `dub_sidecar/` → заполнение `SpeakerId`) + потребление дисплеем/
> экспортом/дубляжом — GPU + multi-session. Детали: второй мозг `Sessions/2026-06-29-session-LIVE-tracker-11.md`.
**Идея от Buzz.** Метки говорящих → лучше форматирование диалогов и понимание. **Решение:** сайдкар
pyannote-audio или возможности faster-whisper-XXL; метки в `SubtitleData`. **Рассуждение:** mission-fit
средний-высокий, крупно; фазами; согласуется с двойными субтитрами/диалогами.

### F-17 — ASR: дрейф языка (вкрапления чужого языка в русских субтитрах) 🟠 Ⓜ · ✅ **DONE (часть — PR #55, merge `3359462`, v0.3.11, 2026-06-27)**
> ✅ **Закрыт рычагом `initial_prompt`.** Новое поле `FasterWhisperConfig.Prompt` → `--initial_prompt` (де-дуп vs
> ExtraArguments) смещает язык/письмо/регистр у источника. Language-lock уже работает при `LanguageDetection=false`
> (`--language` в BuildCommand) — документировано. **Не делали (рискованно):** автостриппинг смешанного письма
> (мог бы портить легитимные имена/термины) — отложено. F-18 (капс) закрыт тем же PR. Детали: второй мозг
> `Sessions/2026-06-27-handoff-f17-f18-asr-quality.md`.
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

### F-18 — ASR: субтитры пишутся КАПСОМ (ALL-CAPS) 🟠 ⓢ-Ⓜ · ✅ **DONE (PR #55, merge `3359462`, v0.3.11, 2026-06-27)**
> ✅ **Закрыт.** Новый чистый `FlyleafLib/Utils/SubtitleCaseFixer.cs` — ALL-CAPS cue (доля заглавных > 0.6, ≥ 2 слов)
> → sentence-case; не трогает акроним/одно слово/нормальный текст/цифры/URL (`://`). Тумблер `Subtitles.FixAllCaps`
> (дефолт ON), в интерактивном ASR и батче (до ре-сегментации), НЕ к загруженным. + `initial_prompt` (общий рычаг
> с F-17) биасит регистр у источника. Гейты 0/0, тесты **235/235** (+19), 4-линзовое adversarial `/code-review`
> (0 Critical/Important; MEDIUM URL-кейс исправлен), `.exe` launch 0.3.11 зелёный. Известный trade-off: легитимный
> ALL-CAPS бренд/крик/имя в капс-cue тоже приводится к sentence-case (тумблер позволяет выключить). Детали:
> `Sessions/2026-06-27-handoff-f17-f18-asr-quality.md`.
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

### F-04 — ASR pause/resume 🟠 Ⓜ · ✅ **DONE (PR #65, merge `f6c7625`, v0.3.17, 2026-06-27)** · (upstream Roadmap «Now»)
> ✅ **Закрыт по плану ниже.** Новый чистый тестируемый `FlyleafLib/Utils/PauseTokenSource.cs` (`PauseTokenSource`+`PauseToken`
> struct) — async-гейт, не блокирует тред, cancellation-aware (не мутирует shared TCS на отмене), thread-safe (Interlocked CAS);
> `default` PauseToken = never-paused → батч передаёт его и получает no-op. `SubtitlesASR.Pause()/Resume()/IsPaused`; reset гейта
> на старте каждого `Execute` (не родиться paused после seek) и в `finally`; consumer в `AudioReader.ReadAll` `await`'ит гейт на
> **границе чанка** (канал bounded cap 1-2 → producer backpressure'ит на `WriteAsync`, не убегает). Пауза **СОХРАНЯЕТ субтитры**
> (в отличие от `TryCancel`, который чистит). UI: `Player.IsASRPaused` + `AppActions.CmdToggleASRPause` + кликабельный ASR-чип в
> `FlyleafBar.xaml` (иконка Pause/Play). Скоуп — только интерактивный ASR (батч НЕ затронут, default token). Гейты build 0/0,
> тесты **275/275**, verify-frozen/doc-coverage green, **6-линзовое adversarial `/code-review` (14 агентов; 0 critical, 0 дефектов
> продакшн-кода — подтверждённые находки = усиление тестов concurrency-примитива, внесено: concurrent stress + bounded-channel
> back-pressure + tight-timeout)**, `.exe` launch-тест 0.3.17 чистый. Контракты product-behavior/media-runtime(ASR-threading)/
> wpf-design/manual-smoke обновлены. Интегрирован с параллельным PR #64 (батч-VC++, v0.3.16) через merge → версия 0.3.17.
> Детали: второй мозг `Sessions/2026-06-27-handoff-f04-asr-pause-resume.md`.
**Файл/TODO:** [`SubtitlesASR.cs:27`](../../FlyleafLib/MediaPlayer/SubtitlesASR.cs) («TODO: L: Pause and resume ASR»).
**Рассуждение:** явный UX-win на длинных видео; в дорожной карте upstream; средняя сложность. Самый крупный/рискованный
пункт остатка (правка frozen ASR-threading + UI), поэтому владелец (AskUserQuestion 2026-06-27) выбрал **отдельную
сессию** под него; здесь — готовый план.

> **📐 ПЛАН (готов к реализации в свежей сессии):**
> **Архитектура (выяснено 2026-06-27):** `SubtitlesASR.Execute` (`:125`) → `AudioReader.ReadAll` запускает
> producer→consumer на каналах (`System.Threading.Channels`): producer демультиплексирует аудио в чанки
> (`while (!token.IsCancellationRequested)` ~`:667`), consumer гоняет ASR-движок по чанку
> (`while (await channel.Reader.WaitToReadAsync(token))` ~`:510`, внутри `asrService.Do(chunk.Stream, token)`).
> Движки: WhisperCpp in-proc (`Do` ~`:1062`), FasterWhisper — ВНЕШНИЙ процесс (`Do` ~`:1330`). `_cts` = отмена;
> `TryCancel` (`:301`) **чистит субтитры** (`_subtitlesManager[i].Clear()`), флаг `player.IsASRRunning` (`:202`).
> **Единственная реализуемая гранулярность — пауза на ГРАНИЦЕ ЧАНКА** (чанк транскрибируется атомарно; внешний
> faster-whisper не прерывается mid-chunk). **Решение владельца:** пауза останавливает на след. границе чанка и
> **СОХРАНЯЕТ накопленные субтитры** (в отличие от Cancel).
> **Шаги:**
> 1. **FlyleafLib `SubtitlesASR`:** добавить async-gate (предпочтительно `SemaphoreSlim`/`AsyncManualResetEvent`, НЕ
>    sync `ManualResetEventSlim.Wait` — не блокировать тред зря; хотя ASR на выделенном треде, async чище). Методы
>    `Pause()`/`Resume()` + `IsPaused`. На границе чанка в consumer-loop (и при нужде producer) `await gate.WaitAsync(token)`
>    ПЕРЕД обработкой следующего чанка. **Cancellation-aware** (отмена во время паузы — чистый выход). Пауза НЕ зовёт
>    `Clear()`. Не ломать dual-ASR (два слота, `SubIndexSet`) и seek-restart (`Execute` при повторном вызове на новой
>    позиции делает `TryCancel(true)`+restore — продумать взаимодействие с паузой).
> 2. **Player/state:** выставить `IsASRPaused` рядом с `IsASRRunning`.
> 3. **UI:** тумблер Pause/Resume рядом с индикатором «ASR идёт» (найти, где рендерится `IsASRRunning` — «ASR chip»);
>    команда → `SubtitlesASR.Pause/Resume`.
> 4. **Скоуп:** интерактивный ASR (батч-ASR `BatchSubtitlesDialog` — отдельный пайплайн с no-overlap/CPU-fallback/трей —
>    отложить).
> **Риск:** frozen `media-runtime-contract` (ASR-threading) → тщательное ревью гонок + cancellation-correctness;
> вынести логику gate в тестируемый вид, где возможно. Гейты + `.exe` smoke на длинном видео (пауза → субтитры целы →
> resume продолжает).

### F-05 — Языковые предпочтения primary/secondary + авто-открытие 🟠 Ⓜ · ✅ **DONE (gap PR #58 v0.3.12 + аудит/фикс PR этот v0.3.14, 2026-06-27)**
> ⚠️ **Аудит 2026-06-27 (верификация):** per-slot primary/secondary language UI + config + per-slot логика
> **УЖЕ реализованы и подключены** (SettingsSubtitles.xaml ~`:663-708`, движок читает в `SubtitlesManager`/
> `SubtitlesOCR`/`SubtitlesTranslator`/`OpenSubtitles`). Остаток закрыт двумя частями.
> **✅ (б) Аудит авто-открытия (этот PR, v0.3.14):** per-slot fallback корректно подключён — `SubManager.Language`
> (`SubtitlesManager.cs:100`) для unknown-языка отдаёт `LanguageFallbackPrimary` (slot 0) / `LanguageFallbackSecondary`
> (slot 1); то же в `SubtitlesOCR.cs:147`, `SubtitlesTranslator.cs:81-82`. Авто-подбор субтитра идёт по приоритет-листу
> `Config.Subtitles.Languages` (`StreamSuggester.SuggestSubtitles`). **Найден и исправлен реальный латентный краш:**
> `StreamSuggester.SuggestBestExternalSubtitles` (`:142`) индексировал `Languages[0]` БЕЗ гарда → `IndexOutOfRangeException`,
> когда пользователь очистил список языков (это возможно: `SelectLanguageDialogVM.CmdMoveLeft` удаляет без min-1 гарда).
> Параллельный код `DecoderContext.Open.cs:826` тот же `[0]` уже гардит `Languages.Count > 0` — добавлен такой же гард
> (+ hoist `preferred` из цикла). Авто-подбор СЕКОНДАРИ-слота по отдельному языку — НЕ существует (конфиг имеет один
> `Languages`-лист); это потенциальная будущая фича, не баг.
> **✅ F-05-gap DONE (v0.3.12):** `BatchSubtitleConfigSnapshot.CreateSubtitlesConfig` теперь копирует все 5
> language-fallback полей (`Languages` deep-copy, `LanguageAutoDetect`, `LanguageFallbackPrimary`,
> `LanguageFallbackSecondary`, `LanguageFallbackSecondarySame`) под try/catch(NRE)→[English] (как `CloneAudioConfig`,
> т.к. ленивые геттеры зовут `GetSystemLanguages()`, который NRE'ит в headless). + focused regression-тест
> (RED-without-fix доказан) + **reflection-completeness guard** по всем скалярным settable-полям `SubtitlesConfig`
> (allow-list: `TranslateTargetLanguage`) — закрывает рекуррентный класс «батч-снапшот забыл поле». Тесты 237/237.
> **Известный смежный gap (НЕ в этом PR):** снапшот не копирует вложенный `DubbingConfig` (тот же класс; влияние
> ~нулевое для headless-батча, но молча) → отдельный follow-up.
**Решение (остаток):** аудит авто-подбора/открытия внешних субтитров (per-slot язык). **Рассуждение:** ядро
изучения языка; в upstream Roadmap «Now»; остаток мал.

### F-06 — Экспорт транскрипта в TXT / VTT 🟡 ⓢ-Ⓜ · ✅ **DONE (этот PR, v0.3.13, 2026-06-27)**
> ✅ **Закрыт.** Новый чистый тестируемый `FlyleafLib/MediaPlayer/SubtitleExporter.cs` (`Build(lines, format)` для
> Srt/Vtt/Txt + `SubtitleExportLine` record + `Extension`) — вынесен в FlyleafLib рядом с `SubtitleData`/`SubStyle`,
> чтобы покрыть юнит-тестами (у LLPlayer нет тест-проекта). TXT = только текст cue (без таймингов/разметки); VTT =
> `WEBVTT`-хедер + точечный мс-разделитель; SRT = как раньше (запятая, индексы). Выбор формата — ComboBox в
> `SubtitlesExportDialog.xaml` (`SelectedFormat`), SaveFileDialog filter/ext по формату. Старый
> `LLPlayer/Services/SrtExporter.cs` удалён (заменён). Тесты 250/250 (+13). Включает T-07 (см. ниже).

### F-07 — AI-summary / извлечение лексики из транскрипта 🟡 Ⓜ · ✅ **DONE (PR #67, merge `9467791`, v0.3.18, 2026-06-27)**
> ✅ **Закрыт.** Новое действие **AI Insights** (правый клик ▸ Subtitles ▸ AI Insights) суммаризирует транскрипт и/или
> извлекает ключевую лексику через сконфигурированный LLM, на целевом языке перевода. Чистый тестируемый слой
> `FlyleafLib/MediaPlayer/AI/`: `AiTranscript` (сборка + char-budget chunking по границе cue, >MaxChunks → сэмплинг +
> `PartialCoverage`), `AiInsightPrompts` (summary single/map/reduce + vocabulary, язык-параметризованы),
> `VocabularyParser` (устойчивый pipe-разбор `term|reading|translation|definition|example`, никогда не бросает, + Merge
> + ToTsv для Anki), `AiInsightService` (оркестратор map-reduce с **инъектируемым `ChatCompletion` делегатом** →
> map-reduce юнит-тестируем; `ForSettings` фабрика), `AiInsightServiceSelector`/`AiInsightLlmResolver` (выбор LLM:
> translate→word→первый usable; нет LLM → known-error, БЕЗ не-LLM фолбэка). `OpenAIBaseTranslateService` получил
> публичный `CompleteAsync` (переиспользует транспорт перевода, минует anti-loop retry) + чистый `ResolveMaxTokens`
> (translate-путь **byte-identical** при override==null; override шлёт только одно из max_tokens/max_completion_tokens +
> cloud/local для o-series; user-cap как floor). UI: `AiInsightsDialog` (VM+XAML), wiring App/AppActions/PopupMenu.
> **Без persisted-config** (переиспользует translate-LLM настройки). **Гарантии:** нет молчаливого truncation
> (finish_reason=length → known-error попап); длинные транскрипты (фильм 2ч+) → chunking+map-reduce. Гейты build 0/0,
> тесты **337/337** (+62), verify-frozen green, дизайн-панель (3+судья) + **11-агентное состязательное ревью** (3 находки
> исправлены: 2 important — `UriFormatException` в probe endpoint → not-configured, override→max_tokens для cloud o-series
> → max_completion_tokens; 1 nit — guard `CmdSave`; 2 ложные отсеяны), `.exe` launch-тест 0.3.18 чистый. Контракты
> product-behavior + wpf-design обновлены. **Persistence/Anki → F-10** (5-полевой `VocabularyEntry` — задел под него).
> Детали: второй мозг `Sessions/2026-06-27-handoff-f07-ai-insights.md`.

### F-08 — Хелпер синхронизации (shift-all / sync-to-current) 🟡 ⓢ-Ⓜ · ✅ **ALREADY DONE (верифицировано 2026-06-27 — отгружено в v0.3.17)**
> ✅ **Закрыт верификацией (кода не писали).** Многоагентный аудит этой сессии нашёл, что ОБЕ половины F-08 уже
> реализованы и подключены в UI: **sync-to-current** — `SubtitlesSidebarVM.CmdSubSync` (`:126`, `newDelay = CurTime -
> StartTime`) с per-row кнопкой в `SubtitlesSidebar.xaml:654`; **shift-all** — per-slot `Delay` как глобальный оффсет
> (`SubtitlesManager.SetCurrentTime:330`, `Config.cs:1098` + DelayAdd/Remove, `Commands.cs:19-31,233-242`, кейбайндинги,
> Reset, UI `PopupMenu.xaml:387-418` + Settings ▸ Keys ▸ Offset). Бэклог требовал ровно это («НЕ полный редактор»);
> деструктивный two-point rate-stretch явно вне скоупа. **Действие:** пометить DONE (опц. ручной smoke кнопки Sync).

### F-09 — Watch-folder авто-batch 🟢 ⓢ-Ⓜ · ✅ **DONE (PR #74, v0.3.22, 2026-06-27)**
> ✅ **Закрыт.** Opt-in режим слежения: батч-окно следит за выбранной папкой (рекурсивно при Recursive) и
> авто-обрабатывает новые видео по мере их появления. **Решение владельца (AskUserQuestion):** (1) **авто-старт в
> простое** (set-and-forget; новый файл → авто-добавляется + авто-запуск когда нет активного прогона, иначе очередь →
> drain после; предохранители Smooth/CPU-when-active берегут отзывчивость); (2) **watch пока окно открыто/в трее**
> (стоп на toggle-off / реальном close / quit). **Pure FlyleafLib seam** (юнит-тесты): `Utils.IsVideoExtension`
> (вынесен из `GetMoviesSorted`, один source), `Batch/WatchFolderPolicy.ShouldEnqueue` (video+dedup+output-exists→enum),
> `Batch/FileReadiness` (`FileStabilityState`+`Step`/`IsReady`, 2 стабильных тика). **Тонкий `LLPlayer/Services/
> BatchFolderWatcher.cs`:** FileSystemWatcher (Created+Renamed, IncludeSubdirectories, NotifyFilter FileName|Size|
> LastWrite, буфер 64KB) + DispatcherTimer 1s; **всё марш на UI-thread** (`PostToUi`, без локов); готовность = size+mtime
> стабильны + open-for-read; **partial-файлы (.part/.tmp) отсекаются** (не video-расширение); **InternalBufferOverflow
> рекаверится re-enum'ом, без teardown**; FileReady/Error на UI. VM: `WatchFolder` (in-session `_watchFolder` —
> авторитет поведения; live-config трогаем только при user-toggle → транзиентный сбой НЕ персистит OFF); `_watchQueued`
> (только watch-приходы авто-стартуют — scan-backlog и post-Cancel не трогаются); drain в `RunAsync.finally`
> (BeginInvoke, без реентрантности); `CanCloseDialog` → minimize-to-tray при watch. Config `WatchFolder` (default OFF,
> additive). UI: чекбокс + 👁-индикатор в summary. **Гарантии:** OFF-path byte-identical; нет double-run (guard IsRunning);
> watch выживает tray-minimize, гибнет на close/toggle/quit. Гейты build `-warnaserror` **0/0 ×3** + тесты **434/434**
> (+24) + verify.ps1 (env/plugin/doc-coverage/frozen) green. Дизайн-панель (3 дизайнера; судья упал на схеме → синтез
> вручную) → **adversarial review (5 линз+триаж): FIX-THEN-SHIP, 3 important исправлены** (idle-watch-гибнет-при-close;
> auto-start мёл все Pending; recoverable FSW-error глушил+персистил OFF) + **verify-агент нашёл 4-й** (live-config leak
> в persist:false-пути) → исправлен. `.exe` launch-test 0.3.22 чистый. Контракты product-behavior + config-data.
> Детали: второй мозг `Sessions/2026-06-27-handoff-f09-watch-folder.md`.

**Идея от Buzz.** Расширение существующего батча (`Batch*`-классы). **Решение:** режим слежения за папкой →
авто-обработка новых файлов. **Рассуждение:** низкий effort, удобство для пакетной обработки.

### F-10 — Anki-интеграция / Word Management 🟢 Ⓛ · ✅ **DONE (PR #79, merge `7ee5ac5`, v0.3.24, 2026-06-27)**
> ✅ **Закрыт.** Персистентный кумулятивный список слов `LLPlayer.WordList.json` (рядом с .exe; аддитивно — нет файла =
> пустой список, OFF-путь byte-identical) + экспорт в Anki **тремя путями**. **Источники (оба, AskUserQuestion):** кнопка
> **Save** в попапе перевода слова при просмотре (термин + показанный перевод + реплику-пример + языки; honor `IsTranslated`;
> Reading/Definition пусты → правятся позже; guard `IsLoading` против пустого перевода) + кнопка **Add to List** в AI Insights
> (bulk из LLM-лексики). Дедуп по Term (case-insensitive, first-wins). **Word Manager** (ПКМ ▸ Subtitles ▸ Word Manager):
> DataGrid (Term ro, 4 поля in-place с write-through), фильтр, delete (gated), Clear All, имя колоды; **живое обновление**
> грида при внешних add'ах (`WordListStore.Changed` + подписка с suppress-guard). **Экспорт** общей 5-полевой моделью:
> TSV (переиспользует `VocabularyParser.ToTsv`); **.apkg** (SQLite `collection.anki2`+zip; genanki-совместимо: guid
> base91(sha256), csum sha1[:8], flds U+001F, model/deck/conf/dconf JSON; **fail-soft**); **AnkiConnect** live-пуш
> (localhost:8765; createDeck+createModel → самодостаточно; ошибка createModel парсится, гасится только «already exists»).
> **Архитектура:** чистая логика в `FlyleafLib/MediaPlayer/AI/` (`SavedWord`, `WordListStore`, `AnkiApkgModel`,
> `AnkiConnectRequests`, `AnkiConnectResponses` — все юнит-тестируемы); WPF-плумбинг (SQLite/HTTP/диалог) в `LLPlayer`
> (`AnkiApkgWriter`, `AnkiConnectSender`, `WordManagerDialog`). Зависимость `Microsoft.Data.Sqlite 9.0.17` +
> `SQLitePCLRaw.bundle_e_sqlite3 3.0.3` (патч против GHSA-2m69-gcr7-jv3q; native `e_sqlite3.dll` копируется в publish).
> Дизайн-панель (3) + **adversarial review (5 линз)** → все подтверждённые находки исправлены (2 HIGH отсеяны как ложные
> после сверки с genanki: `col.mod` в мс и guid на SHA-256 — оба верны). Гейты build `-warnaserror` **0/0 ×3** + тесты
> **548/548** (+49) + verify.ps1 green; **`.apkg` структурно провалидирован реальным SQLite**; `.exe` launch-тест 0.3.24
> чистый (e_sqlite3.dll присутствует). Контракты product-behavior/wpf-design/config-data/dependency-baseline/manual-smoke
> аддитивно. **Owner manual-smoke:** импорт TSV/.apkg в Anki + AnkiConnect-пуш. Детали: второй мозг
> `Sessions/2026-06-27-handoff-f10-word-management.md`.
**Решение:** персистентные списки слов, экспорт в Anki-колоды/SRS. **Рассуждение:** высокий mission-fit,
крупно; строится на F-07.

### F-11 — Dictionary API (англ./яп. и др.) 🟢 Ⓛ · ✅ **DONE (PR #82, merge `58be9bd`, v0.3.25, 2026-06-28)**
> ✅ **Закрыт.** Опц. `Subtitles.WordDefinitionServiceType` {Off,Auto,DictionaryApi,Llm} (default OFF →
> byte-identical). При включении попап перевода слова показывает **словарное определение** третьей строкой под
> переводом и при **Save** авто-заполняет Anki-поля `Reading`/`Definition` (заполняет ровно те поля, что F-10
> оставляла пустыми). **Решения владельца (AskUserQuestion):** провайдер = «Оба» (`Auto` — английское слово →
> бесплатный **dictionaryapi.dev**; иной язык → настроенный **LLM** на target-языке; английское слово, отсутствующее
> в словаре → **LLM-fallback** при наличии LLM) + авто-заполнение Anki = «Да». **Чистая логика → FlyleafLib/MediaPlayer/AI/**
> (`WordDefinitionModels`/`DictionaryApiParser`/`WordDefinitionPrompts`/`WordDefinitionSelector`/`WordDefinitionService`,
> все юнит-тестируемы; парсер зеркалит `ParseGoogleV1`, 404-объект/пусто/мусор → `Empty` без throw; LLM через
> инъектируемый делегат как F-07) + **тонкая WPF → LLPlayer** (`WordPopup` 3-я строка, параллельный fetch под общим
> `_cts`, кэш на жизнь попапа; дропдаун в Settings ▸ Subtitles ▸ Word Action). Переиспользует `AiInsightLlmResolver`/
> `CompleteAsync` — без нового LLM-конфига. **Fail-soft:** 404/таймаут/ошибка LLM/нет LLM → строка скрыта, никогда
> не модал; cancellation пробрасывается; Save ждёт in-flight определение. **Монолингв (source==target) полезен →
> same-language НЕ пропускается.** Config additive/string/без миграции, зеркалится в `BatchSubtitleConfigSnapshot`.
> Дизайн-панель (3) + **adversarial-ревью (5 линз): 4 ship + 1 fix-then-ship** → исправлены UriFormatException
> fail-soft hole, Save-during-definition race, same-language guard, вынос `AllowLlmFallback` в чистый селектор,
> try-обёртка/gate/orphan-observe. Гейты build `-warnaserror` **0/0 ×3** + тесты **607/607** (+59) + verify.ps1 green;
> `/code-review high` → Approve; **`.exe` launch 0.3.25 чистый**. Контракты product-behavior/config-data/wpf-design/
> manual-smoke аддитивно. **Owner manual-smoke:** Definition Source=Auto → клик по EN-слову → строка + Save заполняет
> Anki-поля; слово вне словаря → только перевод. Детали: второй мозг `Sessions/2026-06-28-handoff-f11-dictionary.md`.
**Заодно закрывает реалистичное ядро F-15** (литеральный Yomitan-бридж невозможен аддитивно; «определение слова в
попапе» = то же действие).

### F-12 — A-B повтор ✅ **DONE (v0.3.27, 2026-06-28)** + аудио-waveform ✅ **DONE (PR этот, v0.3.28, 2026-06-28)**
> ✅ **Waveform-половина отгружена (v0.3.28).** Opt-in визуализация аудио-огибающей за сикбаром (тумблер `AppConfig.
> ShowWaveform`, **default OFF → byte-identical**). При включении с открытым локальным файлом аудио декодируется один
> раз в **фоновом worker'е** через `WaveformReader` (свой изолированный `Demuxer`+`AudioDecoder`+`SwrContext` —
> паттерн ASR `AudioReader`, 2-й `avformat_open_input`; НЕ трогает играющий пайплайн), ресэмплится в S16 mono 16kHz
> и сворачивается в **чистый `WaveformPeakBuilder`** (`FlyleafLib/Utils/WaveformPeaks.cs`, PTS-bucketing, max-abs,
> 17 юнит-тестов) → рендерится `Path`/`StreamGeometry` (`WaveformGeometryConverter`, auto-gain) в `Canvas` ПЕРЕД
> слайдером (z-order под треком; `AbOverlay` цел). State на `Player.Waveform.cs` (`WaveformPeaks`/`WaveformActive`/
> `WaveformEnabled`; cancel-and-replace, reset в `ResetMe`, триггер в `Decoder_OpenAudioStreamCompleted`); тумблер
> в баре рядом с A-B. **Skip live/HLS/no-audio/unknown-duration; fail-soft на ошибке декода (нет оверлея, не модал).**
> CTS-владение: worker — единственный диспозер своего CTS (swap/clear/reset только Cancel) → нет ODE-гонки.
> Дизайн-панель (3 линзы) + adversarial-ревью (5 линз: native-memory/threading/correctness/additive/ui — **все SHIP**;
> 1 minor token.Register-ODE + 1 ui-nit unused-progress-props исправлены; nits Reduce-doc учтён). Гейты build
> `-warnaserror` **0/0 ×3** + тесты **803→820 (+17)** + verify.ps1 green; **`.exe` launch 0.3.28 чистый** (бар рендерится,
> без crash.log). Контракты product-behavior/media-runtime/wpf-design/config-data + manual-smoke аддитивно. **Owner
> manual-smoke:** тумблер Waveform → огибающая за сикбаром на локальном файле; смена файла → ребилд; no-audio/live → нет
> waveform. Детали: второй мозг `Sessions/2026-06-28-handoff-f12-waveform.md`. **F-12 полностью закрыта.**
> ✅ **A-B повтор отгружен (v0.3.27).** Пользователь ставит точки A и B во время воспроизведения; плеер зацикливает
> отрезок [A,B] (frame-accurate seek назад к A при достижении B) до сброса. **OFF byte-identical** (нет точек →
> поведение прежнее). Pure тестируемый `FlyleafLib/MediaPlayer/AbLoop.cs` (20 юнит-тестов); состояние — `Volatile.
> Read/Write` по двум `long` на `Player` (`volatile long` запрещён C# CS0677). Loop-back hook ВНУТРИ `UpdateCurTime`
> ПОСЛЕ `lock(seeks)` (покрывает все screamer'ы, в т.ч. audio-only; нет seek-storm — гард `seeks.IsEmpty`) →
> переиспользует готовый thread-safe `SeekAccurate` (без новых локов, frozen media-runtime соблюдён); EOF-guard в
> `Status.Ended` (A-B приоритетнее whole-file loop, snapshot против TOCTOU); reset в `ResetMe`; skip при reverse/
> HLS-live. Хоткеи — движковый `KeyBindingAction` (`ABLoopSetStart/End/Clear/Toggle`, unbound по умолчанию, группа
> Playback, авто в CheatSheet/Command Palette). UI: кнопка A-B в баре (cycle/ContextMenu/lit-active) + маркеры A/B +
> полоса на сикбаре (отдельный `Canvas`-оверлей + 2 конвертера, буфер-`IsSelectionRangeEnabled` цел). Дизайн-панель
> (3 линзы) + **adversarial-ревью (5 линз: все SHIP; 1 MINOR исправлен — TOCTOU в EOF-guard → snapshot; 2 NIT
> отклонены)**. Гейты build `-warnaserror` **0/0 ×3** + тесты **803/803** (+20) + verify.ps1 green; `/code-review high`
> → Approve. Заодно исправлен пред-существующий флак `BatchSubtitlePolicyTests` (был не self-contained — `Utils.
> IsTesting` в ctor; падал в изоляции; новый тест-файл сдвинул порядок xUnit и вскрыл). Контракты product-behavior/
> media-runtime/config-data/wpf-design/manual-smoke аддитивно. **Waveform-визуализация — НЕ в этом срезе** (тяжёлая
> offline-декод половина: отдельный декод-проход + peak-reducer + рендер — отдельная сессия). Детали: второй мозг
> `Sessions/2026-06-28-handoff-f12-ab-loop.md`.
**Идея от SubtitleEdit.** **Остаток (waveform):** рендер waveform из аудио FlyleafLib для визуального sync.
**Рассуждение:** A-B повтор — заметный single-session UX-win для изучения языка; waveform — крупный effort, не топ.

### F-13 — Кросс-платформенность (Avalonia, Linux/Mac) 🟢 ⓍⓁ · DEFERRED · (upstream «Future»)
SE5 и Buzz уже кросс-платформенны → наш Windows-only = конкурентный минус. **Решение:** порт UI на Avalonia
(движок FlyleafLib + WPF-слой). **Рассуждение:** огромная работа (фактически переписывание UI), стратегическая
цель; не трогать инцидентно.

### F-14 — Расширенный локальный поиск субтитров 🟢 ⓢ-Ⓜ · ✅ **DONE (PR #71, merge `3c4107d`, v0.3.20, 2026-06-27)**
> ✅ **Закрыт.** Поиск сайдбара получил 3 тумблера: **match case / whole word / regex** (next/prev/clear/hit-count
> уже были). Чистый тестируемый `FlyleafLib/Utils/SubtitleSearcher.cs` (`TryCreate`/`IsMatch`): дефолт всех опций OFF =
> case-insensitive подстрока **byte-identical** прежнему `SubFilter`; match-case→`Ordinal`; whole-word→`\b…\b`
> (Unicode, кириллица); regex verbatim, whole-word+regex→`\b(?:…)\b`; **regex с match-timeout 100мс против ReDoS**;
> невалидный паттерн→`null`→UI «Invalid regex». 3 персист-тумблера в `AppConfig` (`SidebarSearch{MatchCase,WholeWord,
> Regex}`, дефолт false, аддитивно); VM строит matcher в `ApplyFilter`; 3 `ToggleButton` в `SubtitlesSidebar.xaml`.
> **Скоуп — per-slot поиск** (cross-track merge намеренно вне scope). Гейты 0/0 ×3, тесты **393/393** (+30),
> **4-линзовое adversarial review SHIP-READY 0 Crit** (1 important исправлен: тумблер опции при dual-sub оставлял
> невидимый слот со устаревшим фильтром → инвалидация кэша), `.exe` launch-clean. owner-smoke: видимость checked-state
> тумблеров при 24×24. Детали: второй мозг `Sessions/2026-06-27-handoff-f14-subtitle-search.md`.

### F-15 — Yomitan / 10ten в плеере 🟡 Ⓜ-Ⓛ · ✅ **DONE-BY-F-11 (решение владельца 2026-06-28)** · ([upstream issue #13](https://github.com/umlx5h/LLPlayer/issues/13))
> ✅ **Закрыто как реализованное F-11.** Реалистичное ядро — «словарное определение слова в попапе» — отгружено F-11
> (`WordDefinitionServiceType` {Off,Auto,DictionaryApi,Llm}, v0.3.25); clipboard-авто-копирование уже влито upstream
> (`1db5a76`). Литеральный браузер-мост к Yomitan/10ten аддитивно невозможен (WebExtension не читает WPF-текст; нужен
> WebView2 + упакованное расширение = крупно/fragile/multi-session, ломает frozen WPF). Многоагентная верификация
> (high-confidence, 2026-06-28) + решение владельца (AskUserQuestion) → DONE-BY-F-11. Если позже понадобится именно
> браузерное расширение — заводить отдельной крупной задачей.
Сейчас словарь — через попап перевода слова (F-11) и буфер обмена (FAQ).

### F-16 — Дубляж: расширение голосов/качества (фазы 1-6) 🟢 Ⓛ · IN-PROGRESS (фаза 1 voice-bank ✅ PR #93 v0.3.30; фаза 2 custom voice-ID ✅ PR #96 v0.3.31; фаза 2a per-line voice ✅ PR #106 v0.3.35; остаток фаз 2-6 TODO)
> ⚙️ **Фаза 1 (voice-bank) — срез отгружен (PR #93, merge `526a1a3`, v0.3.30, 2026-06-28).** Пользователь
> выбирает **голос дубляжа** (банк пресетов). Аддитивно/opt-in; default (`DefaultVoiceId=ru-preset-1`, дубляж
> выкл.) **byte-identical**. **Pure GPU-free `FlyleafLib/MediaPlayer/Dubbing/VoiceBankResolver.cs`**: `BuiltIn`
> (read-only, зеркало `dub_sidecar/server.py` VOICES — M/F пресеты) + `Resolve`/`ForConfig` (custom-id →
> плейсхолдер, дропдаун не пустеет/не перезаписывает кастом) + `ResolveAsync(ITtsService)` (phase-2 merge-seam,
> fail-soft, built-in metadata wins, **не стартует sidecar**, пока не в UI). UI: **новая секция Settings ▸
> Subtitles ▸ Dubbing** (голос + ducking + atempo + формат=FLAC) + дропдаун голоса в батч-диалоге рядом с
> «Generate Russian dub» (пишет `DefaultVoiceId`, рендерер читает живьём). `DuckingPercent` клампится 0..100.
> **Фаза 2 (НЕ в срезе):** per-line / per-speaker выбор + diarization-gender (нужны per-line данные); AAC/m4a
> энкод в sidecar (сейчас m4a деградирует в FLAC → в UI только FLAC); pre-render доп. пресет-голосов = owner
> first-run на GPU. Дизайн-панель (3) + adversarial (4 линзы: 3 SHIP + 1 fix-then-ship → 5 фиксов) +
> `/code-review high` Approve. Гейты build `-warnaserror` **0/0 ×3** + тесты **845/845** (+25) + verify.ps1 green;
> `.exe` launch 0.3.30 чистый. Контракты wpf-design/dubbing/dubbing-roadmap аддитивно. Детали: второй мозг
> `Sessions/2026-06-28-handoff-f16-voice-bank.md`.
> ⚙️ **Фаза 2 (частично) — custom voice-ID отгружен (PR [#96](https://github.com/Gorgutc/LLPlayer_ru/pull/96), merge `58e4320`, v0.3.31, 2026-06-28).** Пользователь регистрирует **кастомные voice-ID** (добавленные в локальный
> `dub_sidecar/server.py` VOICES) в Settings ▸ Subtitles ▸ Dubbing ▸ **Custom voice IDs** (ListBox + Add/Remove)
> → они появляются в пикере голоса (Settings + батч-диалог) и доходят до движка как `DefaultVoiceId` →
> `TtsRequest.VoiceId`. Аддитивно/opt-in, пустой список (default) → **byte-identical**. `DubbingConfig.CustomVoiceIds`
> (`List<string>`) + `VoiceBankResolver.ForConfig(selected, customVoiceIds)` overload (merge после банка:
> trim/dedup-ci/declared-order; selected остаётся placeholder; пустой → тот же `BuiltIn`-инстанс) + фабрика
> `CustomVoice(id)`. `ObservableCollection Voices` мутируется **хирургически (без Clear)** → two-way `SelectedValue`
> не бланкуется. GPU-free, **не стартует sidecar** для пикера. **Развилка (AskUserQuestion):** буквальный «Refresh
> voices from engine» = hollow (стартует GPU-движок ради зеркала банка + не launch-проверяем off-GPU) → выбран
> custom-ID список. Adversarial-ревью (4 линзы): **CRITICAL** — батч-пикер не передавал custom-ID → исправлено;
> DRY-фабрика. Гейты build `-warnaserror` **0/0** + тесты **926/926** (+11) + verify.ps1 green; `/code-review`
> Approve; **`.exe` launch 0.3.31 чистый**. Контракты wpf-design/dubbing/dubbing-roadmap (фаза 2) аддитивно.
> **Остаток фазы 2:** per-line / per-speaker выбор + diarization-gender (нужен F-03 + per-line данные), AAC/m4a
> энкод в sidecar (Python+GPU), pre-render доп. голосов + live-discovery `ResolveAsync` refresh (= owner GPU
> first-run). Детали: второй мозг `Sessions/2026-06-28-handoff-f16-custom-voices.md`.
> ⚙️ **Фаза 2a (per-line voice override) — отгружена (PR [#106](https://github.com/Gorgutc/LLPlayer_ru/pull/106), merge `acafd388`, v0.3.35, 2026-06-30).** Пользователь назначает **отдельный голос дубляжа на строку субтитра**
> из per-row кнопки в боковой панели субтитров. Аддитивно/opt-in, default (нет назначений) → **byte-identical**
> (сайдкар уже принимает per-line `voice_id` → Python не тронут). Inert per-cue `SubtitleData.AssignedVoiceId`
> (`string?`, notifying, default null) по паттерну `Language`/`SpeakerId` → копируется в `Clone()` + на split-cue
> обоих re-seg сайтов; `DubbingLine.VoiceId`; `DubbingRenderer.BuildLines` (→internal, trim→null) +
> `ResolveVoiceId(line.VoiceId, _voiceId)` fallback к снапшоту `DefaultVoiceId`. UI: per-row voice-кнопка
> (`AccountVoice`, `Button`+`ContextMenu` банка `VoiceBankResolver.ForConfig` + «Use default voice»; out-of-tree
> меню достаёт VM через `Tag`=VM + `PlacementTarget`) + VM `DubVoiceMenuItems`/`CmdSubSetVoice` + подписка на
> `DubbingConfig`. **Override interactive/in-memory only** — `SubtitleData` не сериализуется, а батч-дубляж читает
> `.ru.srt` файл → override теряется при re-рендере из готового srt (документировано; companion-json persistence =
> follow-up). Контракты dubbing/wpf-design/media-runtime аддитивно; config-data НЕ тронут (поле runtime).
> **Решения владельца (AskUserQuestion ×3):** seam+per-row UI / кнопка в сайдбаре / interactive-only. Многоагентно:
> верификация (8) → дизайн (4) → adversarial-ревью (11 агентов, 5 линз: **SHIP, 0 critical/important**;
> byte-identical+dataflow 0 находок; 4 lifecycle/UI = false-positive/nit; 2 doc-fix). Гейты build `-warnaserror`
> **0/0 ×3** + тесты **1063/1063** (+14) + verify.ps1 green; `.exe` launch 0.3.35 чистый. **2026-07-01 monitor follow-up:** batch dubbing теперь применяет current-session `DubbingVoiceAssignmentMap` к свежим субтитрам и existing `.ru.srt` render-only path; `DubbingConfig.DefaultVoiceId`/`CustomVoiceIds` нормализуются fail-closed; packaging checks усилены positive content validation + recursive `DubEngine`/`dubmodels` rejection. Тесты **1079/1079** (+16). **Остаток:** per-speaker
> (нужен F-03 диаризация), AAC-энкод, pre-render голосов, companion-json — GPU/follow-up. Детали: второй мозг
> `Sessions/2026-06-30-handoff-f16-perline-voice.md`.
Дубляж — Phase 0 (PR #35 влит, CosyVoice2 в `dub_sidecar/`). SE предлагает много TTS (Edge/Kokoro/OmniVoice
voice-cloning). **Решение:** фазы 1-6 из [[2026-06-23-handoff-dubbing-mvp]] (мульти-голос, качество,
diarization-aware). **Рассуждение:** крупно; держать как продолжение существующей фичи.

---

## 3. 🧰 ТЕХДОЛГ / ИНФРАСТРУКТУРА / МЕЛКИЕ TODO

### T-01 — Рассинхрон FFmpeg-биндингов (8.0.1 vs 7.1.1) 🟠 Ⓜ · ✅ **DONE (этот PR, v0.3.12, 2026-06-27)**
> ✅ **Закрыт up-align'ом FlyleafLib 7.1.1→8.0.1.** ⚠️ **Премиса верификатора была НЕВЕРНА** («отгружаемые DLL =
> FFmpeg 7.x» → down-align). Проверка по коду: tracked DLL в `FFmpeg/` = **FFmpeg 8.0** (`avcodec-62`/`avutil-60`/
> `avformat-62`/`avfilter-11`/`swscale-9`/`swresample-6`/`avdevice-62`; release-action их и копирует). Central package
> management нет → NuGet unify конфликтующих ссылок **вверх до 8.0.1**, т.е. реально отгружаемый managed-binding уже
> = 8.0.1 и КОРРЕКТНО совпадал с 8.0 DLL; FFmpeg-interop (159+, `Globals.cs` global usings, hw-ctx) — в FlyleafLib,
> `LLPlayer` юзает binding только для managed-енумов `LoadProfile`/`LogLevel` (не P/Invoke). **Down-align форсировал
> бы unify ВНИЗ к 7.1.1 против 8.0 DLL = реальный mismatch** → отвергнут (решение владельца через AskUserQuestion).
> **Up-align** выравнивает compile-time ref FlyleafLib под рантайм-unify + 8.0 DLL: `FlyleafLib.csproj:40` 7.1.1→8.0.1,
> гейт `verify-frozen.ps1:222` 7.1.1→8.0.1, `dependency-baseline.md` (таблица + секция «alignment»). Эмпирически
> проверено: build `-warnaserror` FlyleafLib **0/0** + LLPlayer+WpfColorFontDialog **0/0** против 8.0.1 (исходники
> компилятся чисто; бинарная совместимость и так доказана работающим .exe). Гейты verify.ps1 0/0 + тесты 237/237.
> **Остаток:** ручной playback-smoke `.exe` (на владельце/при публикации) — по frozen dep-правилу.

### T-02 — Ранняя диагностика VC++ Redistributable 🟠 ⓢ-Ⓜ · ✅ **DONE (PR #62, merge `296c248`, v0.3.15, 2026-06-27)**
> ✅ **Закрыт.** Новый чистый тестируемый `FlyleafLib/Utils/VcRedistChecker.cs` — probe `vcruntime140.dll`/
> `vcruntime140_1.dll`/`msvcp140.dll` через `NativeLibrary.TryLoad` (loader-search = ровно то, что резолвит
> нативка). Детектим **отсутствие** современного CRT (крэш-кейс), НЕ строгую версию — строгий гейт «2022» дал бы
> false-positive на рабочем VC++ 2019+ (redistributable кумулятивен). Preflight в 3 in-process точках:
> `SubtitlesASR.CanExecute` (whisper.cpp), `TesseractOCRService.TryInitialize`, `BatchAsrTranscriber.ValidateAsrConfig`;
> **faster-whisper** (внешний exe) и **Microsoft OCR** (WinRT) НЕ гейтятся. UI: интерактивный whisper.cpp ASR →
> non-blocking «INSTALL» snackbar (`KnownErrorActionKeys.InstallVcRedist` → `AppActions.OpenVcRedistDownload`);
> OCR/batch → modal с URL. Гейты build 0/0, тесты **262/262** (+11), verify-frozen/doc-coverage green, 5-линзовое
> adversarial `/code-review` (19 агентов; 2 critical+1 important разобраны: обновлён frozen `product-behavior-contract`,
> переписан вводящий в заблуждение комментарий), `.exe` launch-тест 0.3.15 чистый. **Follow-up (defer):** батч кидает
> VC++-ошибку в ctor → модал, а не снэкбар (UX-асимметрия; строго лучше прежнего краша). Детали: второй мозг
> `Sessions/2026-06-27-handoff-t02-vcredist-preflight.md`.
Без VC++ 2022+ приложение стартует, но падает при включении ASR/OCR (README/FAQ). **Решение:** усилить
раннюю диагностику/понятное сообщение до включения ASR/OCR. **Рассуждение:** молчаливый краш = плохой UX.

### T-03 — Расширение тестового покрытия 🟡 Ⓜ · ONGOING
**Тесты: 1079/1079** (на 2026-07-01: monitor follow-up добавил +16 регрессов для `DubbingConfig` normalization, `DubbingVoiceAssignmentMap`, и batch dubbing per-line voice bridge → 1079; на 2026-06-30 (поздн.): clean-up находок Codex PR #104 — корневой фикс whitespace-blank пикера голоса дубляжа через trim `DubbingConfig.DefaultVoiceId` на set + закрытие 4 тест-пробелов `VoiceBankResolver` (+7 → 1049, v0.3.34); adversarial-ревью отвергло первый вариант (raw-append в `ForConfig` вносил on-refresh-blank через `ContainsVoiceId`-дифф); на 2026-06-30 (ранее): monitor follow-up добавил +4 регресса для `DubbingConfig.CustomVoiceIds` null-normalization и `VoiceBankResolver.ContainsVoiceId` → 1042; на 2026-06-29 после F-03 prep SpeakerId PR #102 +3 и T-10 per-segment language +9 → 1038; на 2026-06-28 после T-03-среза №4 PR #98 мапперы/SSA/snapshot/Utils +100 → 1026; T-03-срез №3 PR #95 language-мапперы +70 → 915, затем F-16 ф.2 PR #96 +11 → 926; промежуточно 783→845 за счёт НЕ-T-03 срезов F-12 waveform +17 → 820 и F-16 ф.1 +25 → 845. Ранее: F-10 PR #79 → 548, F-11 PR #82 +59 → 607, T-03-срез PR #85 +114 → 721, PR #86 +4 → 725, PR #88 +58 → 783). Крупные области ещё без юнитов.
**Решение:** покрыть парсинг субтитров, перевод (моки сети), ASR/OCR (где детерминируемо),
playlist/demuxer-утилиты. Связано с фиксами B-01/B-02/B-03 (добавить регресс).
> **Прогресс 2026-06-28 (PR #98, +100 тестов → 1026, tests-only):** покрыты ранее непокрытые
> ПУБЛИЧНЫЕ/internal-seam чистые функции 4 областей (ожидания ИЗ КОДА):
> **(1) Переводческие мапперы** — `GoogleV1TranslateService`/`MicrosoftTranslateServiceBase`
> `ToSourceCode` (instance: default-region, user-override, **non-region-override-ignore**) +
> `ToTargetCode` (static): спец-кейсы `nb→no`, `lg→lug`, `mn→mn-Cyrl`, `ny→nya`, `rn→run`,
> `sr→sr-Latn`, `EnglishAmerican→en` (default-ветка через `ToISO6391`) →
> `FlyleafLibTests/MediaPlayer/Translation/TranslateLanguageCodeMapperTests.cs`. **Seam:**
> `private`→`internal` на 4 методах (byte-identical, зеркало DeepL, `InternalsVisibleTo` уже есть).
> **(2) `ParseSubtitles.SSAtoSubStyles`** — bold/italic/underline/strikeout/color (BGR-hex `&HBBGGRR`,
> порядок каналов, ветки short-hex / close-without-open / 2-digit), dialogue `,,`+`\N`+trim,
> literal-backslash, greek-fixup, overlapping styles → `MediaFramework/MediaFrame/ParseSubtitlesTests.cs`
> (throwing-edge-кейсы намеренно вне scope). **(3) `BatchSubtitleConfigSnapshot`** — clone-независимость
> коллекций/словарей/плагинов/nested + `--task` стриппинг + force-Russian (комплемент к scalar-completeness
> guard, который намеренно исключает коллекции/nested) → `MediaPlayer/Batch/BatchSubtitleConfigSnapshotTests.cs`.
> **(4) `Utils`** — конвертеры цветов WinForms/WPF/Vortice/`VideoColor` + `FixFileUrl` (file:-URI→LocalPath,
> %-decode, case-insensitive, passthrough) → `Utils/UtilsColorConversionTests.cs` + `UtilsFixFileUrlTests.cs`.
> Многоагентно: верификация бэклога (8) → adversarial-ревью (5 линз: correctness/non-vacuity/brittleness/
> coverage/seam-safety → **SHIP**, 0 mustFix; ложные отсеяны — Regions предзаполнен DefaultRegions, не пуст) →
> триаж (+7 coverage-тестов: G/B-каналы MidGray, SSA color-ветки, non-region-ignore) → `/code-review high`
> Approve. Гейты build `-warnaserror` **0/0** + verify.ps1 green + **1026/1026**; tests-only → без бампа/launch.
> **Грабли:** `ToSourceCode` — instance (зависит от `_settings.Regions`) → тест через сконструированный сервис
> (HttpClient на shared handler, сеть не трогается); `VideoColor`=`Vortice.Direct3D11.VideoColor`; ⚠️ тест-файлы
> в WORKTREE-путь; `-warnaserror` на тест-проекте всплывает pre-existing xUnit1051 (PauseTokenSourceTests) —
> норм-`dotnet test` их терпит. Остаётся ONGOING (Google/MS мапперы теперь покрыты; demuxer/OCR-утилиты открыты).
> **Прогресс 2026-06-28 (PR #95, +70 тестов → 915, tests-only):** покрыты ранее непокрытые
> ПУБЛИЧНЫЕ чистые language-мапперы движка (ожидания ИЗ КОДА) → `FlyleafLibTests/Engine/LanguageMapperTests.cs`:
> `TesseractModel.TesseractLangToISO6391` (105 активных записей: count-tripwire, spot-маппинги,
> `zh`×2 / `zz`×1, форма «2 строчные буквы», все ключи — определённые enum-значения);
> `WhisperLanguage.LanguageToCode`/`GetWhisperLanguages` (reverse-map 100, case-insensitive,
> title-case `UpperFirstOfWords` вкл. «Haitian Creole», round-trip между фасадами, сортировка,
> distinct codes); `Language.ISO639_2T_TO_2B`/`ISO639_2B_TO_2T` (биекция 20×2 + spot); `Language.StringToCulture`
> (guard-ветки + fallback-скан по EnglishName через exception-path); `Language.ThreeLetterToCulture`
> (zht→zh-Hant/pob→pt-BR/tgl→fil). **Culture/ICU-fragile ветки (nor→nob/scc→srp, `GetCultureInfo` для
> 3-letter) намеренно НЕ ассертятся** (стабильность CI .NET 10.x ↔ локаль .NET 11-preview). Многоагентно:
> верификация (workflow 5 Explore) → adversarial-ревью (workflow 4 линзы) → триаж (2 coverage-доп приняты:
> fallback-scan + tgl; brittleness-«critical» = не дефект теста — prod OrderBy и BeInAscendingOrder делят
> один ambient comparer; `char.IsUpper(char,Culture)` перегрузки НЕТ → галлюцинация, отклонено;
> cardinality-tripwires оставлены) → `/code-review high` Approve. Гейты build `-warnaserror` **0/0** +
> verify.ps1 green + **915/915**; tests-only → без бампа версии/launch. **Возможный prod follow-up:**
> `WhisperLanguage.GetWhisperLanguages` `OrderBy` без `StringComparer.InvariantCulture` (несогласовано с
> `Language.AllLanguages`; безвреден для ASCII-имён). Остаётся ONGOING (demuxer/playlist/OCR-карты Tesseract
> уже покрыты частично; Google/Microsoft translate-мапперы private → нужен seam).
> **Прогресс 2026-06-28 (PR #88, +58 тестов → 783, tests-only):** покрыты ранее непокрытые ПУБЛИЧНЫЕ
> детерминированные функции: `TranslateServiceTypeExtensions` (`IsLLM` одиночные + combined-flags,
> `LLMServices` cardinality-tripwire + membership, `DefaultSettings` enum→конкретный settings-тип через
> `typeof`-InlineData + throw на undefined) → `FlyleafLibTests/MediaPlayer/Translation/TranslateServiceTypeExtensionsTests.cs`;
> `M3UPlaylist.ParseFromString` (EXTINF/Title, теги `key="value"`, `[Geo-blocked]`/`[Not 24/7]`, `(NNNp)` height,
> `#EXTVLCOPT` UA/referrer, мульти/пусто/leading-ws/EOF→null/empty-title→""/tag-value-с-пробелом-отброшен) →
> `MediaFramework/MediaPlaylist/M3UPlaylistTests.cs`; `PLSPlaylist.Parse` (Win32 `GetPrivateProfileString`, temp
> `.pls` ASCII/CRLF, cleanup в Dispose; `NumberOfEntries` cap-больше/меньше/0/-1, File/Title/Length,
> break-on-missing, `GetINIAttribute` present/missing) → `MediaFramework/MediaPlaylist/PLSPlaylistTests.cs`.
> Ожидания выведены ИЗ КОДА (трасса парсеров / .NET Regex no-match quirk `Match.Empty.Groups.Count==1` /
> HasFlag-семантика). 5-линзовое adversarial-ревью (correctness 0 находок) + `/code-review high` → Approve;
> гейты build -warnaserror **0/0 ×3** + **783/783** + verify.ps1 green; tests-only → без бампа версии и launch-теста.
> **Грабли:** combined-flags `IsLLM` = `HasFlag`(«все биты») → `IsLLM(Ollama|GoogleV1)`=**false** (агент-ревьюер
> дал неверное `true` → выведено из кода); `typeof`-InlineData для `DefaultSettings` НЕ менять на
> `BeAssignableTo<ITranslateSettings>` (сделало бы тест вакуумным — все 12 типов реализуют интерфейс).
> Остаётся ONGOING (demuxer/OCR/Translation-сетевые мапперы Google/Microsoft ещё открыты).
> **Прогресс 2026-06-28 (PR #86, +4 теста → 725, v0.3.26 — behaviour-change, НЕ tests-only):** закрыт
> follow-up прошлого среза — прод `GetBytesReadable` переведён на
> `ToString("0.## ", CultureInfo.InvariantCulture)`: десятичный разделитель теперь всегда `.` при любой
> культуре («1.5 KB», не «1,5 KB» на ru-RU). Единственное прод-использование — диаг-лог GPU-памяти
> `GpuAdapter.ToString()`; frozen-контракты метод не упоминают; формат `"0.##"` без групп-разделителей →
> единственный culture-риск был десятичный разделитель. Тесты: `UtilsByteFormatTests` +3 дробных кейса
> (1.5/1.25 KB, 1.5 MB) + culture-guard, форсящий `ru-RU` (`...UsesDotSeparator_UnderCommaCulture` — **падал
> бы до фикса**, настоящий non-vacuous регресс-guard; try/finally восстанавливает `CurrentCulture`).
> Самообзор (5 линз) + `/code-review` → Approve. Гейты build -warnaserror 0/0 (LLPlayer+YoutubeDL) +
> FlyleafLibTests 725/725 + verify-frozen green; бамп 0.3.25→0.3.26, `.exe` launch-тест чистый (10 c, без crash.log).
> **Прогресс 2026-06-28 (PR #85, +114 тестов → 721):** покрыты ранее непокрытые ПУБЛИЧНЫЕ чистые функции
> `Utils.cs` (ожидания выведены из спеки — regex/бит-математика/.NET-форматтеры/switch, не из прогона):
> `Align`/`FFALIGN`/`Scale`/`SnapToInt`/`GCD` (`UtilsMathTests.cs`); `TruncateString`/`GetUrlExtention`/
> `LowerCaseFirstChar`/`ToHexadecimal`/`DoubleToTimeMini`/`GetValidFileName` (`UtilsStringTests.cs`);
> `GetBytesReadable` (`UtilsByteFormatTests.cs` — **culture-safe: только целочисленные значения + EndsWith-пороги**,
> т.к. прод `ToString("0.## ")` БЕЗ InvariantCulture — latent prod-issue, не чинили в tests-only PR);
> `GetMediaParts` (`UtilsMediaPartsTests.cs` — regex S/E/Year + control-flow: early-return при `res.Index==0`
> НЕ заполняет Extension; .NET `Match.Empty.Groups.Count==1` на no-match → fall-through к Year/Title; RxResolution/
> RxExtended noise-границы); `ParseQueryString`/`GetFlagsAsList`/`GetFlagsAsString`/`GetRecInnerException` (cap 4)/
> `GetDumpMetadata` (null/empty/single — multi-entry порядок dict-зависим, опущен) (`UtilsQueryFlagsTests.cs`);
> `Disposable` (`DisposableTests.cs`); `TargetLanguageExtensions.DisplayName`/`ToISO6391` (EnumMember-коды сверены:
> en-US/fr-FR/pt-PT/zh-CN/ru/de) (`TargetLanguageExtensionsTests.cs`). Многоагентно: разведка (4 Explore →
> 91 кандидат/44 реко; сверкой с кодом пойманы 2 ошибки разведки в GetBytesReadable/GetMediaParts) → реализация →
> **adversarial-ревью (4 линзы: non-vacuity/correctness/brittleness/coverage) → 2 SHIP + 2 FIX-THEN-SHIP**;
> усилены DoubleToTimeMini (round-down контраст), TruncateString (Min-ветка), GetMediaParts (+3 regex-границы);
> отклонены 2 CRITICAL как inapplicable (culture тестов уже safe; GetValidFileName platform moot — Windows-only TFM).
> `/code-review high` → Approve. Все чистые (ноль продакшн-кода), гейты build -warnaserror 0/0 ×3 + verify.ps1 green;
> tests-only → без бампа версии и launch-теста. **Follow-up:** ✅ ВЫПОЛНЕНО в PR #86 (выше) — prod `GetBytesReadable`
> переведён на InvariantCulture. Остаётся ONGOING
> (demuxer/playlist/OCR/Translation-мапперы Google/Microsoft/ToTargetLanguage ещё открыты).
> **Прогресс 2026-06-27 (PR этот, +54 теста → 488):** добавлены юнит-тесты на ранее непокрытые чистые функции:
> `Utils` форматтеры времени (`TsToTime`/`TicksToTime`/`McsToTime`/`TicksToTimeMini` — sentinels `NoTs`/0,
> положительные/отрицательные, <1мин/<1ч/<1сут/≥1сут ветки) → `FlyleafLibTests/Utils/UtilsTimeFormatTests.cs`;
> `TextEncodings.DetectEncoding` (byte[]+path: BOM UTF-8/UTF-16 LE/BE/UTF-32 BE, UTF-8-без-BOM, пусто, clamp
> maxBytes, missing-file→null; характеризован quirk «UTF-32 LE BOM детектится как UTF-16 LE» из-за порядка
> проверок) → `FlyleafLibTests/Utils/TextEncodingsTests.cs`; DeepL `ToSourceCode`/`ToTargetCode` switch-таблицы
> (ku→KMR, no→nb, EN-US/EN-GB, PT-PT/PT-BR, ZH-HANS/ZH-HANT, Kurdish→KMR, default uppercase + hyphen-split
> «fr-FR»→FR; internal через InternalsVisibleTo) → `FlyleafLibTests/MediaPlayer/Translation/DeepLLanguageCodeTests.cs`;
> `DubbingSrtReader` edge-cases (PadRight дробных мс, skip блока без таймлайна с верной индексацией, skip
> пустого текста, single-digit час) → дополнения в `DubbingSrtReaderTests.cs`. Все чистые (ноль продакшн-кода),
> гейты build -warnaserror 0/0 ×3 + verify.ps1 green. Остаётся ONGOING (demuxer/playlist/OCR ещё открыты).

### T-04 — Whisper-квантизация (q8_0/q5_0) в UI 🟡 ⓢ-Ⓜ · ✅ **DONE (PR #73, v0.3.21, 2026-06-27)**
> ✅ **Закрыт.** Раньше загрузчик моделей whisper.cpp жёстко слал `QuantizationType.NoQuantization`
> (`WhisperModelDownloadDialogVM.cs:202` — параметр `default`). Теперь список моделей = **кросс-продукт**
> `GgmlType × {NoQuantization, Q5_0, Q5_1, Q8_0}` (Approach A дизайн-панели: каждая квант-вариация — полноценная
> строка download/select/delete, статус Downloaded по файлу). `WhisperCppModel` получил поле `QuantizationType
> Quantization` (персист, дефолт NoQuantization); `ModelFileName`/`ToString`/`Equals`/`GetHashCode` — quant-aware;
> **NoQuantization byte-identical** прежнему (`ggml-{model}.bin`, лейбл, дефолтный выбор `Models.First()`),
> квантизованные → `ggml-{model}-{quant}.bin` (e.g. `ggml-base-q5_1.bin`). Загрузка шлёт `GetGgmlModelAsync(model.Model,
> model.Quantization, token)`; friendly-catch на `HttpRequestException` NotFound (недоступная комбинация на сервере →
> понятное сообщение, не сырой HTTP; .tmp чистится). Лейбл в обоих ComboBox → `{Binding}` (ToString=model+quant),
> ширина 200→240. **Batch-parity:** `BatchSubtitleConfigSnapshot.CloneWhisperCppConfig` копирует `Quantization`
> (silent-bug class — reflection-guard НЕ рекурсит в `WhisperCppModel` → отдельный тест). Персист: enum'ы = строки
> (`AppConfig.GetJsonSerializerOptions`/`JsonStringEnumConverter`); pre-0.3.21 конфиг без ключа → NoQuantization →
> тот же файл, миграции нет. Гейты build `-warnaserror` **0/0 ×3** + тесты **410/410** (+17) + verify.ps1 (env/plugin/
> doc-coverage/frozen) green. Дизайн-панель (3+судья) → **adversarial review (5 линз+триаж): SHIP-READY, 0 crit/imp,
> 0 must-fix**; correctness-агент декомпилировал Whisper.net 1.9.0 и live-проверил все 12×{q5_0,q5_1,q8_0} на HF-зеркале
> (все доступны). `.exe` launch-test 0.3.21 чистый. Контракты product-behavior + config-data обновлены. Детали:
> второй мозг `Sessions/2026-06-27-handoff-t04-whisper-quant.md`.

whisper.cpp/Whisper.net поддерживают квантизованные модели, но в UI не выведено. **Решение:** дать выбор
квантизации (лучший безопасный выигрыш скорости). См. References whisper-research во втором мозге.

### T-05 — Судьба M3-редизайна (PR #31) 🟢 — · ✅ **DONE (решение владельца «B-opt-in», PR #91 merge `89a0fc7`, v0.3.29, 2026-06-28; PR #31 ЗАКРЫТ)**
> ✅ **Закрыто.** [PR #31](https://github.com/Gorgutc/LLPlayer_ru/pull/31) (`claude/modest-brown-29ced0`, 1:1 Material 3 re-skin) был **на 123 коммита позади main** (1 коммит от 2026-06-22). Многоагентная верификация (5 Explore) + adversarial-панель (3 стойки: steelman-merge / steelman-revive / devil-close) показали: механически конфликтуют лишь 3 файла, но **семантически re-skin безнадёжно протух** — перекрашивал UI, которого структурно уже нет (новые `AiInsightsDialog`/`WordManagerDialog` + контролы бара A-B/waveform/ASR-pause он не знает) → любой мерж = несогласованный «half-M3» UI. **Решение владельца (AskUserQuestion): `B-opt-in`** — PR #31 **закрыт** (с пояснением; ветка `claude/modest-brown-29ced0` + дизайн-доки `docs/agent/redesign/` сохранены → дизайн восстановим), а спасённый **M3 цвет-фундамент отгружен аддитивно за тумблером** ([PR #91](https://github.com/Gorgutc/LLPlayer_ru/pull/91), v0.3.29). Opt-in `AppConfigTheme.ShowM3Theme` (Settings ▸ Themes, **default OFF → byte-identical**, оверлеи НЕ мерджатся в `App.xaml`): `RefreshM3Overlays()` переутверждает `M3.Surfaces.xaml` + `M3.Accent.xaml` (взяты ИЗ PR #31; **не** `M3.xaml` с формами) последними в `MergedDictionaries` → переопределяют стандартные MaterialDesign-ключи при `DynamicResource` → весь UI в rose Material You **без правок поверхностей**. **Цвет-only** (формы/радиусы/шаблоны не меняются — полный per-surface re-skin = будущая задача); только Dark; Accent-словарь лишь при default-цветах без accent-sync; fail-soft. Гейты build `-warnaserror` **0/0 ×3** + тесты **820/820** + verify.ps1 (frozen/doc/dub-license) green; 4-линзовое adversarial-ревью реализации (все SHIP, 0 дефектов) + `/code-review high` Approve; **`.exe` launch 0.3.29 чистый** (жив 13c, без crash.log, FFmpeg 7 DLL + e_sqlite3). Контракты wpf-design + config-data аддитивно. **Owner manual-smoke:** тумблер Material 3 Theme → rose Material You; OFF / Light / accent-sync / свой цвет → стоковый вид. Детали: второй мозг `Sessions/2026-06-28-handoff-t05-m3-decision.md`.

### T-06 — Дрейф документации форка vs upstream 🟢 ⓢ · ✅ **DONE (PR #84, v0.3.25, 2026-06-28, doc-only)**
> ✅ **Закрыт (без кода).** Решение владельца (AskUserQuestion): «документировать как agent-infra + заметка про
> будущую RU-локализацию». Цель форка `_ru` явно зафиксирована в репо в трёх местах: **README** (секция «🌐 About
> this fork (`_ru`)»), **`docs/agent/architecture.md`** (секция «Fork Relationship», agent-facing), пойнтер в
> **`AGENTS.md`** (Project Snapshot). Суть: `_ru` = русифицированный слой агентской/Codex-инфраструктуры (`docs/agent/`,
> `scripts/codex/`, `Plugins/llplayer-codex/`, RU commit-сообщения), **НЕ** локализация приложения — UI/код плеера
> наследуется от upstream и НЕ переведён; RU-локализация ресурсов — возможное будущее направление (не начато,
> сопоставимо по объёму с F-13). verify-fast (doc-coverage/frozen) green.

### T-07 — `SrtExporter`: поддержка тегов `<i>` 🟢 ⓢ · ✅ **DONE (этот PR, v0.3.13, 2026-06-27, в составе F-06)**
> ✅ **Закрыт.** `SubtitleExporter.RenderItalic` оборачивает ITALIC-диапазоны `SubStyle` в `<i>…</i>` (тег понимают
> и SRT, и VTT), вставляя теги с конца строки. Только ITALIC (по букве TODO `<i>`); bold/underline/color/font НЕ
> эмитятся. Применяется к ОРИГИНАЛЬНОМУ тексту (offsets `SubStyle` индексируют `Text`; для перевода стили не
> передаются). TXT — всегда plain. `Text` хранит чистый текст (теги уже снесены в `SubStyles` через
> `SSAtoSubStyles`), поэтому это реконструкция, а не pass-through. Покрыто тестами (whole-cue/disjoint/clamp/
> non-italic-ignored/null).

### T-08 — ASR fold-back при перемотке назад 🟢 Ⓜ · ✅ **DONE (PR #69, merge `f7dc152`, v0.3.19, 2026-06-27, бандл с T-09)**
> ✅ **Закрыт (default OFF).** При старте интерактивного ASR с середины (`curTime>30s`) старый код сикал к `curTime` и
> транскрибировал только вперёд, пропуская `[0..curTime)`. Теперь при `ASRFoldBack=true` пропущенная половина
> дотранскрибируется: backfill `[0..curTime)` идёт **ПЕРВЫМ** (`Seek` в начало → `RunPass(curTime)`), затем forward
> **КОНТИГУАЛЬНО** продолжает от места остановки **без ре-сика** → cue эмитятся строго по возрастанию времени →
> append-only `SubtitlesManager.Add` остаётся отсортированным **по построению** (без правок менеджера — выбран
> **Ordering A** дизайн-панелью; B/C ломали бы сорт/Index-инвариант). Чистый `FlyleafLib/Utils/AsrFoldback.cs`
> (`Plan`/`ReachedStop`); петля demux/decode извлечена в local `RunPass(TimeSpan? stopAt)`. OFF по умолчанию: fold-back
> задерживает субтитры у текущей позиции (trade-off против seek-to-current UX). Батч — структурный no-op (`ReadAll(0)`).
> Adversarial-ревью: исправлены seam-дубли (контигуальное продолжение vs ре-сик назад к keyframe). Гейты 0/0, тесты
> **363/363**, `.exe` launch-clean. **Реальная локация TODO была `:666`, не `:616` (бэклог устарел после F-04/F-07).**

### T-09 — ASR: дробление чанков по тишине 🟢 Ⓜ · ✅ **DONE (PR #69, merge `f7dc152`, v0.3.19, 2026-06-27, бандл с T-08)**
> ✅ **Закрыт (default ON).** Чанки резались строго по размеру/времени, деля фразу посреди слова. Теперь продюсер
> **предпочитает резать на тихой границе** (RMS < `ASRSilenceRmsThreshold=0.01`, только после `ASRSilenceSoftFraction=0.6`
> бюджета), size/elapsed-капы — жёсткий потолок; на шумном материале без пауз graceful fallback к капам (= прежнее
> поведение, byte-identical при OFF). Чистый тестируемый `FlyleafLib/Utils/AsrSilence.cs` (`Rms`/`IsSilent`/`IsSoftReady`)
> над уже ресэмплированным s16-mono-16kHz PCM; `ResampleTo` void→int. Применяется к интерактиву И батчу. Кноб
> `ASRSplitOnSilence`. Adversarial-ревью: `resampledDataSize==0` больше не ложная тишина. **Реальная локация TODO была
> `:816`, не `:765`.** Детали (T-08+T-09): второй мозг `Sessions/2026-06-27-handoff-t09-t08-asr-chunks.md`.

### T-10 — Per-segment language detection 🟢 Ⓛ · ✅ **DONE (v0.3.32, 2026-06-29, opt-in)**
> ✅ **Закрыт config-тумблером (решение владельца, AskUserQuestion).** Конфликт с frozen F-17 разрешён **opt-in**
> `Subtitles.ASRPerSegmentLanguage` (**default OFF → byte-identical** к F-17): OFF — язык пиннится на первом непустом
> сегменте (анти-дрейф F-17); ON — каждый сегмент whisper.cpp / чанк faster-whisper авто-детектит свой язык
> (mixed-language контент), и per-cue язык пишется в **новое поле `SubtitleData.Language`**. Гейтинг пиннинга вынесен
> в чистый тестируемый `FlyleafLib/Utils/AsrLanguagePolicy.cs` (`ShouldPinLanguage`/`ShouldResetPerChunk`); 4 сайта в
> `SubtitlesASR.cs` (whisper.cpp `ChangeLanguage` + capture; faster-whisper per-chunk reset + `--language` inject) гейтятся
> `!ASRPerSegmentLanguage`. Затрагивает только auto-detect путь (model/user-fixed язык — no-op). Интерактив + батч
> (snapshot копирует флаг + reflection-guard). UI-тумблер «Detect Language Per Segment» в `SettingsSubtitles.xaml`.
> Многоагентно: верификация (workflow `w9z322ydt`, 7 агентов) → реализация → **adversarial-ревью (workflow `wj858vuaq`,
> 5 линз+триаж): SHIP, 0 must-fix** (единственный «CRITICAL NRE» — **ложноположительный**: безусловный `continue` на
> `SubtitlesASR.cs:1791` делает null-forgiving yield недостижимым при null + `Language.Get(null)` сам null-safe) →
> `/code-review high` Approve. Гейты build `-warnaserror` **0/0** (LLPlayer+YoutubeDL) + тесты **1035/1035** (+8) +
> verify.ps1 green; **`.exe` launch 0.3.32 чистый** (жив 13с, без crash.log, FFmpeg + e_sqlite3). Контракты
> media-runtime/config-data/product-behavior аддитивно. Реальная локация TODO была `SubtitlesASR.cs:1426-1427`
> (бэклог устарел: `:1114-1116`). Детали: второй мозг `Sessions/2026-06-29-session-LIVE-tracker-10.md`.

### T-11 — Sandbox `dotnet`/Windows SDK + нет .NET 10 SDK у владельца 🟢 ⓢ · ✅ **DONE (PR #84, v0.3.25, 2026-06-28, doc-only)**
> ✅ **Закрыт (без кода).** Локальное окружение и процедура эскалации задокументированы в **`docs/agent/technical-stack.md`**
> (новая секция «Local Development Environment (T-11)»): нет .NET 10 SDK у владельца (есть 8/9 + **.NET 11 preview**,
> который собирает `net10.0`; `check-environment.ps1` warn'ит «10.0.x not found» — non-fatal, CI = авторитет 10.0.x);
> **sandboxed `dotnet` падает на чтении Windows SDK из AppData → запросить эскалацию и перезапустить ту же команду**
> (зеркалит `AGENTS.md` Verification Gates); + локальные грабли (PowerShell-не-Bash, `git commit -F`, `dotnet publish`
> не копирует `FFmpeg/` → `Copy-Item`, `py -3`). Frozen build-target и пин CI-SDK в `dependency-baseline.md` не тронуты.
> verify-fast green.

---

## 4. 📊 РАНЖИРОВАНИЕ ПО ВАЖНОСТИ (убыв.)

| # | ID | Задача | Важн. | Сложн. |
|---|----|--------|:---:|:---:|
| 1 | ~~**B-01**~~ ✅ | Краш ProductVersion → DONE PR #46 v0.3.8 | 🔴 | ⓢ |
| 2 | ~~**F-01**~~ ✅ | Универсальная ре-сегментация → DONE PR #53 v0.3.10 | 🔴 | Ⓜ |
| 3 | ~~**T-01**~~ ✅ | Рассинхрон FFmpeg-биндингов → DONE PR #58 v0.3.12 | 🟠 | Ⓜ |
| 4 | ~~**F-02 срез**~~ ✅ | ASR денойз (high-pass+afftdn) → DONE PR #76 v0.3.23; **полный Demucs = STANDBY (по триггеру)** | 🟠 | Ⓛ |
| 5 | ~~**F-05**~~ ✅ | Языковые префы primary/secondary → DONE PR #58/#60 v0.3.14 | 🟠 | Ⓜ |
| 6 | ~~**B-02**~~ ✅ | Сегментер: короткая первая реплика → DONE codex PR #48 | 🟠 | ⓢ |
| 7 | ~~**F-04**~~ ✅ | ASR pause/resume → DONE PR #65 v0.3.17 | 🟠 | Ⓜ |
| 8 | ~~**T-02**~~ ✅ | Ранняя диагностика VC++ → DONE PR #62/#64 v0.3.15-16 | 🟠 | ⓢ-Ⓜ |
| 9 | ~~**F-06**~~ ✅ | Экспорт TXT/VTT → DONE PR #59 v0.3.13 | 🟡 | ⓢ-Ⓜ |
| 10 | ~~**F-07**~~ ✅ | AI-summary / лексика → DONE PR #67 v0.3.18 | 🟡 | Ⓜ |
| 11 | **F-15** | Yomitan/10ten в плеере | 🟡 | Ⓜ-Ⓛ |
| 12 | **F-03** | Диаризация | 🟡 | Ⓛ |
| 13 | **T-03** | Тестовое покрытие (ONGOING) | 🟡 | Ⓜ |
| 14 | ~~**F-08**~~ ✅ | Sync-хелпер (shift-all) → ALREADY DONE v0.3.17 | 🟡 | ⓢ-Ⓜ |
| 15 | ~~**B-03**~~ ✅ | Сегментер: кламп perLine → DONE codex PR #48 | 🟡 | ⓢ |
| 16 | ~~**T-04**~~ ✅ | Whisper-квантизация в UI (q5_0/q5_1/q8_0) → DONE PR #73 v0.3.21 | 🟡 | ⓢ-Ⓜ |
| 17 | ~~**F-14**~~ ✅ | Расширенный локальный поиск (match-case/whole-word/regex) → DONE PR #71 v0.3.20 | 🟢 | ⓢ-Ⓜ |
| 18 | ~~**F-09**~~ ✅ | Watch-folder авто-batch → DONE PR #74 v0.3.22 | 🟢 | ⓢ-Ⓜ |
| 19 | ~~**F-10**~~ ✅ | Anki / Word Management → DONE PR #79 v0.3.24 | 🟢 | Ⓛ |
| 20 | ~~**F-11**~~ ✅ | Dictionary API (определения слов + авто-Anki) → DONE PR #82 v0.3.25 | 🟢 | Ⓛ |
| 21 | **F-16** | Дубляж: voice-bank ✅ PR #93; custom voice-ID ✅ PR #96; per-line voice ✅ PR #106 + monitor bridge; per-speaker/фазы 3-6 TODO | 🟢 | Ⓛ |
| 22 | ~~**F-12**~~ ✅ | Аудио-waveform → DONE PR этот v0.3.28 (A-B повтор DONE v0.3.27) | 🟢 | Ⓛ |
| 23 | ~~**T-07**~~ ✅ | SrtExporter теги `<i>` → DONE PR #59 v0.3.13 | 🟢 | ⓢ |
| 24 | ~~**T-08/T-09**~~ ✅ + **T-10** | fold-back/silence-split ✅ DONE PR #69 v0.3.19; **T-10** per-seg lang ⚠️ конфликт F-17 (OPEN) | 🟢 | Ⓜ/Ⓛ |
| 25 | ~~**T-06**~~ ✅ | Дрейф документации форка → DONE PR #84 (doc-only) | 🟢 | ⓢ |
| 26 | ~~**T-05**~~ ✅ | M3-редизайн: закрыт PR #31 + opt-in M3 цвет-фундамент → DONE PR #91 v0.3.29 | 🟢 | — |
| 27 | **F-13** | Кросс-платформенность Avalonia | 🟢 | ⓍⓁ |
| 28 | ~~**T-11**~~ ✅ | Sandbox/SDK окружение (doc) → DONE PR #84 (doc-only) | 🟢 | ⓢ |

> ✅ DONE-строки выше зачёркнуты для быстрого скана. **Открыто на 2026-07-01 (v0.3.35):** T-03(ongoing), F-03(prep-срез SpeakerId ✅ PR #102 v0.3.33; диаризация GPU TODO), F-16(per-line voice ✅; per-speaker/фазы 3-6 TODO), F-13, F-02-full(Demucs, по триггеру). **T-10 ✅ DONE (v0.3.32, opt-in `ASRPerSegmentLanguage`).** См. также 5b (B-04/F-17/F-18 — все ✅ DONE). **F-11/T-06/T-11 ✅ DONE (v0.3.25); F-12 полностью ✅ DONE (A-B повтор v0.3.27 + waveform v0.3.28); F-15 ✅ DONE-BY-F-11; T-05 ✅ DONE (PR #31 закрыт + opt-in M3 цвет PR #91 v0.3.29).**

## 5. 🛠️ РАНЖИРОВАНИЕ ПО СЛОЖНОСТИ (возр. — самое лёгкое сверху)

| # | ID | Задача | Сложн. | Важн. |
|---|----|--------|:---:|:---:|
| 1 | ~~**B-03**~~ ✅ | Кламп perLine → DONE codex PR #48 | ⓢ | 🟡 |
| 2 | ~~**T-11**~~ ✅ | Sandbox/SDK окружение (doc) → DONE PR #84 | ⓢ | 🟢 |
| 3 | ~~**T-06**~~ ✅ | Дрейф документации форка → DONE PR #84 | ⓢ | 🟢 |
| 4 | ~~**T-07**~~ ✅ | SrtExporter теги `<i>` → DONE PR #59 v0.3.13 | ⓢ | 🟢 |
| 5 | ~~**B-01**~~ ✅ | Фикс ProductVersion → DONE PR #46 v0.3.8 | ⓢ | 🔴 |
| 6 | ~~**B-02**~~ ✅ | Сегментер: forward-merge головы → DONE codex PR #48 | ⓢ | 🟠 |
| 7 | ~~**T-02**~~ ✅ | Ранняя диагностика VC++ → DONE PR #62/#64 | ⓢ-Ⓜ | 🟠 |
| 8 | ~~**F-06**~~ ✅ | Экспорт TXT/VTT → DONE PR #59 v0.3.13 | ⓢ-Ⓜ | 🟡 |
| 9 | ~~**F-09**~~ ✅ | Watch-folder → DONE PR #74 v0.3.22 | ⓢ-Ⓜ | 🟢 |
| 10 | ~~**T-04**~~ ✅ | Whisper-квантизация UI (q5_0/q5_1/q8_0) → DONE PR #73 v0.3.21 | ⓢ-Ⓜ | 🟡 |
| 11 | ~~**F-14**~~ ✅ | Локальный поиск (match-case/whole-word/regex) → DONE PR #71 v0.3.20 | ⓢ-Ⓜ | 🟢 |
| 12 | ~~**F-08**~~ ✅ | Sync-хелпер → ALREADY DONE v0.3.17 | ⓢ-Ⓜ | 🟡 |
| 13 | ~~**F-01**~~ ✅ | Универсальная ре-сегментация → DONE PR #53 v0.3.10 | Ⓜ | 🔴 |
| 14 | ~~**F-04**~~ ✅ | ASR pause/resume → DONE PR #65 v0.3.17 | Ⓜ | 🟠 |
| 15 | ~~**F-05**~~ ✅ | Языковые префы → DONE PR #58/#60 v0.3.14 | Ⓜ | 🟠 |
| 16 | ~~**T-01**~~ ✅ | FFmpeg-биндинги → DONE PR #58 v0.3.12 | Ⓜ | 🟠 |
| 17 | **T-03** | Тестовое покрытие (ongoing) | Ⓜ | 🟡 |
| 18 | ~~**F-07**~~ ✅ | AI-summary / лексика → DONE PR #67 v0.3.18 | Ⓜ | 🟡 |
| 19 | ~~**T-08/T-09**~~ ✅ + **T-10** | fold-back/silence-split ✅ DONE PR #69 v0.3.19; **T-10** per-seg lang ⚠️ конфликт F-17 (OPEN, large) | Ⓜ/Ⓛ | 🟢 |
| 20 | **F-15** | Yomitan/10ten мост | Ⓜ-Ⓛ | 🟡 |
| 21 | ~~**F-02 срез**~~ ✅ | ASR денойз → DONE PR #76 v0.3.23; **полный Demucs-сайдкар = STANDBY (по триггеру)** | Ⓛ | 🟠 |
| 22 | **F-03** | Диаризация (сайдкар) | Ⓛ | 🟡 |
| 23 | **F-16** | Дубляж: voice-bank/custom/per-line ✅; per-speaker/фазы 3-6 TODO | Ⓛ | 🟢 |
| 24 | ~~**F-10**~~ ✅ | Anki / Word Management → DONE PR #79 v0.3.24 | Ⓛ | 🟢 |
| 25 | ~~**F-11**~~ ✅ | Dictionary API (определения слов + авто-Anki) → DONE PR #82 v0.3.25 | Ⓛ | 🟢 |
| 26 | ~~**F-12**~~ ✅ | Аудио-waveform → DONE PR этот v0.3.28 | Ⓛ | 🟢 |
| 27 | **F-13** | Avalonia (переписывание UI) | ⓍⓁ | 🟢 |
| — | ~~**T-05**~~ ✅ | M3-редизайн: закрыт PR #31 + opt-in M3 цвет-фундамент → DONE PR #91 v0.3.29 | — | 🟢 |

> ✅ **Открыто на 2026-07-01 (v0.3.35), легче→тяжелее:** T-03(ongoing) · F-03(prep SpeakerId ✅ PR #102 v0.3.33; диаризация GPU TODO) · F-16(per-line voice ✅; per-speaker/фазы 3-6 TODO) · F-02-full(Demucs, по триггеру) · F-13. **T-10 ✅ DONE (v0.3.32, opt-in `ASRPerSegmentLanguage`).** **T-05 ✅ DONE** (PR #31 закрыт + opt-in M3 цвет-фундамент PR #91 v0.3.29). **F-11/T-06/T-11 ✅ DONE; F-12 полностью ✅ DONE (A-B повтор v0.3.27 + waveform v0.3.28); F-15 ✅ DONE-BY-F-11.** Всё остальное в таблице — ✅ DONE.

---

## 5b. Δ Ранжирование с учётом задач из скриншотов 2026-06-25 (B-04 / F-17 / F-18) — ✅ ВСЕ DONE
> ✅ **B-04** (LM Studio timeout) DONE PR #51 v0.3.9; **F-17** (дрейф языка) + **F-18** (капс) DONE PR #55 v0.3.11.
Чтобы не переписывать таблицы выше, фиксирую позиции трёх новых задач (исторически):
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
2. ~~**B-02 + B-03**~~ ✅ **СДЕЛАНО (codex PR #48 + усиленные тесты PR #49, 2026-06-26).** Осталось **F-01** —
   универсальная ре-сегментация загруженных/sidecar/встроенных субтитров (`SubtitleReader.ReadAll` минует
   `Resegment`), отдельным PR. **B-04** (LM Studio timeout) codex намеренно оставил для основной машины.
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
