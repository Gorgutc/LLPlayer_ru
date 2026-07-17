# LLPlayer_ru — Task Backlog (рабочий бэклог)

> **Назначение:** единый, максимально подробный список незакрытых задач для работы в будущих сессиях.
> Каждая задача имеет стабильный ID (`DOC-`/`B-`/`F-`/`T-`/`HC-`), описание, файлы, ссылки, важность, сложность, статус
> и мои рассуждения. В конце — два ранжирования: **по важности** и **по сложности**.
> **§8 (HC-*) — living-набор находок многоагентного аудита здоровья кода (сессия #16 и follow-up-раунды),
> ранжирован простое→сложное (ⓢ→Ⓜ→Ⓛ). Ручной счётчик намеренно не пиннится: follow-up ID добавляются по мере
> подтверждения, а актуальный порядок живёт в §4–§6.**
>
> Создан 2026-06-25 (сессия-анализ). Жив (living) — обновлять по мере закрытия задач.
> Дополняет, а не заменяет: `docs/agent/*-contract.md` (frozen-контракты), второй мозг
> `Improvements.md` + `Sessions/2026-06-25-handoff-competitive-analysis-roadmap.md`, авто-память.
> Перед изменением ПОВЕДЕНИЯ — сверяться с frozen-контрактами (не трогать без явного запроса владельца).
>
> **Актуальный рабочий срез (2026-07-17, v0.3.61):** app-срез `HC-27b` смёржен через
> [PR #142](https://github.com/Gorgutc/LLPlayer_ru/pull/142), post-merge truth sync — через
> [PR #143](https://github.com/Gorgutc/LLPlayer_ru/pull/143). `T-13a` реализован и проверен в
> [PR #144](https://github.com/Gorgutc/LLPlayer_ru/pull/144): Testing Release больше не интерполирует release
> input/outputs в PowerShell, а fast gate содержит adversarial fixtures. `T-13b` добавил fast gate в обычный
> Build & Test: [PR #145](https://github.com/Gorgutc/LLPlayer_ru/pull/145) зелёный, а закрытый без merge
> proof-[PR #146](https://github.com/Gorgutc/LLPlayer_ru/pull/146) намеренно покрасил тот же gate на сломанном marker.
> `T-13g` изолировал write-token в [PR #147](https://github.com/Gorgutc/LLPlayer_ru/pull/147): выбранный ref собирается
> read-only, отдельный trusted verify job выпускает fixed-name verified artifact, а write job только загружает его.
> Первый feature-head [run 29526902608](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29526902608) зелёный.
> `T-13c` закрыл full-verify/reviewer routing в [PR #148](https://github.com/Gorgutc/LLPlayer_ru/pull/148): все
> **477/477** tracked C#/XAML/project paths получают literal `verify`, а behavioral guard проверяет будущие пути,
> near-miss и wrong-case mutations. Feature-head [run 29604134291](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29604134291)
> и post-merge [run 29604405369](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29604405369) зелёные.
> Полный локальный `verify.ps1` и `ship.ps1` — PASS: **1376/1376**, 0 warnings/errors, publish smoke green. Post-merge CI #143 выявил один
> thread-pool-starvation timeout в HC-27b lock-тесте; в #144 тест переведён на dedicated workers и прошёл 20/20.
> `HC-27b` остаётся `IN-PROGRESS` до targeted owner smoke; active **4**, unresolved **11**; следующий agent-action —
> `T-13e` preflight, затем `T-13f`.

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
> **Две машины:** проект ведётся на двух ПК — имя пользователя в путях зависит от машины:
> `C:\Users\Maxim\…` (ПК №2) и `C:\Users\Junior\…` (ПК №1). Оба варианта путей валидны — не «чинить»
> один в другой (у каждой машины свой второй мозг и своя авто-память Claude).

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
> при нужде поднять headroom и для локально-направленных endpoint'ов (**вынесено в отдельную задачу T-12**). Детали: второй мозг `Sessions/2026-06-26-handoff-b04-llm-timeout.md`.
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

### B-05 — Cross-thread краш загрузки субтитров: `WordPopup.Clear()` трогает WPF UI на worker-потоке 🔴 ⓢ · ✅ **DONE (v0.3.56, 2026-07-04, сессия #32)** · был NEW (скриншот владельца, P0)
> ✅ **Закрыт.** Гард `Dispatcher.CheckAccess()` в начале `WordPopup.Clear()` ([`LLPlayer/Controls/WordPopup.xaml.cs:135`](../../LLPlayer/Controls/WordPopup.xaml.cs)):
> при вызове не с UI-потока — `Dispatcher.BeginInvoke(new Action(Clear))` + return; UI-поточные вызовы (settings/chat-config/word-translate-config-error)
> идут синхронно как раньше. Защищает ВСЕ вызовы `Clear()` (и будущие). App-слой, FlyleafLib (frozen) НЕ тронут — в духе frozen
> `media-runtime-contract` §«WPF Dispatcher Boundaries» (не удалять маршалинг, держать UI-thread границы).
**Симптом (скриншот владельца, v0.3.55):** диалог «Subtitles Unknown Error → Cannot load all subtitles on worker thread:
The calling thread cannot access this object because a different thread owns it». Субтитры (в т.ч. свежесделанные ASR+переведённые)
не подхватываются — загрузка падает.
**Корневая причина:** `SubManager.Open` (`SubtitlesManager.cs:520`) на ПЕРВОЙ реплике внутри `SubtitleReader.ReadAll` выставляет
`LanguageSource = lang`; `Subtitle.Load()` (`Subtitles.cs:625`) идёт на ThreadPool-воркере. Setter `LanguageSource` шлёт
`OnPropertyChanged(nameof(Language))` (`:114/:674`) синхронно → `WeakEventManager` доставляет `WordPopup.SubManagerOnPropertyChanged`
(`:116`) на ТОМ ЖЕ воркере → `Clear()` трогает `DefinitionText.Text` (`DependencyObject`, `:151`) не с UI-потока → `InvalidOperationException`,
вся загрузка субтитров фейлится (`Subtitles.cs:620` `RaiseUnknownErrorOccurred`).
**Регрессия (git blame):** подписка `SubManagerOnPropertyChanged → Clear()` и `LanguageSource = lang` — старые (upstream). Краш ВНЁС
**F-11** (`35320b8`, PR #82, v0.3.25): добавил `DefinitionVisible=false; DefinitionText.Text=""` в `Clear()` — до этого `Clear()` на
воркере трогал только не-UI кэши (безвредно). Латентный с v0.3.25.
**Тест:** LLPlayer без тест-проекта; WPF cross-thread не юнит-тестируется без STA-UI-потока → гейты build 0/0 + полный набор 1316
(без регрессий) + manual-smoke владельца (загрузить переведённый `.ru.srt` → без краша, субтитры появляются).

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
> **⚠️ Совместимость движка (из ресёрча `References/speaker-diarization-research-2026-06.md`, перенесено сверкой #17):**
> faster-whisper-XXL `--diarize pyannote_v3.1` авто-включает `--sentence`, что меняет формат вывода и **ломает наши
> парсеры `SubShortReg`/`SubLongReg`**. Значит выбор архитектуры не нейтрален: (а) XXL `--diarize` (без HF-токена, но
> с обходом `--sentence`-формата) vs (б) отдельный pyannote-сайдкар по образцу `dub_sidecar/` (полный контроль формата).
> Решение — за владельцем; при XXL-пути обязательно учесть форматный конфликт до кода.
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

### F-19 — Speech-aware ре-сегментация (тайминг субтитров под речь) 🟠 Ⓜ · СРЕЗ 1 ✅ DONE (PR #137, v0.3.59) · СРЕЗ 2 ✅ DONE (v0.3.60, сессия #35) · тир 3 ОТЛОЖЕН (GPU)
> **✅ СРЕЗ 2 РЕАЛИЗОВАН (v0.3.60, сессия #35, 2026-07-05):** Silero VAD-snapping границ cue (CPU/ONNX). Вендорен `snakers4/silero-vad` C#-пример (MIT) в `FlyleafLib/Vad/` (`SileroVadOnnxModel`/`SileroVadDetector`/`SileroSpeechSegment`; NAudio выкинут → вход `float[]`; `Dispose` чинит утечку ORT-сессии) + wrapper `AsrVadDetector` (S16→float÷32768, недеструктивный `MemoryStream.GetBuffer`, fail-soft `TryCreate`/`Detect`). `SubtitleSegmenter`: тип `SpeechSegment` + опц. параметр `speech` в `Resegment` + `SilenceClock` (снап внутренней границы к середине паузы речь/тишина в пределах `SnapToSpeechToleranceSec`=0.5с, поверх word/char тайминга; **byte-identical при `speech=null`**). `SubtitlesASR`: `SubtitleASRData.Speech`; `AudioReader` ctor `enableVadSnapping` (**только интерактивный ASR**, оба движка; батч — false → VAD не гоняется, output-identical); consumer гоняет 1 детектор на прогон (Reset per chunk) на `chunk.Stream`, сдвиг на `chunk.Start`. Config `VadCueSnapping` (default ON) + UI-тумблер + батч-снапшот паритет. Модель `LLPlayer/Assets/silero_vad.onnx` (2.22МБ, MIT, bundled), `Microsoft.ML.OnnxRuntime` 1.20.1; publish кладёт `onnxruntime.dll`(11МБ) в корень, action.yml валидирует оба. Контракты media-runtime/product-behavior/config-data/**dependency-baseline** синхронизированы. Гейты: build `-warnaserror` **0/0 ×3** + verify.ps1 full + тесты **1337→1342 (+5 RED)** + .exe 0.3.60 launch чист + **5-линз ревью SHIP 0 must-fix** (модель подтверждена v5-совместимой прямым парсингом onnx: io `input`/`state[2,?,128]`/`sr`→`output`/`stateN`, opset 16). ⚠️ **manual-smoke владельца:** faster-whisper ASR, тумблер «Snap Cue Timing to Speech» ON → границы cue садятся на паузы (лучше на музыке/шуме); OFF → как срез 1. `.exe C:\Users\Maxim\LLPlayer-build\v0.3.60-f19-slice2\`. Оговорка: снап — страховка (сливеры держит `MinCueDurationSec`), не «магия».
> **Заявка владельца (2026-07-05):** после перевода локальной моделью субтитры местами «очень быстро переключаются и не
> совпадают с речью», потом снова нормально. Идея владельца — сопоставить исходные+переведённые субтитры со звуковой
> дорожкой и подгонять переведённые точно под речь говорящего (forced alignment).
> **Диагноз (многоагентная верификация, сессия #34 — тройно подтверждён grep+Летописец+diag+verify-агенты):** корень НЕ
> перевод, а РЕ-СЕГМЕНТАЦИЯ. `SubtitleSegmenter.Resegment` при разбиении длинной cue делит время СТРОГО ПО ДОЛЕ СИМВОЛОВ
> ([`SubtitleSegmenter.cs:114-116`](../../FlyleafLib/Utils/SubtitleSegmenter.cs): `cueEnd = start + spanTicks*consumed/totalChars`,
> комментарий «Redistribute time by character proportion»), неявно предполагая постоянный темп речи. Настоящих word-level
> таймингов в конвейере НЕТ ни на одном пути: whisper.cpp выбрасывает `result.Tokens` (наружу только сегментные Start/End),
> faster-whisper парсится по СЕГМЕНТНЫМ srt-таймкодам stdout БЕЗ `--word_timestamps`. Перевод тайминг наследует 1:1
> ([`SubtitlesTranslator.cs:382-383`](../../FlyleafLib/MediaPlayer/Translation/SubtitlesTranslator.cs) пишет только
> `TranslatedText`; `TranslationCueRules.PostProcess` только переносит строки). «Местами быстро, местами нормально» =
> fast-path (cue влезает `FitsAsIs`+≤7с → тайминг=речь) vs split (внутренние границы синтетические; внешние `first.Start==start`,
> `last.End==end` уже корректны). **Дубля НЕТ:** F-08 = ручной глобальный сдвиг (DONE v0.3.17), F-12-waveform = только визуализация.
> **Решение владельца (AskUserQuestion, сессия #34):** СРЕЗ 1 + СРЕЗ 2; мелькание на **ASR-сгенерированных** субтитрах; движок =
> **faster-whisper (внешний XXL)**.
> **Файлы:** [`SubtitlesASR.cs`](../../FlyleafLib/MediaPlayer/SubtitlesASR.cs) (BuildCommand `:1460-1531`; Do-цикл/stdout-парсер
> `:1632-1815`; модель `SubtitleASRData` `:1818-1835`; регексы `SubShortReg`/`SubLongReg` `:1445-1450`), `SubtitleSegmenter.cs`
> (Resegment `:68-130`, распределение времени `:102-122`, `MergeTooShort` `:486-518`), 3 сайта вызова Resegment
> (`SubtitlesASR.cs:308`, `Batch/BatchAsrTranscriber.cs:136`, `SubtitlesManager.cs:597`).
>
> **СРЕЗ 1 — word-timestamp-driven Resegment (faster-whisper JSON). Ⓜ, CPU, 0 новых зависимостей/лицензий.**
> Верифицировано по первоисточникам (Purfview changelog + faster-whisper dataclass'ы): `--word_timestamps True` +
> `--output_format srt json` пишут ОБА файла за прогон (r194.1); JSON = ФАЙЛ `<basename>.json` в `--output_dir` (НЕ stdout),
> схема `segments[].words[]={start,end,word,probability}` стабильна; **`--sentence` НЕ затрагивает json (r194.2)** → format-риск
> снят. План: (1) BuildCommand: `--word_timestamps True` + расширить `--output_format` до `srt json` (под гейтом-тумблером; дефолт
> можно byte-identical/opt-in); (2) в `Do()` ПОСЛЕ прогона читать `<basename>.json` (сейчас srt-файл удаляется неиспользованным
> `:1810-1812` — json читать так же, чистить в finally), парсить words[]; **stdout-SRT-парсер НЕ трогать** → byte-identical,
> регексы не сломать по построению; (3) добавить `Words` в модель (`SubtitleASRData`/yield-tuple; word-тайминги +`chunk.Start`
> как у cue); (4) прокинуть words в `Resegment` (ИЗМЕНЕНИЕ СИГНАТУРЫ) — при split границы под-cue по РЕАЛЬНЫМ словам вместо
> формулы `:116`; char-proportional = fallback (нет words → byte-identical для батч/loaded); учесть взаимодействие с
> `MergeTooShort` (порог `MinCueDurationSec=1.0` всё ещё держит сливеры → «устраняет артефакт символьной пропорции», НЕ
> «гарантирует отсутствие мелькания»). Логику резки-по-словам — в internal seam FlyleafLib под RED-тесты (whisper native — на
> owner-smoke). Точность DTW ~100-400мс, хуже на музыке → отсюда ценность среза 2. whisper.cpp (токены уже в Whisper.net
> `SegmentData.Tokens[].Start/End`, единицы = сантисекунды ×10мс, `WithTokenTimestamps()`) — опц. под-срез позже.
>
> **СРЕЗ 2 — Silero VAD-snapping границ (CPU/ONNX). Ⓜ, движко-независимый; страховка там, где DTW дрейфует (музыка).**
> Верифицировано: официальный C#-пример `snakers4/silero-vad/examples/csharp` (MIT — вендорить легально), модель ~1.23-2.2МБ
> ONNX, CPU через `Microsoft.ML.OnnxRuntime` (MIT, +10-15МБ нативных бинарников). ⚠️ вход = float32 (÷32768) кадрами РОВНО по
> **512 сэмплов** @16k → `S16MonoResampler` даёт верные rate/каналы, но нужен адаптер S16→float32+framing. План: вендорить 3-4
> `.cs` (выкинуть NAudio, подключить наш `OfflineDemuxer`/`S16MonoResampler`), snap Start/End cue к ближайшей границе речь/тишина
> + не показывать текст в паузах — единый seam в Resegment покрывает все 3 ASR-сайта. GPU-lease не трогает → не конкурирует с
> F-03/F-16/F-02. Effort **M** (пример, не NuGet → сопровождать форк вручную; правка publish-скрипта под native ORT DLL).
>
> **ТИР 3 (ОТЛОЖЕН) — полный forced-alignment сайдкар (WhisperX/CTC) для ВНЕШНИХ загруженных .srt.** XL; ⚠️ GPU-конкуренция
> (НЕТ GPU-lease примитива — только 2 dub-scoped семафора); ⚠️ дефолтные веса ctc-forced-aligner/MMS = **CC-BY-NC 4.0 →
> НЕСОВМЕСТИМО с GPL-3.0**, обязательна замена на Apache-2.0 `jonatasgrosman/wav2vec2-large-xlsr-53-<lang>` (подтверждено);
> aeneas/whisper-timestamped = AGPL → только изолированным сайдкаром + явное owner OK. Семантика: align к языку АУДИО (не RU) →
> тайминг проецируется на перевод. Брать только если владелец грузит внешние .srt ИЛИ появится GPU-lease (тогда синергия с F-03).
>
> **⚠️ Frozen-гейт (Conventions 🔒 + Летописец сессии #34):** тайминг субтитров = зона `media-runtime-contract.md` +
> `product-behavior-contract.md`; замена char-proportional поведения — с owner sign-off (получен через AskUserQuestion) +
> синхронизацией ОБОИХ контрактов в PR среза. `*.cs` = utf-8-BOM (гейт Conventions). Детали: второй мозг
> `Sessions/2026-07-05-session-34-*`, авто-память `llplayer-v0358-session34-f19-subtitle-sync-research.md`.

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
> **✅ Смежный gap ЗАКРЫТ (v0.3.36, 2026-07-01):** `CreateSubtitlesConfig` теперь глубоко копирует вложенный
> `DubbingConfig` через `CloneDubbingConfig` (все settable-поля: `TtsServiceType`, `UseManualEngine`,
> `ManualVenvPython`, `Model`, `DefaultVoiceId`, `CustomVoiceIds` — независимый список, `DuckingPercent`,
> `AtempoMin/Max`, `StressNormalization`, `OutputFormat`) — как остальные вложенные конфиги, он невидим для
> скалярного reflection-guard'а `SubtitlesConfig` (тот пропускает вложенные объекты), поэтому объект снапшота
> раньше молча оставался дефолтным (дефолтный голос/пустые custom-id/дефолтные ducking/atempo). **Это
> latent-фикс / самосогласованность снапшота: снапшотный `DubbingConfig` сейчас не читает ни один потребитель**
> (живой батч-дубляж в `BatchSubtitlesDialogVM` строит `DubbingRenderer` из живого `PlayerConfig`, не из снапшота),
> т.е. рантайм-поведение не меняется — фикс закрывает гап на будущее (headless/snapshot-based дубляж) и держит
> parity с остальными вложенными клонами. + focused regression-тест (RED-without-fix доказан) + **отдельный
> reflection-completeness guard** по всем settable-полям `DubbingConfig`. Дефолтный `DubbingConfig` → snapshot
> byte-identical.
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
> **0/0 ×3** + тесты **1063/1063** (+14) + verify.ps1 green; `.exe` launch 0.3.35 чистый. **2026-07-01 monitor follow-up:** batch dubbing теперь применяет current-session `DubbingVoiceAssignmentMap` к свежим субтитрам и existing `.ru.srt` render-only path; `DubbingConfig.DefaultVoiceId`/`CustomVoiceIds` нормализуются fail-closed; packaging checks усилены positive content validation + recursive `DubEngine`/`dubmodels` rejection. Тесты **1079/1079** (+16).
> ⚙️ **Фаза 2a persistence (companion-json) — отгружена (v0.3.37, 2026-07-01).** Opt-in `Subtitles.PersistPerLineVoices` (default OFF → byte-identical): per-line назначения голоса сохраняются в файл-компаньон `video.ru.voices.json` рядом с медиа и **переживают рестарт / dub re-render**. Pure `DubbingVoiceAssignmentStore` (path-builder + tolerant JSON `ToJson`/`FromJson` + atomic `SaveAtomic` + `LoadMap`) + `DiskVoiceAssignmentProvider`/`CompositeVoiceAssignmentProvider` + `DubbingVoiceAssignmentMap.FromEntries`/`ToTimingMilliseconds`. **Решения владельца (AskUserQuestion ×2):** B1 restore-в-сайдбар + C1 явный тумблер (+ A1+A2 write-timing по рекомендации). A1 запись в `SubtitlesSidebarVM.CmdSubSetVoice`; B1 restore в `Subtitles.Load`/`EnableASR` (fill-empty, gated); batch читает диск через композит. **Грабли:** имя `.ru.dub.voices.json` (предложенное синтезом) коллизировало бы с glob `{name}.ru.dub.*` (`DubbingOutputPathBuilder`) → батч не рендерил бы дубляж → имя `.ru.voices.json` + отдельное исключение gitignore/ship/action. Тесты +20 → **1101** (после мёржа PR #109 F-05-gap), версия v0.3.37. **Остаток:** per-speaker
> (нужен F-03 диаризация), AAC-энкод, pre-render голосов — GPU/follow-up. Детали: второй мозг
> `Sessions/2026-06-30-handoff-f16-perline-voice.md` + `2026-07-01-session-LIVE-tracker-14.md`.
Дубляж — Phase 0 (PR #35 влит, CosyVoice2 в `dub_sidecar/`). SE предлагает много TTS (Edge/Kokoro/OmniVoice
voice-cloning). **Решение:** фазы 1-6 из [[2026-06-23-handoff-dubbing-mvp]] (мульти-голос, качество,
diarization-aware). **Рассуждение:** крупно; держать как продолжение существующей фичи.

---

## 3. 🧰 ТЕХДОЛГ / ИНФРАСТРУКТУРА / МЕЛКИЕ TODO

### DOC-01 — Truth sync канонического backlog и manual-smoke 🟠 ⓢ · ✅ DONE (2026-07-10, docs-only)
**Цель:** синхронизировать рабочий срез v0.3.60, baseline 1353, активные ranking/sequence и стабильные ID
для `HC-27b`/workflow-находок. Меняются только `backlog.md` и `manual-smoke-matrix.md`; app-код, workflows,
скрипты и тесты не входят в этот срез. Frozen product decisions не меняются: с явного разрешения владельца
расширяется только acceptance-матрица будущего `HC-27b`. **DoD:** docs-only diff, fast/full verify,
обязательные domain-reviewers и `/review` без Critical/Important; ручной smoke остаётся pending.
**Evidence:** `verify-fast.ps1` PASS; полный `verify.ps1` PASS (**1353/1353**, 0 warnings/errors);
instruction-drift, WPF, media-runtime, packaging и architecture reviews — SHIP после исправлений. Owner smoke не
объявлялся выполненным: это acceptance следующего app-среза `HC-27b`.

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
**Последний наблюдавшийся прогон: 1376/1376** (на 2026-07-11, HC-27b `a468c3e`, PR #142 merged `f61780c`: +23 — детерминированные OFF/latest-wins/A-B/alias/Dispose race-тесты, compact voice-index, atomic restore, stable recapture и Stop/reset generation; full verify + ship PASS; PR и post-merge Build & Test PASS). Предыдущий baseline **1353/1353**: voice-persistence queue `4d80d39` / merge-tree `be4d6ce`, +6 — `DubbingVoiceAssignmentSaveQueueTests` для двух media внутри debounce, same-media latest-wins, Dispose flush/wait и неблокирующего Enqueue. Ранее на 2026-07-06 (monitor follow-up F-19 guards): +5 — `OfflineDemuxerTests.RegisterInterrupt_DisposeUnsubscribesCancellationCallback`, `FrozenConfigDefaultsTests` для `WordTimestamps`/`VadCueSnapping`, `FasterWhisperArgsTests` для `wordTimestamps` ON/OFF. Ранее на 2026-07-03 (сессия #21, HC-40 вариант A, app-код): +1 — `ConfigCloneTests` element-distinctness тест; 2 характеризационных теста перевёрнуты в ассерты корректного deep-copy `SubConfigs` (RED-without-fix). Ранее на 2026-07-02 (сессия #20, T-03 срез №6 HC-34/39/40 + docs-sync, tests+docs-only): +17 —
`TranslateServiceHelperTests` (8: `TryGetLanguage` throw-ветки + success), `BatchSubtitleConfigSnapshotTests`
(+5: обобщённые nested-config completeness-guards HC-39), `ConfigCloneTests` (4: характеризация `Clone` HC-40).
Прод-код не менялся (версия остаётся v0.3.40). Ранее на 2026-07-02 (сессия #19, UI/краш+cleanup-бандл HC-02/03/04/06/07/32, v0.3.40): +12 —
`ParseSubtitlesTests` (+8: битый/пограничный ASS — незакрытый `{\`, `{\}`, `{\b}`/`{\u}`/`{\s}`, лидирующий `\`,
не-hex цвет `{\c&HZZ&}` через `int.TryParse` — находка ревью), `WhisperConfigNotificationTests` (+3:
`LanguageName`-уведомление сеттеров), `DubbingOutputPathBuilderTests` (+1: `.part`-огрызок не считается готовым
дубляжом) → 1175. Ранее на 2026-07-02 (сессия #18, security-бандл HC-01/05/35, v0.3.39): +30 —
`ProcessUrlSafetyTests` (15: валидация URL для аргумента процесса, инъекционные негативы),
`SafeChildPathTests` (11: safe-child-path против traversal/absolute), `NullTerminatedUtf16Tests` (4:
null-терминированный CF_UNICODETEXT-буфер) → 1163. Ранее на 2026-07-02 (сессия #16-монитор, PR #112 merge `e96c41d`): monitor follow-up +1 `GetWhisperLanguages_TitleCaseIsCultureInvariant_UnderTurkishCulture` вместе с прод-фиксом `char.ToUpper`→`char.ToUpperInvariant` в `WhisperLanguage.cs` → 1133. См. также новую секцию **§8 «Аудит здоровья кода» (HC-*)** — бэклог находок аудита сессии #16, ранжирован простое→сложное; конкретные тест-пробелы аудита — HC-34/HC-39/HC-40. На 2026-07-01 (сессия #15, v0.3.38): срез №5 +31 — `LanguageBadgeTests` (11: код/гейт сайдбар-бейджа языка, см. T-10 follow-up ниже), `UtilsFindNextAvailableFileTests` (8: next-free «name (N).ext», regex-стрип суффикса `(N)`, обе стороны границы 100 слотов — слот 100 занимается + null после 100), `ImageProcessorTests` (11: OCR `BlackText`/`AddPadding` — размеры/PixelFormat/пиксели вне блендинг-границ), culture-guard `GetWhisperLanguages_OrderIsCultureInvariant_UnderCzechCulture` (+1) вместе с прод-фиксом FS-orderby: `WhisperLanguage.GetWhisperLanguages` OrderBy теперь пиннит `StringComparer.InvariantCulture` (зеркало `Language.AllLanguages`; **RED-without-fix доказан под cs-CZ** — чешская «ch»-диграф-коллация смещала «Chinese» за H-имена; да-DK «aa»-пробник оказался вакуумным — пары различаются ДО диграфа) → 1132. SKIP-решения среза №5: `Interrupter` (frozen media-runtime + FFmpeg-callback → интеграционный путь), `SubtitlesOCR.Binarize` (private unsafe — seam не оправдан), `GetUniqueId` (тавтология Interlocked). Ранее: на 2026-07-01 (поздн.): F-16 companion-json persistence v0.3.37 +20 `DubbingVoiceAssignmentStoreTests` (ToJson/FromJson round-trip, atomic Save/LoadMap, disk/composite providers) → 1101; на 2026-07-01 (сред.): F-05-gap DubbingConfig-снапшот PR #109 +2 (regression + reflection-guard) → 1081; на 2026-07-01 (ранее): monitor follow-up добавил +16 регрессов для `DubbingConfig` normalization, `DubbingVoiceAssignmentMap`, и batch dubbing per-line voice bridge → 1079; на 2026-06-30 (поздн.): clean-up находок Codex PR #104 — корневой фикс whitespace-blank пикера голоса дубляжа через trim `DubbingConfig.DefaultVoiceId` на set + закрытие 4 тест-пробелов `VoiceBankResolver` (+7 → 1049, v0.3.34); adversarial-ревью отвергло первый вариант (raw-append в `ForConfig` вносил on-refresh-blank через `ContainsVoiceId`-дифф); на 2026-06-30 (ранее): monitor follow-up добавил +4 регресса для `DubbingConfig.CustomVoiceIds` null-normalization и `VoiceBankResolver.ContainsVoiceId` → 1042; на 2026-06-29 после F-03 prep SpeakerId PR #102 +3 и T-10 per-segment language +9 → 1038; на 2026-06-28 после T-03-среза №4 PR #98 мапперы/SSA/snapshot/Utils +100 → 1026; T-03-срез №3 PR #95 language-мапперы +70 → 915, затем F-16 ф.2 PR #96 +11 → 926; промежуточно 783→845 за счёт НЕ-T-03 срезов F-12 waveform +17 → 820 и F-16 ф.1 +25 → 845. Ранее: F-10 PR #79 → 548, F-11 PR #82 +59 → 607, T-03-срез PR #85 +114 → 721, PR #86 +4 → 725, PR #88 +58 → 783). Крупные области ещё без юнитов.
**Решение:** покрыть парсинг субтитров, перевод (моки сети), ASR/OCR (где детерминируемо),
playlist/demuxer-утилиты. Связано с фиксами B-01/B-02/B-03 (добавить регресс).
> **Следующий шаг T-03:** closure audit после накопленного owner smoke. Выбрать только non-vacuous deterministic
> seam с доказуемым RED-сценарием либо закрыть бесконечный backlog-пункт и оставить тестовое покрытие постоянной
> policy в verification gates; не добавлять тесты ради счётчика.
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
> **Follow-up «per-cue язык не потреблён дисплеем» ЗАКРЫТ (v0.3.38, 2026-07-01):** `SubtitleData.Language` теперь
> показывается read-only бейджем в сайдбаре (короткий lower-case код: ISO 639-1 где есть, 3-буквенный fallback
> для языков без него (haw/yue); tooltip = полное имя, 5-я Auto-колонка
> после voice-кнопки) при включённом `ASRPerSegmentLanguage`. Pure `FlyleafLib/Utils/LanguageBadge.cs`
> (`ToBadgeCode` null-safe к Unknown-ветке `Language.Get` с ISO6391=null + `ShouldShow`) + тонкие
> `SubLanguageBadgeConverter`/`SubLanguageBadgeVisibilityConverter` (MultiBinding: cue.Language + ЖИВОЙ конфиг-гейт
> биндингом; конфиг НЕ читается внутри конвертера — тумблер переключает бейджи вживую). **Гейт обязателен:** ASR
> штампует Language на каждую cue даже при OFF (зеркалит pinned language) → без гейта бейджи появились бы у всех
> ASR-строк при дефолте. Default OFF → byte-identical; loaded/translated cues без Language не бейджатся никогда.
> `Language` стал notifying (прецедент `AssignedVoiceId`) — поведенчески нулевая страховка. Display-only: экспорт/
> перевод/рендер на видео не тронуты. SpeakerId НЕ показывается (нет писателя до F-03-диаризации — было бы вакуумно).

### T-11 — Sandbox `dotnet`/Windows SDK + нет .NET 10 SDK у владельца 🟢 ⓢ · ✅ **DONE (PR #84, v0.3.25, 2026-06-28, doc-only)**
> ✅ **Закрыт (без кода).** Локальное окружение и процедура эскалации задокументированы в **`docs/agent/technical-stack.md`**
> (новая секция «Local Development Environment (T-11)»): нет .NET 10 SDK у владельца (есть 8/9 + **.NET 11 preview**,
> который собирает `net10.0`; `check-environment.ps1` warn'ит «10.0.x not found» — non-fatal, CI = авторитет 10.0.x);
> **sandboxed `dotnet` падает на чтении Windows SDK из AppData → запросить эскалацию и перезапустить ту же команду**
> (зеркалит `AGENTS.md` Verification Gates); + локальные грабли (PowerShell-не-Bash, `git commit -F`, `dotnet publish`
> не копирует `FFmpeg/` → `Copy-Item`, `py -3`). Frozen build-target и пин CI-SDK в `dependency-baseline.md` не тронуты.
> verify-fast green.

### T-12 — Timeout-headroom для `LiteLLM`/`OpenAILike` (follow-up B-04) 🟡 ⓢ-Ⓜ · ✅ DONE (v0.3.55, сессия #31, owner sign-off: Option A — фикс 180000)
> Вынесено из заметки внутри закрытого B-04 (сверка #17 дала свой ID против «потери следа»).
> ✅ **Сделано (v0.3.55, сессия #31):** `LiteLLMTranslateSettings`/`OpenAILikeTranslateSettings` ctors → `TimeoutMs = 180000`
> (зеркало Ollama/LMStudio/KoboldCpp); version-gated миграция `Config.MigrateOpenAiLikeTimeoutDefault` (`<= 0.3.54`, one-shot)
> бампит persisted `15000 → 180000` ТОЛЬКО для этих двух типов; явное значение пользователя сохраняется; облачный `OpenAI`/`Claude`
> вне скоупа (остаются `15000`). Option A (фикс, БЕЗ localhost-эвристики). +5 тестов; `config-data-contract.md` обновлён. Gates 0/0×2 + 1316.
**Проблема:** дефолт локальных LLM подняли до `180000` (B-04, PR #51), но `LiteLLM`/`OpenAILike` остались на базовом
`15000` — их endpoint может быть облачным, поэтому не поднимали автоматически.
**Файлы:** [`ITranslateSettings.cs`](../../FlyleafLib/MediaPlayer/Translation/Services/ITranslateSettings.cs) (дефолты
`TimeoutMs` по сервисам), `GetHttpClient`.
**Решение:** поднять headroom и для локально-направленных `LiteLLM`/`OpenAILike` (или эвристика localhost-vs-облако),
**но только с явным sign-off владельца** — эвристика «локальный ли endpoint» рискованна (ложно-облачные хосты). Идеально
совмещать с принципиальным решением B-04 (streaming + скользящий read-timeout). Пока не трогать без запроса.

### T-13 — Workflow / verification hardening 🟠 Ⓜ · IN-PROGRESS (4/7 срезов, 2026-07-17)
> Общий пакет регистрирует infra-находки; `DOC-01` только даёт им ID и не меняет workflows/scripts/ruleset.

- **T-13a — injection-safe Testing Release inputs/outputs 🟠 ⓢ · ✅ DONE (PR #144, 2026-07-11, infra-only).**
  `workflow_dispatch` ref и derived release metadata теперь попадают в PowerShell только через `env`.
  `validate-release-token.ps1` fail-closed проверяет ref/tag/hash/archive до `GITHUB_OUTPUT`; короткий SHA берётся
  только из checkout-нутого `HEAD`, `github-script` возвращает строку, upload получает quoted validated basename.
  Composite packaging action тоже читает archive input через `env` и повторно проверяет basename.
  `verify-release-workflow.ps1` закрепляет positive fixtures и негативные `;`, `$()`, `--`, CR/LF, `..`, `@{`,
  path/ref сценарии и запрещает возврат `${{ }}`-интерполяции в `run` blocks. Реальный Testing Release не запускался:
  overwrite-run остаётся owner-gated частью `T-13e`. Выполнено раньше owner smoke по прямой команде владельца;
  это не закрывает `HC-27b` acceptance. Evidence: `verify-release-workflow`/fast/full/ship PASS, 1376/1376,
  CI-flake regression 20/20, три профильных `/review` — SHIP без Critical/Important.
- **T-13b — `verify-fast.ps1` в Build & Test 🟠 ⓢ · ✅ DONE (PR #145, 2026-07-12, infra-only).**
  `build.yml` запускает fast gate после Setup .NET 10 и до restore/build/test; fail-closed validator закрепляет
  иерархию, порядок, SDK input и отсутствие `if`/`continue-on-error`/custom-shell/defaults/duplicate-key обходов.
  Обычный [Build & Test run 29194483844](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29194483844)
  зелёный на feature head `5fff787`: fast gate, restore, app/plugin build и 1376 тестов прошли. Отдельный
  intentional-red proof [PR #146](https://github.com/Gorgutc/LLPlayer_ru/pull/146) на head `cb80d23` дал ожидаемый
  красный [run 29194603639](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29194603639): шаг
  `Verify fast repository gates` упал с `plugin.json name must be llplayer-codex`, а restore/build/test были skipped.
  Proof PR закрыт без merge, временная ветка удалена, feature history чиста. Локальные fast/full/ship PASS,
  1376/1376; профильные `/review` — SHIP без Critical/Important.
- **T-13c — full-verify routing для всех app/project paths 🟡 ⓢ-Ⓜ · ✅ DONE (PR #148, 2026-07-17, infra-only).**
  `audit-frozen.ps1` теперь выдаёт cumulative extension floors и structured route-массивы; любые tracked или новые
  `*.cs`, `*.xaml`, `*.csproj`, `*.sln`, `*.slnx` получают literal `verify` и обязательных reviewers, а более узкие
  WPF/media/native/packaging правила только добавляются. Exhaustive behavioral guard динамически проверяет весь
  tracked-набор и positive/near-miss/adversarial fixtures, включая case/slash variants и exact agent/gate IDs.
  Intentional red до floors: **213** requirement gaps, **65** без literal `verify` (**63** fast-only + **2** ship-only,
  где `ship` уже включал full verify). Final: **477/477** routes, 0 без `verify`/`verification_reviewer`; локальные
  fast/full/ship PASS, **1376/1376**; feature-head [run 29604134291](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29604134291)
  и post-merge [run 29604405369](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29604405369) GREEN на точных SHA;
  профильные reviews и финальный `/review` — SHIP, 0 Critical/Important/Minor.
- **T-13d — required `Build & Test` status check 🟡 ⓢ · BLOCKED (owner decision).** Ruleset защищает deletion/
  non-fast-forward, но не требует CI check. **DoD:** владелец явно принимает или отклоняет required check;
  при принятии ruleset блокирует merge без успешного `Build & Test`.
- **T-13e — release preflight + controlled runs 🟡 Ⓜ · TODO (runs BLOCKED: owner approval).** Stable/Testing
  Release ещё не доказаны реальным run, а packaging action не делает fresh full verify перед archive. Добавление
  preflight — actionable; только фактические release-runs требуют разрешения. **DoD:** full verification перед packaging,
  затем отдельный owner-approved controlled run для **каждого** workflow с сохранёнными evidence: Stable проверяет
  tag/draft-release tail, Testing — dispatch/overwrite-upload tail. Пока хотя бы один путь не проверен, `T-13e`
  остаётся открытым; оба запуска — только с явного разрешения владельца.
- **T-13f — проверка hook targets 🟢 ⓢ · TODO.** `verify-plugin.ps1` проверяет наличие `.codex/hooks.json`, но не
  разбирает команды и не подтверждает существование их `-File`. **DoD:** fail-closed parse всех Windows hooks;
  каждый target существует внутри repo и разрешается однозначно.
- **T-13g — изоляция write-token от выбранного release ref 🟠 Ⓜ · ✅ DONE (PR #147, 2026-07-16, infra-only).**
  Testing Release разделён на четыре fresh GitHub-hosted job: trusted `prepare`, selected-ref `build`, trusted `verify`
  и узкий `upload`. Build/package выполняется только с `contents: read`; selected ref один раз разрешается в полный SHA.
  Verify job с `contents: read` принимает fixed-name unverified artifact, fail-closed проверяет единственный непустой
  regular `.7z` и перевыпускает его под отдельным fixed verified-name. Только upload job получает `contents: write`;
  он не checkout-ит и не исполняет selected code/archive, повторно проверяет verified artifact текущего run и передаёт
  validated absolute path фиксированной команде `gh release upload`. Все внешние Actions закреплены полными commit SHA.
  Structural validator с exact allowlists, adversarial mutations и filesystem fixtures включён в fast/full/ship.
  [Run 29526902608](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29526902608) зелёный на implementation head
  `61f7e33`: fast, restore, app/plugin build и 1376 тестов прошли. Локальные fast/full/ship PASS; три профильных review
  и финальный `/review` — SHIP без Critical/Important/Minor. Фактический overwrite-run не запускался и остаётся
  owner-gated частью `T-13e`; проверенная граница защищает token/transport, но не аттестует содержимое выбранной сборки.

---

## 4. 📊 АКТИВНОЕ РАНЖИРОВАНИЕ ПО ВАЖНОСТИ (убыв., as-of 2026-07-17 / v0.3.61)

| # | ID | Следующий результат | Важн. | Сложн. | Статус |
|---|----|---------------------|:---:|:---:|--------|
| 1 | **HC-27b** | Targeted owner smoke: A/B, latest, OFF, clear, exit/restart, responsiveness | 🟠 | Ⓜ | IN-PROGRESS — automated slice merged via PR #142; owner acceptance pending |
| 2 | **T-13e** | Fresh full verify перед packaging; controlled runs отдельно owner-approved | 🟡 | Ⓜ | следующий agent-action: preflight; runs BLOCKED |
| 3 | **T-03** | Closure audit: доказуемый seam либо постоянная coverage-policy | 🟡 | Ⓜ | ONGOING; не гнаться за счётчиком |
| 4 | **T-13f** | Hook commands и их `-File` targets проверяются fail-closed | 🟢 | ⓢ | после T-13e preflight |

**Owner-gated / не брать без решения:** `T-13d` required status check · только controlled Stable/Testing runs
из `T-13e` (сам preflight actionable) ·
GPU coordinator ADR → `F-03` → остаток `F-16`/F-19 tier 3.

**Trigger-only / deferred:** `F-02-full` Demucs — только по явному запросу; `HC-22` — до появления настоящей
точки уничтожения; `F-13` Avalonia — DEFERRED.

### Исторический снимок важности до 2026-07-01 (не использовать для выбора новой работы)

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
| 11 | ~~**F-15**~~ ✅ | Yomitan/10ten → DONE-BY-F-11 (решение владельца 2026-06-28) | 🟡 | Ⓜ-Ⓛ |
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
| 24 | ~~**T-08/T-09/T-10**~~ ✅ | fold-back/silence-split DONE PR #69; per-segment language DONE v0.3.32 | 🟢 | Ⓜ/Ⓛ |
| 25 | ~~**T-06**~~ ✅ | Дрейф документации форка → DONE PR #84 (doc-only) | 🟢 | ⓢ |
| 26 | ~~**T-05**~~ ✅ | M3-редизайн: закрыт PR #31 + opt-in M3 цвет-фундамент → DONE PR #91 v0.3.29 | 🟢 | — |
| 27 | **F-13** | Кросс-платформенность Avalonia | 🟢 | ⓍⓁ |
| 28 | ~~**T-11**~~ ✅ | Sandbox/SDK окружение (doc) → DONE PR #84 (doc-only) | 🟢 | ⓢ |

> Историческая пометка: на 2026-07-01 (v0.3.38) живыми считались T-03/F-03/F-16/F-13/F-02-full;
> `T-10` и `F-15` уже были DONE. Текущий выбор работы определяется только активной таблицей выше.

## 5. 🛠️ АКТИВНОЕ РАНЖИРОВАНИЕ ПО СЛОЖНОСТИ (возр., as-of 2026-07-17 / v0.3.61)

| # | ID | Следующий результат | Сложн. | Важн. | Статус |
|---|----|---------------------|:---:|:---:|--------|
| 1 | **T-13f** | Разрешимость hook targets | ⓢ | 🟢 | TODO |
| 2 | **HC-27b** | Targeted owner smoke после автоматизированного app-среза | Ⓜ | 🟠 | IN-PROGRESS — automated slice merged; owner acceptance pending |
| 3 | **T-13e** | Fresh full verify перед packaging | Ⓜ | 🟡 | следующий agent-action; release-runs BLOCKED |
| 4 | **T-03** | Closure audit вместо бесконечного роста счётчика | Ⓜ | 🟡 | после accumulated smoke |

**Вне actionable-очереди:** `T-13d` и controlled runs из `T-13e` требуют решения владельца; GPU ADR, `F-03` и остаток `F-16`
крупные и заблокированы GPU-lease/координатором; `F-02-full` trigger-only; `HC-22` и `F-13` DEFERRED.

### Исторический снимок сложности до 2026-07-01 (не использовать для выбора новой работы)

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
| 19 | ~~**T-08/T-09/T-10**~~ ✅ | fold-back/silence-split DONE PR #69; per-segment language DONE v0.3.32 | Ⓜ/Ⓛ | 🟢 |
| 20 | ~~**F-15**~~ ✅ | Yomitan/10ten → DONE-BY-F-11 | Ⓜ-Ⓛ | 🟡 |
| 21 | ~~**F-02 срез**~~ ✅ | ASR денойз → DONE PR #76 v0.3.23; **полный Demucs-сайдкар = STANDBY (по триггеру)** | Ⓛ | 🟠 |
| 22 | **F-03** | Диаризация (сайдкар) | Ⓛ | 🟡 |
| 23 | **F-16** | Дубляж: voice-bank/custom/per-line ✅; per-speaker/фазы 3-6 TODO | Ⓛ | 🟢 |
| 24 | ~~**F-10**~~ ✅ | Anki / Word Management → DONE PR #79 v0.3.24 | Ⓛ | 🟢 |
| 25 | ~~**F-11**~~ ✅ | Dictionary API (определения слов + авто-Anki) → DONE PR #82 v0.3.25 | Ⓛ | 🟢 |
| 26 | ~~**F-12**~~ ✅ | Аудио-waveform → DONE PR этот v0.3.28 | Ⓛ | 🟢 |
| 27 | **F-13** | Avalonia (переписывание UI) | ⓍⓁ | 🟢 |
| — | ~~**T-05**~~ ✅ | M3-редизайн: закрыт PR #31 + opt-in M3 цвет-фундамент → DONE PR #91 v0.3.29 | — | 🟢 |

> Историческая пометка: порядок на 2026-07-01 сохранён только как аудит-след; текущая сложность и статусы —
> в активной таблице выше. `T-10` и `F-15` закрыты и кандидатами не являются.

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
1. **HC-27b automated slice ✅** — app+tests коммит `a468c3e`, PR #142 merged `f61780c`, v0.3.61; local full/ship и PR/post-merge CI PASS.
2. **T-13a ✅ (выполнен вне очереди по прямой команде владельца)** — Testing Release больше не вставляет
   dispatch input/outputs в PowerShell; fail-closed validator и негативные injection fixtures входят в fast gate.
   Controlled Testing Release не запускался и остаётся owner-gated в `T-13e`.
3. **Targeted owner smoke (следующий owner-action)** — A/B, same-media latest, OFF, clear, app exit/restart и UI responsiveness по
   `manual-smoke-matrix.md`. Наблюдаемые end-to-end результаты проверяет владелец; внутренние race/save-lock
   гарантии отдельно доказывают детерминированные unit-тесты — нужны оба слоя.
4. **T-13b ✅; T-13g ✅; T-13c ✅; следующий agent-action — T-13e-preflight, затем T-13f** — оставшийся actionable
   workflow/verification hardening. `T-13d` и controlled Stable/Testing runs из `T-13e` остаются заблокированы.
5. **Accumulated owner smoke** — F-19 word/VAD ON/OFF; HC-44 ASR/waveform/external subtitles; B-05 `.ru.srt`
   + WordPopup; HC-43 cancel/re-run; T-12 slow local response.
6. **T-03 closure audit** — выбрать только non-vacuous deterministic seam либо закрепить coverage как policy.
7. **Только после owner approval:** GPU coordinator ADR, затем `F-03` → остаток `F-16`/F-19 tier 3.

**Не берём сейчас:** `F-02-full` (trigger-only), `HC-22` (нет безопасной точки teardown) и `F-13` (DEFERRED).
Перед поведенческими правками сверяться с
frozen-контрактами; для app-кода обязательны `scripts/codex/verify.ps1`, domain-reviewers и targeted smoke.

## 7. ⚙️ Процессные заметки (грабли инфры — для будущих сессий)
- **`/deep-research` харнесс упал ДВАЖДЫ** (auth 403 → server rate-limit, 0 источников): параллельный веер
  десятков веб-фетчей бьётся о лимиты. **Для конкретных репо — фетчить первоисточники самому `WebFetch`
  основным циклом** (он стабилен), generic-харнесс беречь для широкого поиска.
- **Многоагентные веера теряют верификаторы на rate-limit** (code-review подтвердил только сегментацию —
  у остальных измерений верификаторы упали). При нестабильности — снижать concurrency / верифицировать
  ключевые находки основным циклом.
- **Build-гряз:** наши локальные publish-сборки не встраивают git-SHA → см. B-01.

---

## 8. 🩺 АУДИТ ЗДОРОВЬЯ КОДА — 2026-07-02 (сессия #16), находки HC-*

> **Как это получено.** Многоагентный workflow (`wf_90189221-a97`): 13 finder-линз (threading, утечки
> ресурсов, WPF/UI, мёртвый код, дубли, логика субтитров, ASR/перевод, дубляж, конфиг/персистентность, perf,
> дрейф контрактов/гейтов, качество тестов, упаковка/native) → дедуп → **адверсариальная верификация каждой
> находки скептиками** (critical/high — 3 линзы, medium/low — 1; убивает строгое большинство опровержений) →
> критик полноты. Итог: 82 сырых → 76 после дедупа → **65 подтверждено, 11 опровергнуто**. Плюс 3 security-
> находки критика верифицированы основным циклом вручную. Прогон на v0.3.38+monitor-fixes (main `e96c41d`),
> гейты зелёные (build `-warnaserror` 0/0 ×3, тесты **1133/1133**).
>
> **Что это НЕ.** Ни одна из HC-задач не была пофикшена в сессии #16 (docs-only PR). Это чистый бэклог для
> будущих сессий. Ранжирование ниже — **от простого к сложному** (ⓢ → Ⓜ → Ⓛ, внутри тира по важности).
> App-код трогать по одной задаче/бандлу за сессию, с гейтами + adversarial-ревью + launch-тестом (как обычно).
>
> **Важность:** 🔴 критич. (RCE) · 🟠 высокая (high) · 🟡 средняя (medium) · 🟢 низкая (low). **Сложность:** ⓢ · Ⓜ · Ⓛ.
> Опровергнутые находки (11) и не-верифицированные кандидаты раунда №2 — в конце секции.

### 8a. ⓢ Простые (тир 1) — точечные правки, обычно одна-две строки

**🔴/🟠 высокая важность:**

- **HC-01 — Инъекция аргументов yt-dlp через URL 🔴 ⓢ · `Plugins/YoutubeDL/YoutubeDL.cs:523`** (security) · ✅ **DONE (v0.3.39, 2026-07-02, сессия #18)**
  - Проблема: `Arguments = $"...{Options["ExtraArguments"]} ... \"{Playlist.Url}\" ..."` — сырая интерполяция URL в
    командную строку (`UseShellExecute=false`). URL с `"` разрывает кавычки и внедряет произвольные флаги yt-dlp,
    включая `--exec <cmd>` → выполнение команды. Открытие вредоносной ссылки/плейлиста = RCE.
  - Решение: собирать аргументы через `ProcessStartInfo.ArgumentList` (без ручного квотирования) либо жёстко
    экранировать/валидировать URL (запрет `"`, только http/https-схемы) до подстановки.
  - Зачем: пользовательский ввод (в т.ч. из .m3u/ссылок) не должен управлять флагами внешнего процесса.
  - ✅ **Сделано:** новый чистый гейт `Utils.IsSafeProcessUrl(url)` (отвергает `"`, `\`, control-символы; требует
    абсолютный http/https-URI) + ранний возврат в начале `YoutubeDL.Open()` до запуска процесса. Легитимные
    (percent-encoded) URL проходят byte-identical. Тесты `FlyleafLibTests/Utils/ProcessUrlSafetyTests.cs` (15,
    вкл. инъекционные негативы `http://x/"--exec` — RED-without-fix подтверждён). adversarial `/code-review` → Approve.
- **HC-02 — `MenuAudioStreams` без `x:Shared="False"` 🟠 ⓢ · `LLPlayer/Resources/PopupMenu.xaml:17`** · ✅ **DONE (v0.3.40, 2026-07-02, сессия #19)** — добавлен `x:Shared="False"` (как у соседей).
  - Проблема: ресурс из живых `MenuItem` используется ItemsSource'ом двух меню (FlyleafBar `:219` + PopupMenu `:381`);
    соседние `MenuVideoStreams`/`MenuSubtitlesStreams(2)` намеренно помечены `x:Shared="False"`, этот — нет. WPF не
    может вставить один элемент в два визуальных дерева → пункты audio-streams мигают/пропадают в одном из меню.
  - Решение: добавить `x:Shared="False"` к `MenuAudioStreams` (как у соседних).
  - Зачем: единственный audio-меню-ресурс выпал из уже применённого паттерна — прямая UI-регрессия.
- **HC-03 — `SSAtoSubStyles`: доступы `code[1]`/`code[2]`/`s[i-1]` и `Substring` без проверки длины 🟠 ⓢ · `FlyleafLib/MediaFramework/MediaFrame/SubtitlesFrame.cs:88`** · ✅ **DONE (v0.3.40, 2026-07-02, сессия #19)** — `i>0` гард; `IndexOf('}')`+`break` при отсутствии; `code.Length<2` skip; `code.Length>2` для case b/u/s; `int.TryParse` в case `c` (не-hex payload больше не бросает FormatException — доп. находка adversarial-ревью). +8 тестов на ранее-крашившие входы.
  - Проблема: на пограничном ASS-тексте падает: незакрытый `{\` → `Substring(i, -1)` (ArgumentOutOfRange); `{\}` →
    `code[1]`, `{\b}` → `code[2]` вне границ; мёртвый гард `codeLen == -1` не срабатывает. Вход `"{\i1 Hello"`
    (без `}`) при загрузке → падение парсинга всей дорожки субтитров.
  - Решение: `int close = s.IndexOf('}', i); if (close == -1) break;` + проверять `code.Length` во всех `case` + `i > 0`.
  - Зачем: битый/усечённый ASS (частый у скачанных сабов) не должен ронять загрузку субтитров.
- **HC-04 — Temp-файл сборки `<out>.part` попадает под glob-детект `.ru.dub.*` 🟠 ⓢ · `dub_sidecar/server.py:263`** · ✅ **DONE (v0.3.40, 2026-07-02, сессия #19)** — двойная защита: C#-фильтр `.part`/`.tmp` в `DubbingOutputPathBuilder.ResolveExistingRussianDubPath` (чистит уже осевшие огрызки) + Python temp `_atomic_tmp_path` вне glob. +1 тест.
  - Проблема: атомарная запись через `movie.ru.dub.flac.part` рядом с медиа; при креше/kill во время `assemble`
    остаётся усечённый `.part`, который матчится `ResolveExistingRussianDubPath` glob'ом `{name}.ru.dub.*` →
    (а) `DubExistsAnyFormat=true` навсегда блокирует ре-рендер при `OverwriteExisting=false`; (б) auto-loader
    цепляет огрызок как аудио.
  - Решение: писать temp вне glob-паттерна (в `work_dir` или dotted `.{basename}.{pid}.tmp`) либо фильтровать
    `*.part/*.tmp` в резолвере; best-effort удаление залипших `.part` при старте рендера.
  - Зачем: одна незавершённая сборка молча ломает дубляж файла до ручной чистки.

**🟡 средняя важность:**

- **HC-05 — Path traversal при загрузке субтитров OpenSubtitles 🟠 ⓢ · `LLPlayer/ViewModels/SubtitlesDownloaderDialogVM.cs:111`** (security) · ✅ **DONE (v0.3.39, 2026-07-02, сессия #18)**
  - Проблема: `Path.Combine(subDir, sub.SubFileName)` — `SubFileName` приходит из удалённого API; `..\..\name.srt`
    или абсолютный путь пишет `.srt`/`.ass` вне temp (whitelist на строке 123 проверяет только расширение, не путь).
  - Решение: брать только `Path.GetFileName(sub.SubFileName)` и проверять, что итог остаётся внутри `subDir`.
  - Зачем: ответ стороннего/подменённого API не должен управлять путём записи на диск.
  - ✅ **Сделано:** новый чистый `Utils.GetSafeFileNameChildPath(baseDir, name)` (strip директорий через `GetFileName`
    + пост-strip гейт `IsNullOrEmpty`/`.`/`..` + финальный `StartsWith(baseFull + sep)` против traversal/абсолютных
    путей) заменил сырой `Path.Combine`; при `null` — `InvalidOperationException` (как у соседней проверки расширения).
    Тесты `FlyleafLibTests/Utils/SafeChildPathTests.cs` (11: traversal/absolute остаются внутри subDir; завершающий
    сепаратор → null). adversarial `/code-review` → Approve.
- **HC-06 — `SubtitleReader.ReadAll` разыменовывает `sub.rects[0]` без проверки `num_rects` 🟡 ⓢ (краш процесса) · `FlyleafLib/MediaPlayer/SubtitlesManager.cs:877`** · ✅ **DONE (v0.3.40, 2026-07-02, сессия #19)** — общий гард `num_rects<1` вынесен ДО `switch(sub.rects[0])` (bitmap-флаш `prevSub` сохранён дословно); зеркалит `SubtitlesDecoder`.
  - Проблема: гард `num_rects<1` только в ветке `IsBitmap && prevSub != null`. Если первый пакет bitmap-потока —
    clear/end-сегмент (`num_rects=0`, `prevSub==null`), доходит до `switch(sub.rects[0]->type)` при `rects==NULL` →
    AccessViolationException (в .NET не перехватывается → падение процесса). Живой декодер этот вход обрабатывает.
  - Решение: сразу после получения pts общий гард `if (sub.num_rects < 1) { ...; continue; }` (зеркально `SubtitlesDecoder.cs:222`).
  - Зачем: валидный вход (пустой bitmap-cue) роняет весь процесс.
- **HC-07 — `Raise(LanguageName)` передаёт значение свойства вместо имени 🟡 ⓢ · `FlyleafLib/Engine/WhisperConfig.cs:48`** · ✅ **DONE (v0.3.40, 2026-07-02, сессия #19)** — `Raise(nameof(LanguageName))` в 3 сеттерах; +3 теста на PropertyChanged.
  - Проблема: сеттеры `Language`/`LanguageDetection`/`Translate` зовут `Raise(LanguageName)` при
    `[CallerMemberName]`-сигнатуре → в `PropertyChanged` уходит текущее значение («Auto»…) как имя свойства;
    уведомление о `LanguageName` не поднимается → заголовки меню «ASR ({0})» не обновляются при смене языка.
  - Решение: заменить три вызова на `Raise(nameof(LanguageName))`.
  - Зачем: пункты меню ASR показывают устаревший язык до перезахода.
- **HC-08 — `TranslateLanguage` остаётся null при дефолте `EnglishAmerican` 🟡 ⓢ · `FlyleafLib/Engine/Config.cs:1526`** · ✅ **DONE (v0.3.42, 2026-07-03, сессия #22)** — seed-инициализатор `TranslateLanguage = Language.Get(TargetLanguage.EnglishAmerican.ToISO6391())` (сеттер по-прежнему обновляет при смене цели); +2 unit-теста (`SubtitlesConfigTranslateLanguageTests`, RED-without-fix на дефолте).
  - Проблема: `[JsonIgnore] TranslateLanguage` инициализируется только в сеттере `TranslateTargetLanguage`, а тот
    не срабатывает при равенстве дефолту → у конфига/культуры с `EnglishAmerican` поле остаётся null всю сессию
    (потенциальный NRE в потребителях, напр. `WordPopup.xaml.cs:516`).
  - Решение: сделать `TranslateLanguage` вычисляемым (`=> Language.Get(TranslateTargetLanguage.ToISO6391())`) + null-guard; регресс-тест round-trip с `EnglishAmerican`.
  - Зачем: дефолтная конфигурация оставляет производное поле неинициализированным.
- **HC-09 — Бэкфилл `Ctrl+K` (OpenCommandPalette) создаёт дубликат хоткея 🟡 ⓢ · `LLPlayer/Services/FlyleafLoader.cs:104`** · ✅ **DONE (v0.3.46, 2026-07-03, сессия #22, бандл B5)** — решение вынесено в чистый `KeyBindingBackfill.ShouldBackfill` (FlyleafLib): one-shot version-gate (`< 0.3.45`; `loadedConfigVersion` захвачен ДО version-stamp) + chord-free guard `(Key,Ctrl,Alt,Shift)` + already-present. Убирает дубликат Ctrl+K (блокировал Settings▸Keys Apply) и повторное добавление на каждый старт удалённого пользователем биндинга. +12 RED-without-fix тестов (граница версии 0.3.44/0.3.45/0.3.46, unparseable/null, chord-taken/free, Key-дискриминация K↔J). `config-data-contract.md` обновлён (бэкфилл one-shot + chord-safe). Adversarial-ревью (5 линз) — 0 находок по HC-09. Manual-smoke старта на до-0.3.45 конфиге.
  - Проблема: бэкфилл проверяет только отсутствие `ActionName==OpenCommandPalette`, не занятость аккорда, и не
    version-gated (каждый запуск). Удалил палитру и назначил Ctrl+K другому → на старте добавляется второй Ctrl+K →
    `SettingsKeys` блокирует Apply всей вкладки (`DuplicationCount==0`), первый матч затеняет палитру.
  - Решение: добавлять биндинг только если аккорд Ctrl+K свободен, и сделать бэкфилл one-shot через version-гейт.
  - Зачем: приложение само создаёт конфликт хоткеев, который блокирует настройку клавиш.
- **HC-10 — Сбой Save version-штампа внутри try загрузки → ложное «Cannot load» + `Environment.Exit(1)` 🟡 ⓢ · `LLPlayer/Services/FlyleafManager.cs:58`** (см. также `FlyleafLoader.cs:25/65`) · ✅ **DONE (v0.3.42, 2026-07-03, сессия #22)** — миграционный version-stamp Save вынесен в собственный try/catch на всех 3 сайтах (FlyleafManager + FlyleafLoader StartEngine/CreateFlyleafPlayer); сбой ЗАГРУЗКИ остаётся фатальным, транзиентный сбой ЗАПИСИ — нет (миграции идемпотентны). LLPlayer без тест-проекта → manual-smoke.
  - Проблема: миграционный `Save` version-stamp выполняется внутри того же try, что и загрузка. Конфиг валиден, но
    запись временно невозможна (файл залочен AV/OneDrive, каталог RO) → «Cannot load…, review/delete config» + Exit(1).
    Приложение не стартует, хотя конфиг цел.
  - Решение: вынести миграционный Save в отдельный try/catch (при сбое — лог + продолжить; миграции идемпотентны).
  - Зачем: транзиентная блокировка файла не должна мешать запуску с валидным конфигом.
- **HC-11 — `SevenZipBase.SetLibraryPath("lib/7z.dll")` по CWD-относительному пути 🟡 ⓢ · `LLPlayer/ViewModels/WhisperEngineDownloadDialogVM.cs:164`** · ✅ **DONE (v0.3.42, 2026-07-03, сессия #22)** — `Path.Combine(AppContext.BaseDirectory, "lib", "7z.dll")`. LLPlayer без тест-проекта → manual-smoke.
  - Проблема: относительный путь резолвится от CWD процесса; приложение нигде не делает `SetCurrentDirectory(BaseDirectory)`.
    Запуск через ассоциацию файлов/ярлык с чужим «Start in» → CWD ≠ папка установки → `SevenZipLibraryException`,
    распаковка Whisper-движка ломается.
  - Решение: `SevenZipBase.SetLibraryPath(Path.Combine(AppContext.BaseDirectory, "lib", "7z.dll"))`.
  - Зачем: native-либа должна грузиться от каталога приложения, а не от CWD.
- **HC-12 — Дефолтный пин yt-dlp `2025.08.20` протух 🟡 ⓢ · `.github/actions/build-package/action.yml:10`** · ✅ **DONE (v0.3.48, 2026-07-03, сессия #22, бандл B8)** — `build-package/action.yml`: input `yt-dlp-version` default → `''`; новый шаг `Resolve yt-dlp version` тянет latest через GitHub API (`releases/latest` + `github.token`, логирует версию), пишет в `GITHUB_OUTPUT`, Download использует `steps.fetch-yt.outputs.version`; явный input по-прежнему пиннит (reproducible-build). Заменил закомментированный `@master`-экшен (supply-chain-плюс). **Ревью-Minor (GitHub Actions script-injection) исправлен:** input/output читаются из env-переменных (НЕ `${{ }}`-интерполяция в pwsh-строку) + fail-closed валидация формата версии. Проверяется в CI.
  - Проблема: экшен качает `yt-dlp.exe` версии из input с default `2025.08.20`, но ни один release-workflow input не
    передаёт → все релизы пакуют ~годовой давности бинарь, который почти гарантированно не работает на актуальном
    YouTube (nsig/player). Самообновления (`-U`) в коде нет.
  - Решение: вернуть шаг получения latest yt-dlp (с фиксацией версии в логе) либо регламент бампа default перед
    релизом (+ проверка «не старше N месяцев» в `ship.ps1`).
  - Зачем: онлайн-видео (ключевая фича плагина) ломается «из коробки» в свежих релизах.
- **HC-13 — `DuckingPercent=0` молча превращается в 15 на стороне сайдкара 🟡 ⓢ · `dub_sidecar/server.py:209`** · ✅ **DONE (v0.3.48, 2026-07-03, сессия #22, бандл B7)** — вынесен чистый `_resolve_ducking(raw)` (None→15% дефолт, explicit 0→полный mute оригинала, clamp [0,100]) вместо `max(1, min(100, int(req.get("ducking_percent") or 15)))`, где `or 15` глотал 0, а `max(1,…)` запрещал 0. Lockstep с C# `DubbingConfig.DuckingPercent` (допускает 0) восстановлен. Дефолт (None/15) byte-identical. Проверено 12 stdlib-assertions чистой логики; DSP-микс — owner manual-smoke. Adversarial-ревью (3 линзы): 0 находок.
  - Проблема: C# допускает 0 («заглушить оригинал»), Python `int(req.get("ducking_percent") or 15)` — 0 falsy →
    подставляет 15; и `max(1, …)` запрещает 0. Ducking 0% → оригинал звучит на 15% без ошибки.
  - Решение: `dp = req.get("ducking_percent"); dp = 15 if dp is None else int(dp); ducking = max(0, min(100, dp))/100.0`.
  - Зачем: настройка молча не соблюдается (lockstep C# ↔ Python нарушен).
- **HC-14 — `assemble_real` игнорирует `total_ms`: хвостовые реплики обрезаются/выпадают 🟡 ⓢ · `dub_sidecar/server.py:229`** · ✅ **DONE (v0.3.48, 2026-07-03, сессия #22, бандл B7)** — вынесен `_timeline_len(original_n, total_ms, rate) = max(original_n, ceil(rate*total_ms/1000))`; оригинал zero-паддится до `timeline_n`, а `bed`-размер/`end`-кламп/гейт `0≤off<…` переведены с `total_n` на `timeline_n` → хвостовой клип с `off≥original_n` больше не выбрасывается, перекрывающий конец не обрезается. `total_ms=None`/≤длины оригинала → byte-identical. Проверено stdlib-assertions; DSP — owner manual-smoke (sync near-end по `manual-smoke-matrix`). Adversarial-ревью (3 линзы): 0 находок.
  - Проблема: `bed = np.zeros(total_n)` по длине декодированного оригинала; клип с `off >= total_n` выбрасывается,
    перекрывающий конец — обрезается. Субтитр у конца файла + русская реплика длиннее слота → последняя фраза
    дубляжа обрывается/отсутствует молча. Мок-путь `total_ms` использует честно.
  - Решение: размер тайм-линии `max(len(original), ceil(rate*total_ms/1000))`, оригинал допаддить нулями.
  - Зачем: конец фильма систематически теряет дубляж.
- **HC-15 — Второй рукописный SRT-сериализатор в батче без защит `SubtitleExporter` 🟡 ⓢ · `FlyleafLib/MediaPlayer/Batch/SrtSubtitleWriter.cs:42`** · ✅ **DONE (v0.3.47, 2026-07-03, сессия #22, бандл B6)** — `SrtSubtitleWriter` теперь маппит `SubtitleData`→`SubtitleExportLine(Start,End,DisplayText??Text??"",Styles:null)` и сериализует общим `SubtitleExporter.Build(…,Srt)` (несёт `NormalizeCueText` — дроп in-cue blank-line, ломавшего SRT-парсер на выводе LLM при `ResegmentSubtitles=Off` — и `InvariantCulture`-тайминги), сохраняя atomic temp+move + per-run GUID-temp. Well-formed однострочные cue на Windows byte-identical (`WriteLineAsync` уже давал CRLF). +5 тестов (`BuildSrtContent` map + I/O RED-without-fix на blank-line + overwrite/atomic). Adversarial-ревью (5 линз): 1 REFUTED-концерн о RED (I/O-тест подтверждён настоящим RED).
  - Проблема: `SrtSubtitleWriter` заново пишет SRT без `NormalizeCueText` (blank-line guard) и `InvariantCulture`,
    хотя есть чистый `SubtitleExporter.BuildSrt`. LLM вернул текст с пустой строкой, `ResegmentSubtitles=Off` →
    пустая строка внутри cue терминирует cue в SRT → рассинхрон парсера у переведённого файла.
  - Решение: переиспользовать `SubtitleExporter` (маппинг `SubtitleData`→`SubtitleExportLine`) через atomic temp+move;
    минимум — звать общий `NormalizeCueText`/`FormatTime`.
  - Зачем: батч-путь беднее интерактивного и портит SRT на реальном выводе LLM.
- **HC-16 — `verify-frozen.ps1` не пиннит frozen-дефолты `ASRPerSegmentLanguage=false`/`PersistPerLineVoices=false` 🟡 ⓢ · `scripts/codex/verify-frozen.ps1:136`** · ✅ **DONE (v0.3.48, 2026-07-03, сессия #22, бандл B8)** — `verify-frozen.ps1` +2 `Require-Text` по образцу `ASRFoldBack` (строки 137/138) пиннят `ASRPerSegmentLanguage`/`PersistPerLineVoices` `\{…\} = false;` в `Config.cs` — НЕ дублируя PR #112-строки (XAML-bind `SubtitlesSidebar.xaml` + doc-упоминание `dubbing-contract.md` — иной концерн). + C#-тест `FrozenConfigDefaultsTests` (оба рантайм-дефолта false, belt-and-suspenders к source-grep гейта). `verify-frozen.ps1` exit 0, тесты 1236→1238.
  - Проблема: контракты объявляют «default false → byte-identical» frozen-границей (T-10 v0.3.32, F-16 v0.3.37), но
    гейт пиннит соседние дефолты (`FixAllCaps`, `ASRFoldBack`…), а два новых тумблера — нет. Случайная смена дефолта
    на true пройдёт гейт → тихая потеря byte-identical.
  - Решение: два `Require-Text` по образцу `ASRFoldBack` (+ юнит-тест на оба дефолта).
  - Зачем: заявленный инвариант без автопроверки.
  - ℹ️ **Обновление (сверка #17, 2026-07-02):** PR #112 добавил в `verify-frozen.ps1` *упоминания*
    `ASRPerSegmentLanguage`/`PersistPerLineVoices`, но **дефолты `false` по-прежнему не запиннены** (`Require-Text`
    как у `ASRFoldBack` нет) → задача остаётся валидной; при фиксе учесть уже добавленные строки, не дублировать.
- **HC-17 — O(n)-цикл + полный `Refresh` ListCollectionView на тоггл `EnabledTranslated` 🟡 ⓢ · `FlyleafLib/Engine/Config.cs:1084`** (perf) · ✅ **DONE (v0.3.45, 2026-07-03, сессия #22, бандл B4)** — `SubtitleData.EnabledTranslated` поле→INPC-свойство (зеркало `TranslatedText`: raise `UseTranslated` при флипе + `DisplayText`); сеттер `SubConfig` идёт по `SnapshotSubs()` (thread-safe, был прямой `foreach` по `Subs`) и вместо полного `SubManager.Refresh()` зовёт новый `RefreshAfterTranslationToggle()` — перезапуск фильтра ТОЛЬКО при активном поиске (`view.Filter != null`; ⚠️ handoff-предположение «фильтр от тоггла не зависит» **неверно** — `SubtitlesSidebarVM.SubFilter` матчит `DisplayText`) + сохранён `OnPropertyChanged(CurrentIndex)`. +3 RED-without-fix теста (INPC-контракт), 1195→1198. Adversarial-ревью (5 линз) — 0 находок по HC-17. Рендер/UI-путь — manual-smoke.
  - Проблема: сеттер (частая горячая клавиша показа перевода) на UI-потоке идёт `foreach` по ВСЕМ cue (public-поле
    без INPC → строки не обновятся) и зовёт `SubManager.Refresh()` → полная перестройка view (O(n)-копия под sync-локом,
    пере-прогон фильтра, регенерация контейнеров). На длинном файле — заметный фриз на каждое нажатие.
  - Решение: сделать `EnabledTranslated` INPC-свойством `SubtitleData` (обновятся только видимые строки), цикл под
    `_subsLocker` по снимку; минимум — `view.DeferRefresh()` и не звать `Refresh`, когда сайдбар закрыт.
  - Зачем: горячая клавиша даёт O(n) UI-работу на пустом месте.

**🟢 низкая важность (ⓢ):**

- **HC-18 — Гонки перечисления `Subs` без `SnapshotSubs()` (бандл) 🟡/🟢 ⓢ · несколько сайтов** · ✅ **DONE (v0.3.44, 2026-07-03, сессия #22, бандл B3)** — `SnapshotSubs()` на всех 4 сайтах: AiInsightsDialogVM (`cues` + `_hasText`), SubtitlesExportDialogVM, `Subtitles.cs` OCR-`Do`; CmdSubPlay/CmdSubSync (SubtitlesSidebarVM) — snapshot + bounds-check индекса (защита от ArgumentOutOfRange при усадке `Subs` между UI-командой и хендлером). Adversarial-ревью (3 линзы) — 0 находок; нормальный случай byte-identical. Manual-smoke.
  - Проблема: контракт `SnapshotSubs()` (`SubtitlesManager.cs:208-213`) требует читать `Subs` только под `_subsLocker`
    (`EnableCollectionSynchronization` защищает лишь WPF-биндинг, не app-`foreach`). Прямое перечисление во время
    фонового ASR/OCR-`Add`/`Clear` → `InvalidOperationException`/`ArgumentOutOfRange` на UI. Сайты:
    `AiInsightsDialogVM.cs:118/311` (HC-18a), `SubtitlesExportDialogVM.cs:57` (HC-18b),
    `SubtitlesSidebarVM.cs:154/165` CmdSubPlay/Sync по индексу (HC-18c), `Subtitles.cs:655` OCR-`ToList()` (HC-18d).
  - Решение: во всех — `SnapshotSubs()` (или per-row `{Binding .}` вместо Index для Play/Sync).
  - Зачем: повтор уже известного класса багов; частичный экспорт/AI-инсайты во время ASR роняют UI.
- **HC-19 — Утечка per-chunk CTS и `token.Register` в FasterWhisper 🟢 ⓢ · `FlyleafLib/MediaPlayer/SubtitlesASR.cs:1707`** · ✅ **DONE (v0.3.43, 2026-07-03, сессия #22, бандл B2)** — site 1 (per-chunk `forceCts`+`token.Register` в FasterWhisper `Do`) → `using`-декларации (диспоз при завершении чанка); site 2 (linked CTS в `AudioReader.ReadAll`) → dispose в `finally` ПОСЛЕ `Task.WaitAll` fault-continuations (race-free vs `cts.Cancel()`). Adversarial-ревью (concurrency+build линзы) — чисто. Native/process path → manual-smoke.
  - Проблема: на каждый аудио-чанк `new CancellationTokenSource` + `token.Register(...)` не диспозятся (finally чистит
    только temp-файлы); `token` — на весь прогон → на 2-3ч фильме сотни живых регистраций/CTS; при отмене все
    накопленные колбэки армят таймер в мёртвом CTS. Смежно: linked CTS в `AudioReader.ReadAll:504` без Dispose.
  - Решение: `using CancellationTokenSource forceCts` + `using CancellationTokenRegistration reg = token.Register(...)`;
    обернуть linked cts в try/finally с Dispose.
  - Зачем: монотонный рост памяти на длинном/батч-ASR.
- **HC-20 — Языкодетект faster-whisper: гейт глотает сегменты + lookup индексером роняет прогон 🟢 ⓢ · `FlyleafLib/MediaPlayer/SubtitlesASR.cs:1788`** · ✅ **DONE (v0.3.44, 2026-07-03, сессия #22, бандл B3)** — индексер `LanguageToCode[name]` → `TryGetValue` с фолбэком в `_manualLanguage` (не роняет на неизвестном языке); `continue` сужен ДО строки детекта (внутрь `if(match.Success)`) → реальные cue больше не съедаются при `LanguageDetection=true` + `--language` в ExtraArguments; yield `(_detectedLanguage ?? _manualLanguage)` (cue не несёт null-язык). Adversarial-ревью — чисто; нормальный путь детекта byte-identical. Manual-smoke.
  - Проблема: при `_isLanguageDetect && _detectedLanguage==null` каждая stdout-строка уходит в `continue` до строки
    «Detected language …». Если `LanguageDetection=true`, но пользователь передал `--language xx` в `ExtraArguments`,
    строки детекта не будет → все cue съедаются, прогон «успешно» пуст; плюс lookup языка индексером бросает на
    неизвестном коде.
  - Решение: `continue` только для реальной строки детекта/нематча; lookup через `TryGetValue` с фолбэком в `_manualLanguage`.
  - Зачем: легальная power-user-ручка приводит к молча пустому ASR.
- **HC-21 — PGS: коррекция `EndTime` зависит от наличия `Bitmap` → cue длиной +49.7 дней 🟢 ⓢ · `FlyleafLib/MediaPlayer/SubtitlesManager.cs:866`** · ✅ **DONE (v0.3.42, 2026-07-03, сессия #22)** — raw `end_display_time` трекается в локали `prevEndDisplayTime` → коррекция «до следующего пакета» работает и при `useBitmap=false` (где `prevSub.Bitmap==null`); последний cue с sentinel `uint.MaxValue` клампится (StartTime+5s). Текст-субтитры byte-identical. Native decode-loop → manual-smoke + adversarial-ревью.
  - Проблема: при `end_display_time==uint.MaxValue` EndTime исправляется по следующему пакету только если
    `prevSub.Bitmap?...==uint.MaxValue`; при `useBitmap=false` (режим только таймстемпов) `Bitmap==null` → конец
    остаётся ~49.7 дней → перекрытие cue, вечное `Showing`, битый prev/next-интервал.
  - Решение: хранить `end_display_time` независимо от `useBitmap` и корректировать по нему; последний cue клампить.
  - Зачем: некорректные тайминги субтитров в bitmap-режиме без кэша.
- **HC-22 — `WordPopup`: сервисы/`_cts` не освобождаются при уничтожении контрола 🟢 ⓢ · `LLPlayer/Controls/WordPopup.xaml.cs:137`** · ⏸️ **DEFERRED (сессия #22, бандл B2)** — при попытке фикса adversarial-ревью выявил: `WordPopup` живёт внутри `NonTopmostPopup`, а WPF Popup поднимает `Unloaded` на КАЖДОЕ закрытие (video resume / Esc / Close), не только при уничтожении → teardown в `Unloaded` затирал бы translate/definition-кэши на каждом закрытии (регрессия cache-miss). Weak-event подписки (ctor) уже НЕ рутят контрол → сервисы/`_cts` GC-собираемы; детерминированной точки teardown нет (overlay-хост живёт всю сессию; sidebar-хост уже диспозится через `SubtitlesSidebar.Unloaded→VM.Dispose`). Оставлено как GC-ограниченная мягкая утечка; правка отклонена как net-negative. Переоткрыть только с настоящей точкой уничтожения.
  - Проблема: `_translateService`/`_wordDefinitionService` (каждый владеет HttpClient, для LLM — со своим handler вне
    общего пула) диспозятся только в `Clear()` при смене настроек; при уничтожении WordPopup (сайдбар пересоздаётся
    из DataTemplate на каждый toggle) — нет teardown → утечка соединений до GC.
  - Решение: `Unloaded`-хендлер (или `Teardown`) с `Clear()` + `_cts?.Cancel()/Dispose()` + dispose `_pdicSender`.
  - Зачем: повторные toggle сайдбара при LLM-переводе слов копят соединения.
- **HC-23 — PDIC: pipe-процесс спавнится на каждый WordPopup и не убивается 🟢 ⓢ · `LLPlayer/Controls/WordPopup.xaml.cs:349`** · ✅ **DONE (v0.3.43, 2026-07-03, сессия #22, бандл B2)** — `PDICSender` → DI-синглтон (один общий pipe-процесс вместо N per-WordPopup); диспоз ЯВНО в `App.OnExit` через `PDICSender.Current` (Prism/DryIoc НЕ диспозит контейнер на выходе — подтверждено декомпиляцией Prism 9.0.537 в adversarial-ревью), без bare-`Resolve` (не плодит pipe на выходе, если PDIC не использовался); `Dispose` синхронный+bounded, `PipeClient.SendMessage` +`ConfigureAwait(false)` (нет sync-over-async дедлока UI-треда на выходе). Ревью 2 раунда (5/5 проверок OK). **Известное ограничение (Minor):** синглтон кэширует первый exe-путь на сессию (смена `PDICPipeExecutablePath` в рантайме требует рестарта); hard-crash всё ещё может осиротить процесс — job-object hardening опциональный follow-up.
  - Проблема: `_pdicSender ??= Container.Resolve<PDICSender>()` (transient) в конструкторе запускает внешний exe
    PDIC-пайпа; `Dispose` не вызывается нигде, у `PipeClient` нет финализатора → процессы копятся; сам `Dispose` —
    `async void`.
  - Решение: зарегистрировать `PDICSender` синглтоном (dispose при выходе) или диспозить в `Unloaded`; переписать
    `Dispose` синхронно (try close с таймаутом / finally dispose pipe).
  - Зачем: утечка внешних процессов при использовании словаря.
- **HC-24 — `TakeSnapshotToFile`: GDI+ Bitmap не диспозится на успехе 🟢 ⓢ · `FlyleafLib/MediaPlayer/Player.Extra.cs:362`** · ✅ **DONE (v0.3.42, 2026-07-03, сессия #22)** — `using var snapshotBitmap` (диспоз на успехе И на исключении, rethrow сохранён). Manual-smoke.
  - Проблема: снапшот-битмап диспозится только в `catch`; на успешном пути (после Save) — до финализатора. Серия
    снапшотов (зажатый хоткей) → быстрый рост нативной памяти/GDI-хендлов, риск исчерпания GDI-лимита (10k).
  - Решение: `using var snapshotBitmap = …` (или try/finally).
  - Зачем: интенсивные снапшоты копят GDI-ресурсы.
- **HC-25 — AI Insights: кнопка Generate не активируется, если транскрипт появился после открытия 🟢 ⓢ · `LLPlayer/ViewModels/AiInsightsDialogVM.cs:309`** · ✅ **DONE (v0.3.46, 2026-07-03, сессия #22, бандл B5)** — подписка на `Subs.CollectionChanged` выбранного трека, пока диалог открыт (re-wire на смене слота, отписка на закрытии); при появлении текста — re-eval `_hasText` + raise `CanGenerate`. ⚠️ **Adversarial-ревью поймало Critical** первого прохода: `CollectionChanged` от фонового ASR-потока → INPC `CanGenerate` на фоновом потоке → Prism `ObservesCanExecute`→`RaiseCanExecuteChanged` синхронно → кнопка Generate трогает `IsEnabled` не на UI-потоке → `InvalidOperationException` (краш в ЦЕЛЕВОМ сценарии). Исправлено: маршалинг реакции через `Utils.UI` (BeginInvoke) + early-out `if (_hasText) return` (без флуда диспетчера) + `_hasText` volatile. Manual-smoke.
  - Проблема: `_hasText` пересчитывается только в `OnDialogOpened`/сеттере `SelectedSubIndex`. Запуск ASR + сразу
    открыть диалог → Generate остаётся серой, пока не переключить слот ①/② или переоткрыть.
  - Решение: подписаться на `PropertyChanged` выбранного SubManager (Count/IsLoading) на время жизни диалога,
    вызывать `RefreshHasText`; отписка в `OnDialogClosed`.
  - Зачем: «мёртвая» основная кнопка диалога в типичном сценарии.
- **HC-26 — `SidebarFontWeight` сохраняется/показывается, но сайдбар его не применяет 🟢 ⓢ · `LLPlayer/Views/SubtitlesSidebar.xaml:531`** · ✅ **DONE (v0.3.46, 2026-07-03, сессия #22, бандл B5)** — `FontWeight="{Binding FL.Config.SidebarFontWeight}"` на `SubtitleListBox` (string→FontWeight через неявный конвертер, как существующий FontFamily-биндинг строки). Дефолт `Normal` → byte-identical. Manual-smoke.
  - Проблема: диалог шрифта сайдбара пишет `AppConfig.SidebarFontWeight`, Settings показывает его, но список сайдбара
    биндит только FontSize/FontFamily — FontWeight нигде не привязан.
  - Решение: `FontWeight="{Binding FL.Config.SidebarFontWeight}"` на `SubtitleListBox` (либо убрать выбор веса из диалога).
  - Зачем: настройка не имеет эффекта — вводит в заблуждение.
- **HC-27 — `PersistVoiceAssignments`: синхронный I/O + двойной O(n)-снимок на UI-потоке 🟢 ⓢ · `LLPlayer/ViewModels/SubtitlesSidebarVM.cs:210`** (perf) · ✅ **DONE (v0.3.45, 2026-07-03, сессия #22, бандл B4)** — вся блокирующая работа (до 3×`File.Exists` SMB + 2×O(n) `SnapshotSubs` + JSON-запись) ушла с UI-потока в debounce-`Task.Run` (400мс, коалесинг по generation через `Interlocked`); flush на `Dispose` (`_voicesDirty`) чтобы не терять правку при teardown. ⚠️ **Adversarial-ревью поймало Important-регрессию durability** первого прохода: debounce гейтил только планирование, не выполнение — на медленном SMB старая generation-запись могла завершиться ПОСЛЕ новой, затерев последнюю правку (GUID-temp защищает от порчи, не от порядка). Исправлено: общий `_voicesSaveLock` сериализует записи + повторная проверка generation под локом (записи не регрессируют, last-edit-wins). Manual-smoke. **Исторический DONE относится к v0.3.45. В `d83efa5` immutable capture ради корректной смены media вернул `File.Exists` и полные subtitle-снимки на dispatcher; `4d80d39` сохранил этот capture, добавил очередь с повторным clone и утратил opt-in re-check непосредственно перед записью. Поэтому исходное утверждение «вся блокирующая работа ушла» больше не описывает live-код — см. `HC-27b`.**
  - Проблема: `CmdSubSetVoice` на UI-потоке синхронно: до 3× `File.Exists` (для SMB — блокирующие сетевые), `SnapshotSubs`
    копирует ОБА трека (2×O(n) под локом, блокируя per-frame скример), `SaveAtomic` пишет JSON. Сетевой файл + 5000+ cue
    + `PersistPerLineVoices=on` → фриз на каждый выбор голоса.
  - Решение: быстрый снимок только override-cue на UI, а `File.Exists`/сериализацию/запись — в `Task.Run` с debounce.
  - Зачем: назначение голоса замораживает UI на больших/сетевых файлах.
- **HC-27b — Voice-save queue: OFF-race + UI capture 🟠 Ⓜ · IN-PROGRESS — automated slice merged (v0.3.61, PR #142, `f61780c`, 2026-07-11); owner acceptance pending**
  - **Почему follow-up отдельный:** `HC-27` остаётся исторически закрытым срезом v0.3.45, но рефакторинг
    capture в `d83efa5` и последующий `DubbingVoiceAssignmentSaveQueue` в `4d80d39` изменили реализацию и выявили
    новые проверяемые границы: UI-I/O/сканы происходят из первого среза, повторный clone и поздний OFF race — из второго.
  - **Проблема 1 — opt-in race:** `_isEnabled()` проверяется до ожидания глобального `_saveLock`; если другой save
    держит lock, пользователь выключает `PersistPerLineVoices`, а уже claimed request затем всё равно вызывает `_save`.
  - **Проблема 2 — UI capture:** `CreateVoiceAssignmentSaveRequest` вызывается с WPF dispatcher и всё ещё делает до
    трёх `File.Exists`, два `SnapshotSubs` и минимальный clone; очередь после этого повторно клонирует snapshot.
  - **Пробел тестов:** шесть существующих queue-тестов не покрывают queued-behind-save → OFF и
    same-media old-in-flight → final latest state.
  - **Реализация:** повторный OFF/latest check стоит непосредственно под save-lock; media identity захватывается
    без I/O и разрешается на worker; per-track compact index копирует только назначенные cue; immutable snapshots,
    A/B isolation, stale ContextMenu и Stop/open generation защищены отдельными revision/generation guards.
  - **Evidence:** RED-before-fix для трёх исходных дефектов; +23 теста (race/alias/index/restore/reset), targeted
    49/49, full **1376/1376**, build 0 warnings/errors, `verify-fast`/`verify`/`ship` PASS; обязательные WPF,
    media-runtime, .NET, native и packaging review — SHIP, 0 Critical/Important.
  - **DoD:** повторно проверить opt-in непосредственно под save-lock перед `_save`; убрать с dispatcher filesystem
    probes, повторный clone и полные O(n)-сканы обоих subtitle-треков на каждую правку (либо заменить их доказуемо
    ограниченным incremental capture), сохранив immutable capture-at-edit и корректность при смене media; добавить детерминированные race/latest-wins тесты;
    full verify + targeted owner smoke из `manual-smoke-matrix.md`. Автоматизированная часть выполнена; наблюдаемый
    owner smoke остаётся обязательным и не подменяется unit-тестами.
- **HC-28 — Bing/Microsoft: отменённая задача access-токена залипает в кэше 🟢 ⓢ · `FlyleafLib/MediaPlayer/Translation/Services/MicrosoftTranslateServiceBase.cs:177`** · ✅ **DONE (v0.3.42, 2026-07-03, сессия #22)** — **постоянный баг уже был закрыт ранее** (eviction-гарды: 401 compare-and-clear + faulted-task eviction в `catch`); остаточное упрочнение — общий fetch токена с `CancellationToken.None` (был токен вызывающего) снимает и разовый сбой + thrash после отмены. Каждый вызывающий бейлит через `WaitAsync(token)`. Network/abstract path → покрыт корректностью + ревью.
  - Проблема: `GetAccessTokenTask` кэширует Task с токеном первого вызывающего; если Cancel пришёл во время первого
    fetch, canceled-task остаётся в `_accessToken` → следующий перевод детерминированно фейлится «A task was canceled».
  - Решение: в OCE-ветке compare-and-clear кэша перед throw (или fetch с `CancellationToken.None` + `WaitAsync(token)`).
  - Зачем: один отменённый seek ломает переводчик до перезахода.
- **HC-29 — `AtempoMin/AtempoMax` не валидируются 🟢 ⓢ · `FlyleafLib/Engine/DubbingConfig.cs:51`** · ✅ **DONE (v0.3.47, 2026-07-03, сессия #22, бандл B6)** — сеттеры `AtempoMin/Max` клампят в [0.5, 2.0] (`ClampAtempo`, действует и на STJ-десериализацию) так, что typo `0.15`/`0`/negative не доходит до `librosa.time_stretch` (rate≤0 бросает → весь дубляж файла Failed); `DubbingIsochrony.ComputeAtempo` для overflow-реплики возвращает `Math.Max(1.0, Clamp(factor,min,max))` — mis-set max<1 больше не ЗАМЕДЛЯЕТ переполняющую реплику (не растит drift). Дефолты 0.9/1.15 в диапазоне → клампы/пол no-op, byte-identical (пиннится тестом). +14 тестов. `config-data-contract.md` уже упоминал «atempo range» обобщённо → правки frozen-контракта не потребовалось.
  - Проблема: свободные TextBox без клампа. `AtempoMax<1` → переполняющие реплики ЗАМЕДЛЯЮТСЯ (drift растёт); `≤0` →
    `librosa.time_stretch(rate<=0)` бросает → 500 → весь дубляж файла Failed. Опечатка `0.15` вместо `1.15`.
  - Решение: в `ComputeAtempo` для `clipMs>slotMs` возвращать `Math.Max(1.0, Clamp(...))`; клампить `AtempoMin/Max` в
    сеттерах (напр. 0.5..2.0, min≤max).
  - Зачем: опечатка в настройке ломает весь дубляж.
- **HC-30 — Отмена/таймаут ожидания порта сайдкара маскируется под `InvalidOperationException` 🟢 ⓢ · `FlyleafLib/MediaPlayer/Dubbing/DubSidecarHost.cs:152`** · ✅ **DONE (v0.3.47, 2026-07-03, сессия #22, бандл B6)** — извлечены два чистых хелпера: `ClassifyPortWaitFailure(callerCanceled, processHasExited)` → `{Canceled, Timeout, ExitedEarly}` и `BuildPortWaitException(fault, stderr)` → `{OperationCanceledException, TimeoutException("…within 120 seconds."), InvalidOperationException("exited before…")}`. Отмена батча теперь = Canceled (не Failed), 120с-таймаут = отдельное сообщение, «exited before…» только при `_process.HasExited`. Ветка переписана без `goto` (ревью-Minor: маппинг был непокрыт → вынесен и запиннен). +7 тестов (классификатор + маппинг fault→exception). Хост-обвязка (реальный процесс/гонка токена) — manual-smoke.
  - Проблема: `WaitForExitAsync(portCts.Token)` при отмене/120с-таймауте становится Canceled → бросается
    «sidecar exited before reporting a port» при живом процессе → джоб `Failed` вместо `Canceled`.
  - Решение: перед ошибкой `token.ThrowIfCancellationRequested()` + отдельное сообщение на таймаут; «exited before…»
    только при `_process.HasExited`.
  - Зачем: отмена батча выглядит как ошибка сайдкара.
- **HC-31 — `OutlinedTextBlock`: безусловный `UpdatePen()` в Measure/Arrange 🟢 ⓢ · `LLPlayer/Controls/OutlinedTextBlock.cs:323`** (perf) · ✅ **DONE (v0.3.45, 2026-07-03, сессия #22, бандл B4)** — `UpdatePen()` пересоздаёт `Pen` только по cache-key `(Stroke, StrokeThickness, StrokePosition)` + `Freeze()` (фолбэк `CanFreeze` для незамораживаемых кистей) и зовёт `InvalidateVisual()` лишь при реальной смене; repaint-на-смену-геометрии перенесён в `_geometryKey`-блок `ArrangeOverride`. Обесценивание `_geometryKey`-гейта устранено, замороженный Pen не клонируется в рендер-поток. WPF-рендер — manual-smoke (визуально идентично).
  - Проблема: `ArrangeOverride` безусловно, `MeasureOverride` при `StrokeThicknessInitial>0` (дефолт 3) зовут
    `UpdatePen()` → новый незамороженный `Pen` + `InvalidateVisual()` на каждый layout-проход каждого слова,
    обесценивая `_geometryKey`-гейт; незамороженный Pen требует клона в render-поток.
  - Решение: пересоздавать Pen только при смене входов (кэш-ключ), `pen.Freeze()`, не звать `InvalidateVisual` из
    Measure/Arrange без изменений.
  - Зачем: субтитр-оверлей с многими словами перерисовывается на пустом месте.
- **HC-32 — Bundle «мёртвый код» 🟢 ⓢ · один PR-чистка** · ✅ **DONE (v0.3.40, 2026-07-02, сессия #19)** — удалено ~440 строк: `FindIndex`, кластер `Utils.cs` (`AddFirewallRule`/`FindFileBelow`/`GetUserDownloadPath`/`DownloadFile`×2/`GetGPUCounters`+`GetGPUUsage`/`GZipDecompress`), мёртвые P/Invoke `NativeMethods.cs` (`GetWindowRgn`/`GetClientRect`/`GetWindowInfo`+`WINDOWINFO`/`SetForegroundWindow`), закомм. `SeekSubtitles`, `PDICSender.Connect()`, зомби `SavedSession`, файл `ZOrderHandler.cs`. build `-warnaserror` = верификация. **✅ Остаток `VideoConfig.SwsForce` УДАЛЁН (v0.3.41, 2026-07-03, сессия #21, owner-decision вариант A):** мёртвое сериализуемое public-свойство `VideoConfig` (`Config.cs`, не читалось) удалено; STJ самомигрирует (`JsonUnmappedMemberHandling=Skip` по умолчанию → лишнее поле в старом `Config.json` игнорируется при загрузке и исчезает при следующем `Save`); `config-data-contract.md` свойство не упоминал → правки frozen-контракта не потребовалось.
  - Проблема: подтверждено grep'ом по всему репо (вкл. XAML/DryIoc/JSON-сериализацию/reflection-guard) — не используется:
    `ObservableCollectionExtensions.FindIndex` (`SubtitlesManager.cs:1243`; все `FindIndex` идут на `List<T>`);
    `VideoConfig.SwsForce` (`Config.cs:846`, сериализуется, не читается — форс через `VideoProcessor=SwsScale`);
    класс `Utils.ZOrderHandler` целиком (`ZOrderHandler.cs`, ~165 стр.); кластер в `Utils.cs:172` (`AddFirewallRule`,
    `FindFileBelow`, `GetUserDownloadPath`, `DownloadFile`×2, `GetGPUCounters/GetGPUUsage`, `GZipDecompress`, ~120 стр.);
    мёртвые P/Invoke `NativeMethods.cs:100` (`GetWindowRgn`/`GetClientRect`/`GetWindowInfo`+`WINDOWINFO`/`SetForegroundWindow`
    + 2 закомм.); зомби-метод `SeekSubtitles` `DecoderContext.cs:309` (+вызовы :566/:588-601/:259-268, `Player.Open.cs:820`);
    `PDICSender.Connect()` (`PDICSender.cs:73`); зомби-блок `SavedSession` `Session.cs:23-34` (ссылается на несуществующее
    свойство). ⚠️ Каждое удаление подтвердить своим grep'ом непосредственно перед правкой (репо живёт).
  - Решение: точечные удаления одним cleanup-PR (build `-warnaserror` = верификация).
  - Зачем: ~500 строк мёртвого кода вводят в заблуждение и раздувают поверхность сопровождения.
- **HC-33 — Bundle «дрейф контрактов/доков/гейтов» 🟢 ⓢ · docs/tooling-PR** · ✅ **DONE (v0.3.41, 2026-07-03, сессия #21, инфра-коммит)** — (1) `audit-frozen.ps1`: добавлено правило маршрутизации `^docs/agent/(dubbing-contract\.md|dubbing/)` → `media_runtime_mapper`/`native_dependency_auditor`/`packaging_release_reviewer`/`verification_reviewer` (по матрице :25); (2) `.gitignore`: добавлены `.venv/`+`venv/` (риск `dub_sidecar/.venv`); (3) `ship.ps1`: `$releaseTailChecks` дополнен `avcodec-62.dll` (7/7 FFmpeg-маркеров; `action.yml:101` его содержит → check green); (4) `manual-smoke-matrix.md`: добавлена строка opt-in M3-оверлея (`Theme.ShowM3Theme`, T-05). Счётчик тестов «1132» (было в §8-снапшоте) уже неактуален — глобальный давно 1193; `PR #112` в хронике отражён (`backlog.md:641`). Гейт `verify-fast.ps1` green.
  - Проблема: `audit-frozen.ps1:32` не маршрутизирует правки `dubbing-contract.md`/`docs/agent/dubbing/**` на доменных
    ревьюеров из `subagent-review-matrix.md` (падают в общий бакет); `manual-smoke-matrix.md:82` не покрывает opt-in
    M3-оверлей (`Theme.ShowM3Theme`, T-05 v0.3.29), в отличие от прочих opt-in UI-фич; `backlog.md:621` счётчик тестов
    «1132» устарел (PR #112 → 1133), monitor-PR #112 не занесён в хронику; `ship.ps1:126` releaseTailChecks проверяет
    6/7 FFmpeg-маркеров — пропущен ключевой `avcodec-62.dll`; `.gitignore:370` не покрывает `.venv/` (риск закоммитить
    многогигабайтный `dub_sidecar/.venv` при `git add -A`).
  - Решение: точечные правки гейтов/доков (не трогая frozen-контракты без запроса владельца) одним PR.
  - Зачем: гейты «зелёные» при реальном дрейфе → ложная уверенность.
  - 📌 **Независимое подтверждение (сторонний ревьювер, верифицировано сессия #21, 2026-07-03):** три из пяти подпунктов
    переоткрыты внешним ревью и подтверждены по коду main v0.3.40 — (а) `audit-frozen.ps1` dubbing-routing (пути
    `dubbing-contract.md`/`docs/agent/dubbing/**` попадают не в «общий бакет», а в infra-бакет `audit-frozen.ps1:90`;
    из требуемых матрицей `media_runtime_mapper`/`native_dependency_auditor`/`packaging_release_reviewer`/`verification_reviewer`
    скрипт назначает лишь последний — рецепт фикса: правило `^docs/agent/(dubbing-contract\.md|dubbing/)` по образцу `dub_sidecar/`
    строки 137-146); (б) `.venv/` не в `.gitignore` (подтверждено `git check-ignore` → не игнорируется; побочно захламляет
    вывод `audit-frozen.ps1:14`); (в) `ship.ps1:126` releaseTailChecks без `avcodec-62.dll` (это grep-стража по тексту
    `action.yml`, физический недокомплект DLL ловит publish-smoke `ship.ps1:89-101` — реальный пробел лишь в анти-дрейфе CI).
    ⚠️ severity сторонним ревьювером у пункта (а) заявлена «Important» — при исполнении учесть, что `audit-frozen.ps1` —
    advisory read-only хелпер (`verification.md:19-23`), а не обязательный гейт; нормативный источник назначения ревьюеров —
    сама `subagent-review-matrix.md`, и она dubbing-пути покрывает корректно (реальная важность — medium). Приоритет HC-33
    стоит поднять: два независимых процесса сошлись на этих дефектах.
- **HC-34 — Bundle «пробелы тестов» 🟢 ⓢ (в рамках T-03)** · ✅ **DONE (T-03 срез №6, 2026-07-02, сессия #20)** — `TranslateServiceHelperTests` (8: 4 throw-ветки `TryGetLanguage` + 4 return, вкл. 2 китайских региона, min-stub `ITranslateService`); `CancellationToken.None` → `TestContext.Current.CancellationToken` во всех await-вызовах `BatchSubtitleTranslatorTests` (xUnit1051).
  - Проблема: await-тесты `BatchSubtitleTranslatorTests.cs:271` без `TestContext`-токена/таймаута (xUnit1051-паттерн:
    зависший тест стопорит весь прогон); `TranslateServiceHelper.TryGetLanguage` (`ITranslateService.cs:190`) — чистая
    бизнес-логика (throw на Unknown, запрет same-language кроме китайских регионов, маппинг) без единого теста.
  - Решение: прокинуть `TestContext.Current.CancellationToken`/`WaitAsync(10s)`; добавить `TranslateServiceHelperTests`.
  - Зачем: защита от deadlock-регрессий и рефакторинга языковой логики.
- **HC-35 — Buffer null-терминатор в `WindowsClipboard.SetText` 🟢 ⓢ · `LLPlayer/Extensions/WindowsClipboard.cs:34`** (security/корректность) · ✅ **DONE (v0.3.39, 2026-07-02, сессия #18)**
  - Проблема: `AllocHGlobal((len+1)*2)` не зануляет память, а `Marshal.Copy(..., len)` копирует только `len` символов —
    null-терминатор `CF_UNICODETEXT` не пишется → возможен мусорный хвост во вставленном тексте.
  - Решение: копировать `len+1` символов из массива с явным `\0` в конце (или занулять последние 2 байта).
  - Зачем: копирование субтитра/слова может дать мусор в буфере обмена.
  - ✅ **Сделано:** новый чистый `Utils.ToNullTerminatedUtf16(text)` (буфер `len+1` с явным `'\0'`) + `Marshal.Copy(chars,
    0, target, chars.Length)` в `SetText`. Тесты `FlyleafLibTests/Utils/NullTerminatedUtf16Tests.cs` (4). (Native
    P/Invoke-путь `SetText` сам по себе вне юнит-охвата — LLPlayer без тест-проекта — покрыт корректностью хелпера + ручным smoke.)

**🟢 Находки стороннего ревьювера (сессия #21, 2026-07-03) — верифицированы адверсариально по коду main v0.3.40:**

> Внешний ревьювер прислал 7 находок; workflow `wf_3ae2db0b-ab1` перепроверил каждую по коду. Итог: 3 новые (ниже,
> HC-45/46/47), 3 — дубли открытого **HC-33** (см. отметку там), 1 — дубль **HC-40** (owner-decision, нового нет).
> Опровергнутых нет. Нумерация продолжает таксономию §8 (единый счётчик открытых HC).

- **HC-45 — `DubbingConfig.OutputFormat` не валидируется: рукописный `part`/`tmp` в Config.json → вечный ре-рендер дубляжа + невидимый для auto-loader выход 🟢 ⓢ · `FlyleafLib/Engine/DubbingConfig.cs:59`** (источник: ревьювер R-04, вердикт **CONFIRMED**) · ✅ **DONE (v0.3.49, 2026-07-03, сессия #24)** — `NormalizeOutputFormat` в сеттере (whitelist FLAC-only; `part`/`tmp`/`mp3`/blank/null → `flac`), так рукописный не-FLAC не доходит до имени файла и не загоняет в вечный ре-рендер (`ResolveExistingRussianDubPath` скипает `.part`/`.tmp`). Default `flac` byte-identical (field-init минует сеттер). +9 тестов (`DubbingConfigTests.OutputFormat_*` RED-without-fix на `part`); рефлексион-гейт снапшота `ShouldCopyEveryWritableDubbingConfigSetting` исключает `OutputFormat` (нормализация делает value-distinctness-пробу невозможной — иначе гейт вакуозен). Adversarial-ревью (4 линзы): 3 SHIP + 1 minor (вакуозность гейта — исправлено) + 1 nit.
  - Проблема: сеттер `OutputFormat` (`DubbingConfig.cs:59`) — единственное поле класса без нормализации (`DefaultVoiceId:36` /
    `DuckingPercent:47` / `CustomVoiceIds:43` защищены от hand-edit). Сырое значение из Config.json течёт в имя файла
    (`BatchSubtitlesDialogVM.cs:761` → `BatchSubtitleProcessor.cs:284` → `movie.ru.dub.part`); сайдкар (`dub_sidecar/server.py:266-268`)
    молча пишет туда FLAC-битстрим, а резолвер после HC-04 отфильтровывает `.part`/`.tmp` (`DubbingOutputPathBuilder.cs:59-62`)
    → `DubExistsAnyFormat=false` навсегда → батч ре-рендерит дубль при каждом прогоне (гейты `BatchSubtitleProcessor.cs:260/:285`,
    включая `OverwriteExisting=false`), auto-loader (`DubbedAudioAutoLoader.cs:69`) готовый файл никогда не подцепляет. UI
    (`SettingsSubtitlesDubbing.xaml:139`) предлагает только `flac` — сценарий требует ручной правки конфига (низкая вероятность).
  - Решение: нормализовать в сеттере по образцу `DefaultVoiceId` — trim/`TrimStart('.')`/lower + whitelist реально кодируемых
    контейнеров (`flac`, опц. `wav`), всё прочее → `DubbingOutputPathBuilder.DefaultExtension`; юнит-тесты
    `part`/`tmp`/`.FLAC `/`mp3` → `flac`.
  - Зачем: рукописный конфиг не должен уметь загонять систему в тихий вечный ре-рендер (жжёт GPU-часы) с выходом, который никто
    не подцепит; фикс однострочный, в уже принятом в этом классе стиле «нормализация на set».
- **HC-46 — Ошибки пост-Download в `SubtitlesDownloaderDialogVM` уходят в глобальный обработчик вместо контекстного попапа 🟢 ⓢ · `LLPlayer/ViewModels/SubtitlesDownloaderDialogVM.cs:113-134,211`** (UX/диагностика; источник: ревьювер R-02, вердикт **PARTIAL** — severity понижена Medium→low, 2 скептика подтвердили) · ✅ **DONE (v0.3.49, 2026-07-03, сессия #24)** — пост-Download блок `CmdLoad` (safe-path/расширение/temp-запись) и запись `CmdDownload` обёрнуты в try/catch → `ErrorDialogHelper.ShowUnknownErrorPopup(…, UnknownErrorType.Subtitles, ex)` вместо генерик «Unhandled Exception» + записи в crash.log. `subPath` объявлен до try (definite-assigned, catch делает return); decoder-хендлер подписывается только на успехе. LLPlayer-VM → manual-smoke (юнит-теста нет — чистой логики не добавлено); success-путь byte-identical. Adversarial-ревью: correctness-линза SHIP (0 находок).
  - Проблема: локальный try/catch в `CmdLoad` (`:100-108`) покрывает только `_subProvider.Download` (`:102`). Вне catch остаются:
    safe-path валидация с `throw` (`:116`, осознанное решение HC-05), `Directory.CreateDirectory` (`:120`), проверка расширения
    с `throw` (`:131`, upstream-код — был вне catch ещё до HC-05), запись temp-файла (`:134`); в `CmdDownload` — запись выбранного
    файла (`:211`). Prism 9 `AsyncDelegateCommand` без `.Catch(...)` перебрасывает исключение из `async void` в
    `App_OnDispatcherUnhandledException` (`LLPlayer/App.xaml.cs:163`): пользователь видит генерик «Unhandled Exception: …» вместо
    контекстного «Cannot load the subtitle from opensubtitles.org: …», а некраш пишется в `crash.log` (`:180`) как краш. НЕ краш
    и НЕ silent-fail — приложение/диалог продолжают работать. Реалистичный триггер — `IOException` при записи (диск полон, ACL,
    антивирус), не только hostile API-ответ.
  - Решение: расширить локальный try/catch на весь пост-Download блок (валидация + `CreateDirectory` + запись temp) с контекстным
    `ErrorDialogHelper.ShowUnknownErrorPopup(...)`; аналогично обернуть запись в `CmdDownload:211`. Умышленные
    `InvalidOperationException` (`:116/:131`) поймает тот же локальный catch.
  - Зачем: контекстное сообщение об ошибке вместо генерик «Unhandled Exception» и чистый `crash.log` — не засорять краш-диагностику
    обычными файловыми ошибками. (Тестируемого чистого куска нет — правка в LLPlayer-VM, проверяется ручным smoke.)
- **HC-47 — Док-дрейф: `*.ru.voices.json` отсутствует в 3 инструкционных перечнях dub-артефактов 🟢 ⓢ · docs/skills-only** (источник: ревьювер R-05, вердикт **PARTIAL** — «roadmap» уже актуален, реально отстают 3 поверхности) · ✅ **DONE (v0.3.49, 2026-07-03, сессия #24)** — `*.ru.voices.json` добавлен в 3 перечня «Do Not Commit»/reject: `llplayer-runtime-assets/SKILL.md:19`, `llplayer-packaging-release/SKILL.md:24-25`, `dubbing-spec.md §8` (помечен Phase 2a). Прод-код/скрипты/CI/.gitignore не тронуты (уже актуальны); согласовано с frozen `dubbing-contract.md:142-147`. verify.ps1 green. Adversarial-ревью: frozen-docs-линза SHIP (0 находок).
  - Проблема: после F-16 фазы 2a (v0.3.37, PR #106/#110) companion-файл per-line голосов `*.ru.voices.json` описан в контрактах
    (`dubbing-contract.md:143`, `dubbing-roadmap.md:68`, `dependency-baseline.md:77+97`, `DO_NOT_PUSH.md:7`, `.gitignore:373-374`)
    и enforce'ится в `ship.ps1:59-61` / `build-package/action.yml:133-136`, но три поверхности с перечнями dub-артефактов отстают:
    `Plugins/llplayer-codex/skills/llplayer-runtime-assets/SKILL.md:19` (список «Do Not Commit» перечисляет `*.ru.dub.*`, но не
    `*.ru.voices.json`), `Plugins/llplayer-codex/skills/llplayer-packaging-release/SKILL.md:24-25` (список reject'ов пакета уже
    фактически проверяемого в `action.yml`/`ship.ps1`), `docs/agent/dubbing/dubbing-spec.md:220` («Never committed» без voices.json;
    спека Phase-0). Опционально: `dub_sidecar/README.md:15`. Оба SKILL.md — drift-surfaces (`llplayer-instruction-drift/SKILL.md:15,22`).
  - Решение: docs-only — добавить `*.ru.voices.json` в три перечня (в `dubbing-spec.md` — с пометкой «Phase 2a»); прод-код, скрипты
    и CI не трогать (уже актуальны).
  - Зачем: оба SKILL.md — рабочие инструкции Codex-агентов; неполный «Do Not Commit»/reject-список провоцирует пропуск voices.json
    при проверках пакета и порождает ложные находки drift-ревью.

### 8b. Ⓜ Средние (тир 2) — требуют рефакторинга/нового API/тестового каркаса

> 🔁 **Перепроверка кандидата №1 (сессия #21, 2026-07-03, workflow `wf_3ae2db0b-ab1`):** HC-36, HC-37, HC-38 — все **STILL_VALID**
> на main v0.3.40 (находки датированы v0.3.38; коммиты PR #114 `ff3a743`/`a4aa871`/`4d7c9e5` эти места не трогали, строки совпадают).
> HC-36: единственный смягчённый нюанс — «двойной Dispose» сам безвреден (idempotent-guard `_disposed` в обоих OCR-сервисах), но
> конкурентный use-after-dispose при dual-OCR стоит. HC-37: все 6 под-претензий актуальны (одна смежная гонка «Cancel на disposed CTS»
> уже закрыта в `SubtitlesTranslator`, но заявленные — в силе). HC-38: все актуальны + образец atomic-write уже есть в репо
> (`DubbingVoiceAssignmentStore.SaveAtomic`, `SrtSubtitleWriter`), общего хелпера в `FlyleafLib/Utils` нет. Рекомендованный путь тестов
> по конвенции проекта — выносить lifecycle/CTS-логику в `FlyleafLib/Utils` (`DisposableSlots<T>` для HC-36, `CtsGuard`/atomic-exchange
> для HC-37/38, `AtomicFile.WriteAllText` для HC-38) и покрывать фейками; сами движки/VM — ручным smoke.
> ⚠️ **Кандидат №1 в летописи ошибочно записан как «HC-36/14/15»** — HC-14 (`dub_sidecar/server.py` `assemble_real` игнорирует
> `total_ms`) и HC-15 (`SrtSubtitleWriter` без `NormalizeCueText`) — это ⓢ-находки тира 8a, НЕ про OCR. Верный бандл кандидата №1 =
> **HC-36 + HC-37 + HC-38** (исправлено в летописи сессией #21).

- **HC-36 — OCR: один общий `_ocrService` на оба сабтрека 🟠 Ⓜ · `FlyleafLib/MediaPlayer/SubtitlesOCR.cs:65/91`** · ✅ **DONE (v0.3.52, PR #127 `c0cd0c8`, сессия #26)**
  - Проблема: класс параметризован `subIndex` и создан для 2 треков с раздельными `_lockers/_ctss`, но движок хранится
    в единственном поле: повторный `TryInitialize` перезаписывает без Dispose (утечка нативного Tesseract-движка +
    модели); `Do` диспозит через `using`, но поле не обнуляет (use-after-dispose); dual-OCR primary+secondary → оба
    `Do` захватывают ОДИН движок → неверный язык/двойной Dispose.
  - Решение: `IOCRService?[] _ocrServices` per-subIndex; в `TryInitialize` менять только слот с Dispose старого; в `Do`
    забирать атомарно (`Interlocked.Exchange`).
  - Зачем: утечка native-движка и порча результатов при двух OCR-дорожках.
  - ✅ **Сделано (v0.3.52, сессия #26):** `IOCRService?[]` per-subIndex через `OcrEngineSlots` (atomic `Interlocked.Exchange`,
    Dispose старого слота при reinit) + `SubtitlesOCR : IDisposable`; закрыл утечку native-Tesseract + use-after-dispose при
    dual-OCR. Concurrency-ревью в стеке. +тесты.
- **HC-37 — Гонки/TOCTOU на локах `_cts`/`SubIndexSet`/`_lockerSubs` в ASR/OCR/Translate (бандл) 🟡 Ⓜ** · ✅ **DONE (v0.3.51, PR #128 `ea65a67`, сессия #26)**
  - Проблема: несколько связанных дефектов синхронизации: `SubtitlesASR.Execute:215` бэкапит `Subs` под чужим
    `_lockerSubs` вместо `SnapshotSubs` (гонка с `Refresh→Clear`); `SubIndexSet` (`SubtitlesASR.cs`) мутируется под ДВУМЯ
    локами и энумерируется без лока (`:328` — лок внутри цикла) → `InvalidOperationException` при dual-ASR + Reset;
    `SubtitlesTranslator.cs:63` `_translationStartCancellation` мутируется из UI+screamer без лока (double-dispose);
    `SubtitlesTranslator.cs:262` CTS создаётся ВНУТРИ задачи после снапшота → `Cancel()` не отменяет только-что
    запланированный проход (полный проход с LLM-таймаутом); `SubtitlesTranslator.cs:280` читает `Subs[i]` без лока;
    TOCTOU на `_cts` в `SubtitlesManager.TryCancelWait:603` и `_ctss[subIndex]` в `SubtitlesOCR.TryCancelWait:153`
    (NRE/ObjectDisposedException на воркере загрузки).
  - Решение: единый лок на `SubIndexSet` + снапшот под локом для энумерации; `SnapshotSubs()` для бэкапа/чтения окна;
    CTS создавать в момент планирования задачи (в том же lock) и передавать токеном; `Interlocked.Exchange` +
    локальная копия в `TryCancelWait` (оба класса).
  - Зачем: набор гонок, каждая роняет ASR/OCR/перевод с error-диалогом при быстром переключении дорожек/seek —
    прямое продолжение уже закрытого класса «Subs без `_subsLocker`».
  - ✅ **Сделано (v0.3.51, сессия #26):** `CtsGuard` для обоих `TryCancelWait` (TOCTOU), `SubIndexSet` → private +
    `SnapshotSubIndexes()` под локом для энумерации, `SnapshotSubs()` для бэкапа/чтения окна, CTS создаётся в момент
    планирования задачи. Concurrency-ревью поймало смежный `Player.Screamers.cs:185`. +тесты.
- **HC-38 — Неатомарная запись всех трёх конфигов 🟡 Ⓜ · `LLPlayer/Services/AppConfig.cs:78` (+`Config.cs:140/1753`)** · ✅ **DONE (v0.3.50, PR #125 `95fa6dd`, сессия #26)**
  - Проблема: `Save` пишет прямым `File.WriteAllText`; записи идут и без действий пользователя (PersistBatchDefaults на
    каждый тумблер, AsrOnboardingShown, version-stamp). Power-loss/креш посреди записи → усечённый JSON → следующий
    старт: `JsonException` → MessageBox + `Environment.Exit(1)` (кирпич).
  - Решение: единый atomic-хелпер (temp рядом + `File.Replace/Move(overwrite)`, как companion-json); при `JsonException`
    на загрузке — переименовать битый в `.bak` и стартовать с дефолтами вместо Exit(1).
  - Зачем: обрыв записи не должен блокировать запуск приложения.
  - ✅ **Сделано (v0.3.50, сессия #26):** единый `AtomicFile.WriteAllText` (temp рядом + `File.Replace`/`Move(overwrite)`)
    для всех 3 конфигов. **Atomic-only** — graceful-degrade битого JSON на загрузке (`.bak` вместо `Exit(1)`) сознательно
    отложен как отдельный шаг. +тесты.
- **HC-39 — Reflection completeness-guard покрывает не все nested-конфиги снапшота 🟡 Ⓜ · `FlyleafLibTests/MediaPlayer/Batch/BatchSubtitleTranslatorTests.cs:148`** · ✅ **DONE (T-03 срез №6, 2026-07-02, сессия #20)** — обобщённый helper `AssertSnapshotCopiesEveryWritableProperty<T>` + 5 guard-тестов (`WhisperConfig`/`WhisperCppConfig`/`FasterWhisperConfig`/`TranslateChatConfig`/`WhisperCppModel`) с исключениями трансформируемых полей (`Translate` force-false, `ExtraArguments` strip). Все зелёные (поля уже копируются) — fail-closed на будущие «забытые поля».
  - Проблема: полноценный guard есть только для `DubbingConfig`; `WhisperCppConfig` (15 полей), `FasterWhisperConfig` (9),
    `TranslateChatConfig` (11), `WhisperConfig` (3) — лишь точечные тесты. Новое свойство с UI-биндингом, забытое в
    `BatchSubtitleConfigSnapshot`, батч молча проигнорирует (тот же класс, что F-05-gap).
  - Решение: обобщить `DubbingConfig`-guard в параметризованный helper и добавить guard-тест на каждый nested-конфиг.
  - Зачем: fail-closed на будущие «забытые поля» батч-снапшота.
- **HC-40 — `Config.Clone()` теряет `Data`/`Plugins`/`Version`; `KeysConfig.Clone` → `Keys=null`; `SubtitlesConfig.Clone` делит вложенные объекты 🟢 Ⓜ · `FlyleafLib/Engine/Config.cs:54`** · ✅ **DONE (v0.3.41, 2026-07-03, сессия #21, owner-decision вариант A)** — deprecate + точечный фикс: (1) `SubtitlesConfig.Clone` теперь deep-copy массива `SubConfigs` и его элементов (добавлен `SubConfig.Clone` = `MemberwiseClone`+`player=null`) — реальная утечка закрыта; (2) `Config.Clone` переносит `Version` (`config.Version = Version;`) + XML-doc помечает Clone неполным (`Data`/`Plugins` не копируются → для батча использовать `BatchSubtitleConfigSnapshot`); (3) `ConfigCloneTests`: 2 характеризационных теста перевёрнуты в ассерты корректного deep-copy (RED-without-fix) + element-distinctness тест (+1 → 1193). **Вариант B (deep-copy `Data`/`Plugins`) НЕ делали** — `Config.Clone` без вызывающих (grep пуст); `SubtitlesConfig.Clone` вызывается только из него → фикс без риска для живых путей. `KeysConfig.Clone`→`Keys=null` — by-design, оставлено.
  - Прежняя характеризация (сессия #20): `ConfigCloneTests` (4, Engine-free) зафиксировали баг — `SubtitlesConfig.Clone` шарил `SubConfigs`; теперь исправлено.
  - Проблема: публичный снапшот-API библиотеки неполон: `Config.Clone` копирует не всё (опции плагинов YoutubeDL
    теряются, `Version=null` → повтор всех миграций у клона), `PlayerConfig.Clone→KeysConfig.Clone` ставит `Keys=null`
    (NRE у потребителя), `SubtitlesConfig.Clone` через `MemberwiseClone` делит массив `SubConfigs` и вложенные объекты
    с оригиналом.
  - Решение: либо довести `Config.Clone` до полноты (deep-copy Data/Plugins/nested, перенос Version), либо пометить
    как неподдерживаемый и направить потребителей на `BatchSubtitleConfigSnapshot`; закрепить reflection-guard.
  - Зачем: скрытая мина для любого будущего потребителя `Config.Clone`.
- **HC-41 — Три download-диалога (~90% copy-paste, уже с дрейфом) 🟢 Ⓜ · `LLPlayer/ViewModels/TesseractDownloadDialogVM.cs` (+Whisper model/engine)** · ✅ **DONE (v0.3.53, 2026-07-04, сессия #28)**
  - Проблема: `WhisperModelDownloadDialogVM`/`TesseractDownloadDialogVM`/`WhisperEngineDownloadDialogVM` — один VM
    скопирован трижды (одинаковый `DownloadModelWithProgressAsync`); копии разъехались: Whisper диспозит `_cts`,
    Tesseract/Engine — только `=null` (утечка CTS); `CmdOpenFolder` с try/catch только у Whisper.
  - Решение: `ModelDownloadServiceBase` (download+progress+temp-move+единый finally+единый OpenFolder+класс. OCE-таймаута);
    три VM оставляют только источник моделей и UI.
  - Зачем: дрейф между копиями = баги чинятся в одной, живут в двух.
  - ✅ **Сделано (v0.3.53, сессия #28):** извлечён общий тестируемый `FlyleafLib/Utils/StreamDownloadPump.cs` (copy-loop,
    +5 тестов) + база `LLPlayer/ViewModels/ModelDownloadDialogVMBase.cs` — единый `finally` с `_cts.Dispose()` (закрыл утечку
    CTS у Tesseract/Engine), guarded `OpenFolderSafe` (закрыл незащищённый `Process.Start` у Tesseract/Engine), унифицир.
    cancel-vs-timeout через `token.IsCancellationRequested` (HTTP/provider-таймаут → «Failed to download», не мнимое
    «Download canceled» — заодно закрыл латентный дрейф Engine). 3 VM оставили источник+finalize+UI. Гейты build
    `-warnaserror` **0/0 ×2** + тесты **1283/1283** + verify.ps1; 5-линзовое adversarial-ревью — **0 находок**. LLPlayer без
    тест-проекта → чистая логика (pump) юнит-покрыта, VM-правки = manual-smoke владельца (download/cancel/open-folder/delete).
- **HC-42 — Батч-переводчик вручную зеркалит интерактивный (паритет только на комментариях) 🟢 Ⓜ · `FlyleafLib/MediaPlayer/Batch/BatchSubtitleTranslator.cs:146`** · ✅ **DONE (v0.3.53, 2026-07-04, сессия #28)**
  - Проблема: продублированы 3 куска логики `SubtitlesTranslator`: построение ContextWindow (`:146` vs `:416`),
    empty-reply guard (`:117` vs `:372`), WrapLines-гейтинг по `ResegmentSubtitles` (`:53/:122` vs `:380`). Синхронность
    держится только комментариями «Parity with interactive» — при правке одного пути второй молча разойдётся.
  - Решение: вынести чистые куски в `TranslationCueRules` (ShouldAcceptReply/PostProcess/BuildWindow) + паритетный тест.
  - Зачем: контекст/качество перевода в батче должны совпадать с интерактивом гарантированно, не «на честном слове».
  - ✅ **Сделано (v0.3.53, сессия #28):** 3 куска вынесены в чистый `FlyleafLib/MediaPlayer/Translation/TranslationCueRules.cs`
    (`ShouldAcceptReply`/`PostProcess`/`ClampWindow`/`BuildContext`) и зовутся ОБОИМИ путями → паритет теперь структурный,
    не на комментариях. Чистый рефактор, поведение **byte-identical** (проверено: `SubManager.GetContextWindow` тоже
    пропускает whitespace-соседей = эквивалентно батч-`Collect`; клампы/flatten/ordering совпадают). +14 тестов
    (`TranslationCueRulesTests`). Гейты **0/0 ×2** + **1283/1283** + verify.ps1; adversarial-ревью — **0 находок**.
    ⚠️ **уточнение путей (верифиц. сессией #27):** интерактив = `FlyleafLib/MediaPlayer/Translation/SubtitlesTranslator.cs`
    (класс `SubTranslator`), строки `:418/:371/:382` (сдвиг +2 после мёржа HC-37 vs исходные `:416/:372/:380`).
- **HC-43 — Отмена рендера дубляжа гоняется с in-flight `assemble` сайдкара 🟢 Ⓜ · `FlyleafLib/MediaPlayer/Dubbing/DubbingRenderer.cs:103`** · ✅ **DONE (v0.3.55, 2026-07-04, сессия #31, owner-decision: C#-митигейшн)**
  - Проблема: отмена HTTP-POST `/assemble` не останавливает python-поток — он дописывает `os.replace(output)` позже
    (~5с окно). C# в catch делает `TryDeleteOutput` (файла ещё нет) → «нежеланный» дубляж материализуется ПОСЛЕ →
    следующий запуск пропускает рендер (`DubExistsAnyFormat`), auto-loader цепляет его.
  - Решение: удалять output не сразу, а после гарантированной остановки сборки (cancel-endpoint/поколение запроса в
    сайдкаре, либо повторная зачистка в `DisposeAsync` и в начале следующего рендера того же файла).
  - Зачем: отменённый дубляж «оживает» и ломает последующие прогоны.
  - ✅ **Сделано (v0.3.55, сессия #31, C#-only — HTTP-контракт сайдкара НЕ тронут):** новый чистый
    `FlyleafLib/MediaPlayer/Dubbing/DubOrphanCleanup.cs` (`MarkCanceled`/`ClearAndClean`/`CleanAll`) в `DubbingRenderer`:
    на cancel — записать target (после немедленного `TryDeleteOutput`); повторная зачистка в начале след. рендера того же
    файла (forget-before-success, чтобы не снести свежий хороший дубляж) + в `DisposeAsync` ПОСЛЕ `host.DisposeAsync()`
    (сайдкар-процесс реапнут → осиротевший файл гарантированно долетел). Сужает окно без cancel-endpoint (полный фикс с
    HTTP-контрактом отложен). +7 тестов (`DubOrphanCleanupTests`, RED-without-fix). Wiring рендерера = manual-smoke владельца.

### 8c. Ⓛ Крупные (тир 3) — архитектурный рефакторинг

- **HC-44 — Тройная копия offline-читателя (`WaveformReader`/`AudioReader`/`SubtitleReader`) 🟡 Ⓛ · `FlyleafLib/MediaPlayer/S16MonoResampler.cs` + `OfflineDemuxer.cs`** · ✅ **DONE (срез 1 v0.3.57 + срез 2 v0.3.58, 2026-07-05, сессия #33; owner sign-off на срез 2)**
  - Проблема: три класса «изолированный второй `avformat_open_input`» дублируют почти дословно: `Open()`/`Dispose()`
    (Demuxer + `token.Register(ForceInterrupt)` + Log.Prefix + обработка ошибок, ~35 стр. ×3 + 4-я частичная копия
    `MediaAudioProbe.cs`) и swr-блок ресемплинга в S16 mono 16k (reinit-guard + `swr_alloc_set_opts2` + расчёт
    nOut+delay + `swr_convert`) скопирован в **2 копиях**: `SubtitlesASR.ResampleTo` + `WaveformReader.ResampleFrame`
    (третий `swr_convert` в `AudioDecoder.cs` — stereo/playback-rate, НЕ S16-mono-16k, вне скоупа).
  - Решение: `OfflineMediaReaderBase`/`OpenIsolated(...)` для Open/Dispose + переиспользуемый `S16MonoResampler`
    (с опциональным denoise-хуком для F-02), которым пользуются `AudioReader` и `WaveformReader`.
  - Зачем: native/FFmpeg-код в копиях — правка (напр. фикс ресемпла или denoise) должна делаться единожды;
    высокий риск рассинхрона в самом хрупком (interop) слое. Пересекается с F-02-full.
  - ✅ **Сделано (срез 1, v0.3.57, сессия #33):** новый чистый `FlyleafLib/MediaPlayer/S16MonoResampler.cs`
    (`IDisposable`, владеет `SwrContext` + переиспользуемым выходным буфером; `Resample(frame, targetSampleRate,
    targetChannel)` возвращает размер PCM, оставляет данные в `Buffer`). Оба swr-сайта (`AudioReader.ResampleTo` +
    `WaveformReader.ResampleFrame`) делегируют в него — **поведение byte-identical** (порядок swr-вызовов сохранён;
    единственная замена — seed кодек-гарда `_lastFormat` 0→-1, доказуемо нейтрально: первый кадр всегда аллоцирует
    контекст, т.к. `_swrContext==null`). ASR-специфику (F-02 high-pass/afftdn, WAV-write, T-09 silence-read по
    `_resampler.Buffer`) оставили в `ResampleTo`. Три чистых seam'а вынесены `internal` и юнит-покрыты RED-without-fix
    (`ComputeOutputSampleCapacity` — nOut+pad+delay-clamp; `DetectCodecChange` — гард reinit; `EnsureCapacity` —
    рост-без-усадки): +17 тестов (`S16MonoResamplerTests`, **1316→1333**). Дедуп: два вызова потеряли ~194 стр.
    дублированного swr-кода → единый источник. Гейты 0/0 (LLPlayer+YoutubeDL) + verify.ps1 green. Native `swr_convert`
    (как и раньше) вне юнит-охвата → owner manual-smoke (ASR-транскрипция + рендер waveform F-12).
  - ✅ **Сделано (срез 2, v0.3.58, сессия #33, owner sign-off):** новый чистый `FlyleafLib/MediaPlayer/OfflineDemuxer.cs`
    (`internal static` `OpenIsolated`/`DisposeIsolated`) — единый источник create+`token.Register(ForceInterrupt)`+
    prefix-rename+`Open` и teardown (`ForceInterrupt=0`+`Dispose`). Три field-читателя (`AudioReader`/`WaveformReader`/
    `SubtitleReader`) делегируют — **byte-identical** (каждый сохраняет СВОЮ политику отмены на open-error: `return` у
    Audio/Waveform vs `ThrowIfCancellationRequested` у Subtitle — дрейф НЕ унифицирован, т.к. это отд. поведенческое
    решение; capture лямбды local≡field, т.к. все три single-use `using`+один `Open`/инстанс). Декодер/каст/доп.
    handles (frame/packet/resampler/ExternalStream/`_isFile`) остались в вызывающем. **`MediaAudioProbe` вне скоупа**
    (свой lifecycle: локальный demuxer + `using`-registration + OCE, без prefix). Дедуп −39 стр. из вызывающих.
    Тестируемого чистого seam'а нет (native demuxer-механика; читатели и раньше без юнит-тестов) → build 0/0 +
    adversarial-ревью + owner manual-smoke (ASR + waveform + загрузка внешних субтитров). **HC-44 закрыт полностью.**

### 8d. Опровергнутые находки (11) — НЕ баги, зафиксировано для истории
> Верификаторы отсеяли (цитаты часто верны, но сценарий нереализуем / уже известно / стилевой нит):
> dual-ASR блокировка на `_locker` (`SubtitlesASR.cs:167`, вход не существует); per-frame конкуренция за `_subsLocker`
> (`SubtitlesManager.cs:140`, WPF ведёт себя иначе); `DeleteAfter` binary-search по EndTime (`:441`, путь под гейтом
> `LanguageSource==null`, ASR не поверх загруженных); `SubtitleData.Clone()` не копирует `SubStyles` (`:1144`, факт
> верен, но сценарий сегодня нереализуем); atomic temp+move в `DubbingVoiceAssignmentStore` (стилевой нит);
> GoogleV1/DeepLX/MS повторное оборачивание `TranslationException` (Kind не теряется на этих путях); CosyVoice
> preset→voice маппинг (`server.py:125`, = уже известный открытый F-16-остаток); README «about this fork» (= закрытый
> T-06); `MaxConcurrent` в тесте (теоретический hardening); `OneShotHttpServer` RST-гонка (для .NET нереализуемо);
> `-warnaserror` без NuGetAudit-политики (намеренная замороженная политика).

### 8e. 🔎 Кандидаты раунда №2 — ✅ ВЕРИФИЦИРОВАНЫ адверсариально (сессия #29, 2026-07-04, workflow `wf_9ca415bf-d53`, 22 агента)

> **Итог верификации против live-кода `bced48f` v0.3.53 (каждая находка адверсариально перепроверена 2 линзами):**
> - **YoutubeDL lifecycle → ✅ CONFIRMED (ⓢ, sev-low):** незащищённые `Process.Start("taskkill")`
>   (`Plugins/YoutubeDL/YoutubeDL.cs:210-217`) и `Directory.Delete(workingDir,true)` (`:234`) в `DisposeInternal` →
>   **реальный краш фонового потока** на пути stop/switch-after-YouTube (через `PlayThread` finally, `IsBackground=true`).
>   Фикс: guarded-swallow + `workingDir=null` в `finally` + вынос `SafeDeleteDirectory` в `FlyleafLib/Utils` (юнит-тест).
> - **Supply-chain (zip-slip) → ✅ CONFIRMED (ⓢ, sev-low, defense-in-depth):** `ExtractArchiveAsync`
>   (`WhisperEngineDownloadDialogVM.cs:120-122`, дрейф с `:166` после HC-41) без per-entry containment;
>   `Squid-Box.SevenZipSharp.Lite 1.6.2.24` сам НЕ защищает (`..` не чистит). Достижимо только через компромат
>   upstream-релиза / TLS-MITM (URL хардкожены https на доверенные хосты). Фикс: чистый `ArchivePathGuard.IsWithin`
>   в `FlyleafLib/Utils` + per-entry `ExtractFile` (юнит-тест на zip-slip escapes). Опц. пиновка SHA-256.
> - **Секреты → ⚠️ OVERSTATED, downgrade до accepted-risk:** выжил ТОЛЬКО plaintext-ключ в локальном `Config.json`
>   (файл владельца; 5 свойств `ApiKey` DeepL/Azure/Bing/Microsoft). Severity-претензии ОПРОВЕРГНУТЫ: эхо на 401/403 =
>   **тело ответа провайдера** (`ex.Data["response"]`, `OpenAIBaseTranslateService.cs:460/471`), **НЕ ключ** (ключ только в
>   заголовках запроса `:595/:671`); лог-утечки нет; «LiteLLM-ключа» НЕ существует (нет свойства `ApiKey`); экспорта
>   конфига нет. ⚠️ **НЕ добавлять `[JsonIgnore]`** к 5 свойствам — тихая потеря сохранённых ключей при следующем `Save`.
>   DPAPI-at-rest меняет frozen `config-data-contract` + портируемость → sign-off. Опц. чистый `Redact`-хелпер безвреден.
> - **Prompt injection → ❌ REFUTED (accepted-risk, НЕ work):** для однопользовательского офлайн-плеера потолок вреда =
>   кривой перевод/саммари самому себе, загрузившему контент. Ролевые границы (system=инструкции, user=текст субтитров)
>   есть, tool/function-calling модели не выставлен, парсинг ответа толерантный (`ParseChatResponse` читает только
>   HTTP-конверт). Нет escalation/exfiltration/persistence. Пометить бокс «не верифицировано» закрытым.
> - **Не покрытые зоны → ⚠️ OVERSTATED:** ровно **1** реальный дефект — незащищённый `Process.Start` в
>   `AppActions.cs:193-198` (`CmdOpenCurrentPath`); «краш» ОПРОВЕРГНУТ (глобальный `App_OnDispatcherUnhandledException`
>   `App.xaml.cs:179-217` ставит `e.Handled=true` → generic-попап + запись в crash.log вместо контекстного сообщения,
>   класс HC-46). ~4 строки try/catch. Остальное (`WpfColorFontDialog` NRE, MeCab BPos, `ScrollParentWhenAtMax`) —
>   false-positive/мёртвый код.
>
> ✅ **Решение владельца (сессия #29, AskUserQuestion):** **сессия #30 = защитный хардненинг-бандл** =
> YoutubeDL-lifecycle + Supply-chain (zip-slip guard) + fold-in `AppActions.cs:193` guard. Все ⓢ, без sign-off, без
> frozen-контракта, каждый даёт новое покрытие в `FlyleafLibTests` (`SafeDeleteDirectory` + `ArchivePathGuard`; VM/AppActions
> = manual-smoke владельца). Секреты/prompt-injection → accepted-risk-заметки (не work). **HC-43/HC-44/T-12 остаются
> owner-decisions** (HC-43 меняет HTTP-контракт сайдкара; HC-44-full = interop-риск; T-12 = persisted-default).
>
> ✅ **ОТГРУЖЕНО (v0.3.54, сессия #30, 2026-07-04):** защитный хардненинг-бандл реализован.
> - **YoutubeDL-lifecycle:** `taskkill` `Process.Start` → try/catch + `?.WaitForExit`; `Directory.Delete` → новый чистый
>   `FlyleafLib/Utils/SafeDirectory.TryDelete` (best-effort, не бросает `IOException`/`UnauthorizedAccessException`);
>   `workingDir=null` безусловно (не ретраится). Больше не роняет фоновый `PlayThread`.
> - **Supply-chain (zip-slip):** новый чистый `FlyleafLib/Utils/ArchivePathGuard` (`IsWithinDirectory`/`ValidateEntries`);
>   `WhisperEngineDownloadDialogVM.UnzipEngine` валидирует все `ArchiveFileData`-entry ДО `ExtractArchiveAsync` и отклоняет
>   весь архив при выходе за `EnginesDirectory`. Опц. пиновка SHA-256 НЕ делалась (defense-in-depth достаточно).
> - **AppActions.CmdOpenCurrentPath:** `Process.Start` → try/catch → `ErrorDialogHelper.ShowKnownErrorPopup` (как line 163).
> - **Гейты:** build `-warnaserror` **0/0 ×2** + xUnit **1283→1304 (+21)** (`SafeDirectoryTests`+`ArchivePathGuardTests`,
>   RED-without-fix) + `verify.ps1` + `ship.ps1` publish-smoke + launch **0.3.54** чист + **5-линз adversarial-ревью 0 находок**.
>   VM/AppActions/YoutubeDL = manual-smoke владельца (нет тест-проекта LLPlayer/Plugins). Секреты/prompt-injection остаются
>   accepted-risk (не work). PR за владельцем.
>
> **Исходные кандидаты (для истории, до верификации):**
> - **Supply-chain:** сетевые загрузки (Whisper-движок 7z, модели, tesseract traineddata) без проверки хэшей/подписей
>   + возможный zip-slip в `SevenZipExtractor.ExtractArchiveAsync` (`WhisperEngineDownloadDialogVM.cs:166`).
> - **Секреты:** API-ключи (DeepL/Azure/OpenAI-like/LiteLLM) плоским текстом в `Config.json`; проверить утечку в
>   логи/error-диалоги (`ex.Message` с URL) и в экспорт конфига.
> - **Prompt injection:** недоверенный текст субтитров (скачанные/ASR) без экранирования уходит в LLM-промпты
>   (AI Insights, LiteLLM/OpenAILike-перевод) — проверить устойчивость парсинга ответов.
> - **Не покрытые зоны:** `WpfColorFontDialog/` (12 файлов), `LLPlayer/Services/AppActions.cs` (1075 стр., рестарт-путь
>   `Process.Start`), `LLPlayer/Controls/SelectableSubtitleText.xaml.cs` (662 стр., word-интеракции),
>   `LLPlayer/Extensions/*` (кроме HC-35).
> - **YoutubeDL lifecycle:** `Directory.Delete(workingDir, true)` без try/catch в Dispose + `taskkill`-процесс без
>   Dispose (`YoutubeDL.cs:234/217`); толерантность `YoutubeDLJson.cs` к битому JSON.
