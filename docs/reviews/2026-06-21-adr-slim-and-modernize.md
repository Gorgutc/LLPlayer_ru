# ADR: Облегчение LLPlayer до ядра «речь→текст» + модернизация UI

**Status:** Proposed  **Date:** 2026-06-21

---

## Контекст

LLPlayer (`net10.0-windows`, WPF + Prism/DryIoc, MaterialDesignThemes 5.3.1) — это медиаплеер для изучения языков на базе форка FlyleafLib. Поверх воспроизведения он несёт большой набор языковых функций: распознавание речи (ASR/Whisper), пакетную генерацию субтитров, перевод (DeepL/Google/Bing/Azure/OpenAI/Ollama/Claude и др.), OCR субтитров-картинок (Tesseract + Windows.Media.Ocr), загрузку субтитров (OpenSubtitles) и онлайн-видео (yt-dlp), словарную панель/попап с разбором слов (LibNMeCab, SearchPioneer.Lingua, PDIC), запись/снимки/ремукс и экспорт SRT.

Владелец хочет: **(A)** сузить продукт до ядра «открыть медиа → декодировать аудио (FFmpeg) → Whisper ASR → хранилище субтитров → показать/экспортировать текст» **плюс** пакетный поток субтитров (Batch — это и есть пакетный speech-to-text), убрав всё избыточное **безопасно**; **(B)** модернизировать внешний вид, отзывчивость и взаимодействие.

Ключевое ограничение — **frozen-контракты** в `docs/agent/`: `product-behavior-contract.md`, `wpf-design-contract.md`, `media-runtime-contract.md`, `config-data-contract.md` и `dependency-baseline.md` (версии пакетов заморожены, удаление Whisper/Tesseract/Vortice/FFmpeg «как попутная уборка» прямо запрещено — `dependency-baseline.md:75`). Любое удаление пользовательской функции трогает эти контракты и требует явного согласия.

Верификация трассировкой кода дала три структурных вывода, которые ломают наивный план «удалить всё, что не ASR»:

1. **Перевод не опционален для Batch.** `BatchSubtitleProcessor` (конструктор требует `IBatchSubtitleTranslator`, стадия `_translator.TranslateAsync` на линии 126) и `BatchSubtitleConfigSnapshot` имеют compile-time ссылки на `FlyleafLib.MediaPlayer.Translation.*`. Кроме того, `SubManager` (ядро-приёмник ASR) безусловно создаёт `new SubTranslator(...)` (`SubtitlesManager.cs:134`), а `SubtitleData.DisplayText/UseTranslated` — это то, что рендерит `SubtitlesControl`. **Удаление Translation ломает сборку Batch и путь отображения ASR ⇒ must-keep.**
2. **«OpenSubtitles» — это два разных субъекта.** Загрузчик-диалог (`Services/OpenSubtitlesProvider.cs` + `SubtitlesDownloaderDialog`) — удаляем; а плагин `FlyleafLib/Plugins/OpenSubtitles.cs` (единственный реализатор `IOpenSubtitles` + `ISearchLocalSubtitles`) — **обязателен**: через него идёт открытие любых внешних/сайдкар-субтитров и их авто-поиск, в т.ч. в Batch. Удаление плагина = регрессия открытия субтитров.
3. **Самый большой безопасный выигрыш не требует удаления фич:** GPU-рантаймы Whisper (Cuda ~72 МБ + Vulkan ~94 МБ = ~166 МБ) не используются по умолчанию (`RuntimeLibraries = [Cpu, CpuNoAvx]`, `WhisperConfig.cs:112`).

---

## Решение (резюме)

- **Сохранить ядро** ASR без изменений: `SubtitlesASR.cs` (`AudioReader` → Whisper.net `WhisperCppASRService` / `FasterWhisperASRService`), `SubtitlesManager`/`SubManager`, конфиг Whisper, загрузка моделей, вкладка `SettingsSubtitlesASR`, весь поток `Batch/*`, FFmpeg-декод и Vortice/DirectX (нужен для воспроизведения и позиционирования битмап-субтитров).
- **Облегчение делать слоями, от безопасного к рискованному:**
  - **Сейчас (после ревью baseline):** убрать GPU-рантаймы Whisper Cuda+Vulkan (~166 МБ, без потери фич, только GPU-ускорение). Это крупнейший выигрыш.
  - **Безопасно (с FLAG):** удалить онлайн-загрузчик субтитров (диалог+VM+provider), интерактивный экспорт SRT (но сохранить флаг `SubsExportUTF8WithBom` — его использует Batch), PDIC.
  - **С усилием (с FLAG):** OCR (Tesseract + Microsoft OCR, ~12.7 МБ), yt-dlp плагин, словарная панель/попап/боковая панель вместе с LibNMeCab+IpaDic (~51 МБ) и Lingua (~72 МБ), запись/снимки.
  - **Не трогать как «удаление»:** Translation (требуется Batch), плагин `FlyleafLib/Plugins/OpenSubtitles.cs`, Vortice/FFmpeg, SevenZip (распаковка faster-whisper), UTF.Unknown (детект кодировки сайдкар-субтитров).
- **Модернизация — только презентационный слой**, не трогая ASR/runtime-контракты: токенизация темы и опциональный light/Mica, отзывчивый layout, кэширование вкладок настроек, пулинг элементов оверлея субтитров, гайд-онбординг ASR, действенные ошибки, snackbar-фидбэк, поиск в настройках, командная палитра, drag-and-drop.

---

## Часть A — Что можно убрать (сохранив speech-to-text)

| Фича | Зависит ли ASR | Безопасность удаления | Радиус | Frozen | Выигрыш (deps/размер) | Усилие | Рекомендация |
|---|---|---|---|---|---|---|---|
| **GPU-рантаймы Whisper Cuda+Vulkan** | indirect (opt-in) | Безопасно (не дефолт) | `RuntimeLibraryOrder` (`SubtitlesASR.cs:972`); фильтрация enum в UI ASR | dependency-baseline:39,42,75 | **~166 МБ** (cuda 72 + vulkan 94) | M | **Убрать после ревью baseline** — крупнейший выигрыш, фичи не теряются (fallback на CPU) |
| Онлайн-загрузчик субтитров (диалог) | none | Безопасно | `OpenSubtitlesProvider` + `SubtitlesDownloaderDialog(VM)` + команда/keybind/DI | product-behavior, config-data | пакета нет, ~530 LOC | M | **Убрать** (если онлайн-загрузка вне scope). НЕ трогать плагин `FlyleafLib/Plugins/OpenSubtitles.cs` |
| Экспорт SRT (интерактивный диалог) | none | Безопасно | `SrtExporter` + диалог/VM + команда/enum + `PopupMenu.xaml:478` + `SubtitlesSidebarVM:107` | product-behavior, config-data | ~41 LOC, ничтожно | S | **Оставить** (входит в keep-goal «показать/экспортировать текст»; Batch имеет свой `SrtSubtitleWriter`). Если убирать — **сохранить флаг `SubsExportUTF8WithBom`** (его читает Batch) |
| PDIC (`PDICSender`) | none | Безопасно | `WordPopup` ветка `WordClickAction.PDIC`; `PDICPipeExecutablePath` + enum | config-data (persisted enum) | ~101 LOC | S | **Убрать** вместе со словарным попапом; миграция конфига для `WordClickAction.PDIC` |
| OCR (Tesseract + Microsoft OCR) | none | С усилием | `Player.SubtitlesOCR`, `DecoderContext:158`, `Subtitles.Reset/Refresh/Load`, `Player.Screamers.VASD:544`, вкладка/диалог, `TesseractModel`, enum/config | product-behavior, config-data (`OCREngine`, `*OcrRegions`), dependency-baseline (TesseractOCR) | **~12.7 МБ** native (x64 6.9 + x86 5.8) + ~533 LOC | M | **Кандидат** (битмап-субтитры вне scope). Чисто отделяемо (~10 call sites). FLAG |
| yt-dlp плагин (онлайн-видео) | none | С усилием | отдельный проект (`LLPlayer.slnx`), динамическая загрузка; CI `build-package/action.yml`, `ship.ps1` | dependency-baseline (yt-dlp-маркеры), product-behavior | yt-dlp.exe не бандлится; снимает шаг загрузки в релизе | M | **Кандидат** для local-only транскрайбера. Нет compile-связи, но трогает frozen-упаковку. FLAG |
| Словарь/WordPopup/боковая панель (+LibNMeCab/IpaDic, Lingua) | none | С усилием | `SubtitlesControl.xaml` (встроены `WordPopup`+`SelectableSubtitleText`), `MainWindow` sidebar, `AppActions` Cmd*Sidebar*, `AppConfig` ~15 Word/Sidebar props | product-behavior, **wpf-design**, config-data, dependency-baseline (NMeCab, IpaDic, Lingua) | **~51 МБ** IpaDic + **~72 МБ** Lingua + PDIC | L | **Оставить** для языкового плеера (это смысл продукта). Удалять только при редизайне в чистый транскрайбер — придётся переписать `SubtitlesControl` на `TextBlock`/`OutlinedTextBlock` |
| Запись/снимки/ремукс | none | С усилием | `Player.Extra.cs`, `DecoderContext:800-859`, `AudioDecoder:277-283`, `Renderer.Snapshot`, **`KeyBindingAction` enum** | media-runtime, product-behavior, **config-data (enum персистится по имени)** | пакета нет (FFmpeg/Vortice уже нужны) ⇒ ~0 | M | **Оставить** (нулевой dep-выигрыш + риск ломки персистентных конфигов через enum). Низкий ROI |
| Перевод (Translation/*, DeepL.net) | **indirect, но Batch — hard ref** | **Рискованно** | `SubManager` ctor, `DisplayText/UseTranslated`, `BatchSubtitleProcessor` стадия translate, `WordPopup`, `SettingsSubtitlesTrans`, 12 типов в JSON-маппинге, frozen `Config.SubtitlesConfig` | product-behavior, config-data (JSON-форма), dependency-baseline (DeepL) | DeepL.net ~151 КБ + ~3136 LOC | L | **Must-keep** в текущем виде. Удаление = развязать Batch (no-op переводчик) + срезать `Translated*` из SubManager. Не удалять без согласия |
| Плагин `FlyleafLib/Plugins/OpenSubtitles.cs` | none (но core open) | **Рискованно** | единственный `IOpenSubtitles`+`ISearchLocalSubtitles`; `DecoderContext.Open` (открытие внешних субтитров, авто-поиск сайдкар, в т.ч. Batch) | product-behavior, media-runtime | — | — | **Must-keep**. Не путать с загрузчиком-диалогом |
| Video rendering (Vortice/DirectX) | none для ASR, **required для плеера** | **Рискованно** | весь FlyleafHost/Renderer, FlyleafOverlay, битмап-субтитры, hw-decode (D3D11 = ffmpeg hw_device) | media-runtime, wpf-design | ~2.1 МБ render-only DLL | L | **Оставить**. Удаление = редефиниция в headless-транскрайбер, не slimming |

### Убрать безопасно (после FLAG)
- **GPU-рантаймы Whisper Cuda+Vulkan (~166 МБ)** — самый большой выигрыш, без потери пользовательских фич (только скорость на GPU; есть fallback на CPU). Требует ревью `dependency-baseline.md`.
- **Онлайн-загрузчик субтитров** (только `OpenSubtitlesProvider` + диалог/VM + команда/keybind; плагин не трогать).
- **PDIC** (с миграцией enum в конфиге).

### Убрать с усилием (после FLAG)
- **OCR** (Tesseract + Microsoft OCR, ~12.7 МБ; ~10 call sites, механически).
- **yt-dlp плагин** (отдельный проект; трогает frozen-упаковку и CI).
- **Словарь/WordPopup/боковая панель + LibNMeCab/IpaDic (~51 МБ) + Lingua (~72 МБ)** — высокий выигрыш по размеру, но требует переписать `SubtitlesControl` и убирает headline-функцию языкового обучения.
- **Запись/снимки** (низкий ROI; риск ломки персистентного `KeyBindingAction` enum).

### Рискованно / оставить
- **Перевод** — `asr_breaks_if_removed=true` по факту коупла (Batch hard ref + `SubManager`/`SubtitleData` отображение). **Must-keep**, пока Batch не развязан.
- **Плагин OpenSubtitles** — load-bearing для открытия внешних субтитров. **Must-keep**.
- **Vortice/DirectX rendering** — нужен для воспроизведения и hw-decode. **Оставить**.

### Ядро (не трогать)
- `SubtitlesASR.cs` (оркестратор + `AudioReader` Demuxer/AudioDecoder/swr → 16 кГц WAV), `WhisperCppASRService` (дефолт), `FasterWhisperASRService`, CPU-рантаймы (`Cpu`+`CpuNoAvx`).
- `SubtitlesManager`/`SubManager`/`SubtitleData`.
- `WhisperConfig`/`WhisperCppConfig`/`FasterWhisperConfig`, `WhisperCppModel` + загрузчик моделей + диалоги загрузки, вкладка `SettingsSubtitlesASR`.
- Весь `FlyleafLib/MediaPlayer/Batch/*`.
- FFmpeg-bindings + Vortice + `SevenZip` (распаковка faster-whisper) + `UTF.Unknown` (детект кодировки сайдкар-субтитров).

---

## Часть B — Модернизация (внешний вид / отзывчивость / взаимодействие)

### Тема 1 — Визуал / тема

**Win11 Mica + light theme.** Текущее: окно непрозрачное, `BaseTheme` жёстко `Dark`. Проблема: плоский устаревший хром, нет светлой темы. Предложение: добавить Mica-фон и селектор Dark/Light/Follow-OS. WPF: P/Invoke `DwmSetWindowAttribute`/`SetWindowCompositionAttribute` + `PaletteHelper` MaterialDesign. Усилие M / эффект высокий. **FLAG** (wpf-design-contract — тёмная тема как ожидание).

**Гигиена токенов.** Текущее: ~12 файлов используют hex-литералы, радиусы ad-hoc, тяжёлые тени; `FlyleafBar`/`WordPopup` хардкодят `White`/серый. Проблема: литералы игнорируют акцент и светлую тему; нечитаемо поверх произвольного видео. Предложение: перевести цвета на `DynamicResource`-кисти, ввести шкалу радиусов и type ramp, облегчить тени, добавить scrim под баром/субтитрами. WPF: только XAML, аддитивно. Усилие M / эффект средне-высокий. **FLAG** (шрифты/тени в контракте).

### Тема 2 — Layout / отзывчивость

**Кэш вкладок настроек.** Текущее: `SettingsDialog` синхронно создаёт `new SettingsXxx()` на UI-потоке при каждом выборе узла, без кэша (`SettingsDialog.xaml.cs:17-90`). Проблема: заметный фриз на тяжёлых страницах (ASR/Trans/Keys), потеря состояния скролла. Предложение: `Dictionary<string,UserControl>` с ленивым созданием, либо `ContentControl` + `DataTemplateSelector`. WPF: ~10 строк, риск низкий (страницы биндятся к live `FL.Config`). Усилие S / эффект средний.

**Пулинг элементов оверлея субтитров.** Текущее: `SelectableSubtitleText.SetText` (`:218-370`) полностью очищает и пересоздаёт `WrapPanel` (регекс/MeCab токенизация, на каждое слово — `OutlinedTextBlock`+`Border`+6 подписок+2 лямбды) при каждой смене реплики, для первичного и вторичного субтитра. Проблема: доминирующая стоимость layout оверлея, GC-давление, утечки обработчиков. Предложение (этап 1, безопасно): пул переиспользуемых элементов по индексу, общие static-обработчики вместо лямбд, мемоизация токенизации по (text, language). Этап 2 (L, contract-sensitive): один `OutlinedTextBlock` на реплику с hit-testing через `FormattedText.GetTextBounds`. WPF: этап 1 — низкий риск. Усилие M / эффект высокий.

**Адаптивный layout окна и бара.** Текущее: фиксированные 3 колонки (`MainWindow.xaml:49-135`), боковая панель с абсолютной `Width`, бар из 12 фикс-колонок без overflow (`FlyleafBar.xaml`), `ContextMenu` открывается хаком `BeginStoryboard`. Проблема: на узком окне сайдбар вытесняет видео, кнопки бара клипаются. Предложение: `MinWidth/MaxWidth` + breakpoint авто-сворачивания сайдбара; перенос второстепенных кнопок в MaterialDesign `PopupBox` overflow; замена storyboard-хака на `ContextMenuService`/attached behavior. WPF + MDIX 5.3.1 (`PopupBox` уже используется). Усилие M / эффект средний. **FLAG** (рестайл бара трогает wpf-design-contract).

**Throttle записей при resize.** Текущее: `FlyleafOverlay_OnSizeChanged`/`SubtitlePanel_OnSizeChanged` пишут `ScreenWidth/Height`/`SubsPanelSize` в общий Config на каждый кадр resize, что запускает обратную связь resize→config→relayout. Предложение: debounce через `DispatcherTimer`, коммит по концу жеста (с гарантией финального значения). WPF, риск низкий, без контрактного влияния. Усилие S / эффект низкий.

### Тема 3 — Взаимодействие / UX

**Гайд-онбординг ASR (first-run).** Текущее: онбординга нет вообще; модель Whisper нужно скачать вручную, иначе ASR не работает; функция доступна лишь через правый клик → Subtitles → ASR. Проблема: главная возможность продукта недоступна для нового пользователя. Предложение: одноразовый `WelcomeDialog` (Prism dialog, как `WhisperModelDownloadDialogVM`) — степпер: объяснить ASR → выбрать движок → встроенная загрузка модели → подтверждение; флаг `completedOnboarding` в конфиге. Усилие M / эффект высокий. **FLAG** (config-data + product-behavior).

**Действенная ошибка ASR.** Текущее: при отсутствии модели `OpenSubtitlesASRAction` поднимает тяжёлый topmost-модальный `ErrorDialog` с текстом «download it from the settings» и без кнопки перехода. Предложение: для `KnownErrorType.Configuration` — non-modal snackbar с кнопкой «Open ASR Settings»/«Download Model», deep-link во вкладку/диалог. MDIX `Snackbar` уже в баз­лайне. Усилие M / эффект высокий. **FLAG** (стиль ErrorDialog в контракте — меняем маршрутизацию, не диалог).

**Поиск в настройках + basic/advanced.** Текущее: фикс-180px `TreeView`, ~15 узлов, без поиска; вкладка ASR — ~25 jargon-параметров в одном скролле + двухсписочный transfer-виджет приоритета библиотек. Предложение: поле поиска над деревом; в ASR — группа Basic (Engine, Model+download, Language/Auto-detect) и свёрнутый `Expander` Advanced. Чистый XAML/VM. Усилие M / эффект высокий. **FLAG-low** (wpf-design).

**Determinate-прогресс загрузок.** Текущее: все 3 диалога загрузки — `IsIndeterminate` + сырой счётчик байт для GB-загрузок. Предложение: determinate `Value/Maximum` + проценты/ETA (размер модели уже доступен `WhisperCppModel.Size`). WPF, риск низкий. Усилие S / эффект средний.

**Глобальный snackbar + статус-чип «ASR: транскрибирование».** Текущее: фидбэк — единичный OSD в углу оверлея, ошибки — модал. Предложение: один app-wide `SnackbarMessageQueue` (DI) + чип в `FlyleafBar` на флаг активной транскрипции. Усилие M / эффект средний.

**Командная палитра + drag-and-drop + empty-state.** Текущее: действия только через горячие клавиши/F1 CheatSheet (уже searchable+runnable); открытие медиа — через контекст-меню; пустое окно без подсказок. Предложение: палитра Ctrl+K (переиспользует модель действий CheatSheet), `AllowDrop` на окно/оверлей → существующий `Commands.Open`, empty-state «Открыть/перетащить файл». WPF, риск низкий, частично reuse. Усилие S–M / эффект средний. **FLAG** (drag-drop/навигация — product-behavior).

**Корректный Save/Cancel настроек.** Текущее: TODO «Implement cancel» (`SettingsDialog.xaml:219`) — «Close» не откатывает изменения сессии. Предложение: снапшот конфига при открытии, restore на Close. Усилие M / эффект средний. **FLAG** (config-data — семантика мутаций).

### Два варианта общего направления визуала

| Критерий | **Вариант A: рефреш MaterialDesign 5** | **Вариант B: Fluent/WinUI-3 + Win11 Mica** |
|---|---|---|
| Сложность | Низкая–средняя (XAML + токены, пакет уже есть) | Высокая (interop Mica/acrylic, новый язык дизайна, возможно WPF-UI пакет) |
| Риск | Низкий (аддитивно) | Средний–высокий (P/Invoke, риск регрессии видео-рендера) |
| Эффект | Средний (свежее, но узнаваемо) | Высокий (нативный Win11-вид) |
| Совместимость с frozen wpf-design-contract | Высокая — остаёмся на MDIX 5.3.1 (`dependency-baseline` заморожен), меняем токены/тему аддитивно | Низкая–средняя — Mica/light противоречат «dark-only» ожиданию; новый пакет нарушает frozen baseline |
| Усилие | M | L |

**Рекомендация: Вариант A** как основная линия (токенизация темы, светлая тема опционально, scrim/elevation, анимации появления бара/OSD) — он укладывается в frozen `dependency-baseline` и `wpf-design-contract` с минимальным риском. **Mica/light (элементы Варианта B)** добавить позже как опциональный режим под отдельный FLAG, без смены UI-фреймворка и без бампа пакетов.

---

### Liveness / обратная связь долгого ASR (Batch и в плеере)

#### Краткий диагноз: почему сейчас «непонятно, работает ли»

В пакетном режиме строка файла в `RunningASR` стоит мёртвой от начала и до конца распознавания. Причина — потоковая природа ASR теряется на границе batch-слоя:

- **ASR на самом деле стримит посегментно.** `AudioReader.ReadAll(...)` вызывает callback на каждый распознанный сегмент по мере декодирования чанков (consumer-петля в `SubtitlesASR.cs`, `await foreach` по `WhisperCppASRService.Do`). Каждый сегмент несёт `Text`, `StartTime`, `EndTime` в абсолютном медиа-времени.
- **Но batch буферизует и отдаёт всё одним куском.** `BatchAsrTranscriber.TranscribeInternal` (`BatchAsrTranscriber.cs:39-62`) в своём `addSub`-лямбда-callback только делает `subtitles.Add(...)` в локальный `List` и возвращает целый `BatchAsrResult` лишь в конце (`:74`). `BatchSubtitleProcessor.ProcessAsync` делает один `await _asrTranscriber.TranscribeAsync(...)` (`:65`), блокируясь до конца файла.
- **Прогресс-события приходят только на границах фаз.** За весь ASR UI получает РОВНО одно событие — переход в `RunningASR` (`:63`). `SubtitleCount` выставляется единожды, уже при `QueuedForTranslation` (`:71`). Модель `BatchSubtitleProgress` (`BatchSubtitleModels.cs:67-73`) не имеет ни поля времени, ни позиции; `BatchSubtitleJob` (`:28-44`) — только `SubtitleCount`/`StartedAt`/`CompletedAt`.
- **Тотальная длительность выбрасывается.** `Demuxer.Duration` существует (`Demuxer.cs:32`, тики 100 нс) и читаем в `MediaAudioProbe.Probe` до `Dispose` (`MediaAudioProbe.cs:57`), но `MediaAudioProbeResult` (`:9-13`) несёт только `MediaPath/StreamIndex/MediaType/Language` — длительность теряется. Значит «processed / total» и детерминированный бар не из чего строить.
- **Нативный progress-callback Whisper.net не подключён.** Греп по `WithProgressHandler/WithSegmentEventHandler/WithPrintProgress` — ноль совпадений; `ConfigureBuilder` (`WhisperConfig.cs`) задаёт только язык/потоки/длину сегмента/температуру. Он и не нужен — посегментные timestamps дают более тонкий сигнал.

Итог: данные о «живости» (текст, время, скорость) реально текут внутри `FlyleafLib`, но не пересекают границу `IBatchAsrTranscriber` → процессор → VM. **In-player ASR этим уже не страдает:** тот же `ReadAll` посегментно зовёт `_subtitlesManager[i].Add(sub)` (`SubtitlesASR.cs:240`), плюс боковая панель крутит индетерминированный `IsLoading`-спиннер. Пробел — только в batch-диалоге (и в плеере отсутствует детерминированный прогресс/ETA, но не отсутствие живости).

#### Таблица сигналов (только подтверждённое верификацией кода)

| Сигнал | Доказывает «живость» | Доступно сегодня | Как в WPF | Усилие | Эффект |
|---|---|---|---|---|---|
| **Живой счётчик Subs** (тикает вверх во время ASR) | Число растёт (12… 47… 103) — самое дешёвое однозначное «живо». Без знания длительности. | Частично: данные есть посегментно, но `SubtitleCount` идёт один раз в конце. | Добавить `IProgress<int>`/`Action<int>` в `IBatchAsrTranscriber.TranscribeAsync`; внутри `addSub`-лямбды (`BatchAsrTranscriber.cs:61`) звать sink с `subtitles.Count`; процессор зовёт существующий `Report(..., subtitleCount: count)`. **VM/XAML менять не нужно** — колонка `Subs` (`xaml:100`) и `uiJob.SubtitleCount = update.SubtitleCount.Value` (`VM:211`) уже live. | **S** | high |
| **Поток живого текста** (распознанные строки активного файла в скролл-панель) | Сильнейшее доказательство: пользователь буквально видит появляющийся текст — ровно его пожелание. | Частично (нужна проводка): стрим есть, но заперт в `addSub`-замыкании. | Тот же per-segment callback (или `IProgress<BatchAsrSegment>` с `MediaPath/Text/StartTime`); в VM завести `Progress<BatchAsrSegment>` (зеркало существующего `Progress` на `VM:202`), писать в `ObservableCollection<string>` активного джоба, маршрутизация по `MediaPath` (канал ёмкости 1, `Processor.cs:33-39` ⇒ одновременно один ASR). `ListBox` рядом с гридом + auto-scroll в code-behind; ring-buffer ~200 строк. | **M** | high |
| **Детерминированный бар + «mm:ss / total»** на активной строке | Заполняющийся к известному тоталу бар — каноническое «вот насколько готово»; закрывает и просьбу про ETA. | Частично (нужна проводка длительности + позиции). | (1) Добавить `Duration` в `MediaAudioProbeResult`, читать `demuxer.Duration` до `Dispose` (`MediaAudioProbe.cs:57`). (2) Протянуть позицию = `data.EndTime` последнего сегмента через новый sink. (3) Добавить `ProcessedTime/TotalDuration` в `BatchSubtitleProgress` и `BatchSubtitleJob`. (4) `DataGridTemplateColumn` с `ProgressBar` + новый `TimeSpan→string` конвертер (его нет в `GeneralConverters.cs`). При `Duration==0` (live/HLS) ⇒ `IsIndeterminate=true`. **Шаги дискретны: бар прыгает на границе чанка, не плавно.** | **M** | high |
| **Throughput «xN realtime»** на активной строке | «2.8x realtime» — яркое «движок молотит», заодно база для ETA; «0.0x» мгновенно выдаёт затык. | Частично: только хвост от детерминированного бара — нужна та же проводка позиции. | В VM-handler: `factor = processedSeconds / (Now - job.StartedAt).TotalSeconds`, формат `"x{0:0.0} realtime"`, бинд в строку/бейдж. Сглаживать/показывать после первого сегмента (загрузка модели искажает первый чанк). Без проводки позиции из бара — НЕ standalone. | **M** (как самостоятельный) / **S** (поверх бара) | medium |
| **Indeterminate-спиннер на бегущей строке** | Подтверждает активность ДО первого сегмента (загрузка модели/первый чанк, когда бар ещё на 0). Сам по себе слабый (движение ≠ прогресс). | Да: чистый XAML по `Status==RunningASR`. Паттерн уже есть в плеере (`SubtitlesSidebar.xaml`, `MaterialDesignCircularProgressBar` по `IsLoading`). | `DataTrigger`/шаблонная колонка по `Status`. Пары с детерминированным баром (спиннер до появления позиции). | **S** | low |
| **Кадр-превью / запущенное видео** | Визуально убедительно, но доказывает декод/seek видео, а не распознавание. | Нет / нужна переработка: batch открывает ТОЛЬКО аудио. | См. явный вывод ниже. | **L** | low |

(«Доступно сегодня» помечено «частично» там, где исходный рецепт давал бы значение, обновляющееся один раз в конце файла — т.е. `feasible_as_stated=false`: единственный live-тик требует новой проводки per-segment sink, которую рецепт опускал.)

#### Рекомендованный МИНИМАЛЬНЫЙ набор «по умолчанию»

Внедрить первым, чтобы доверие появилось сразу и одной проводкой:

1. **Живой счётчик Subs (S)** — самый дешёвый видимый выигрыш: колонка и бинд уже есть, нужен только sink из `addSub` через существующий `Report`. Дальше всё остальное вешается на тот же per-segment callback.
2. **Поток живого текста активного файла (M)** — прямой ответ на дословную просьбу «видеть распознанный текст вживую». Переиспользует тот же sink (расширить полезной нагрузкой `Text/StartTime`).

Эти два используют ОДНУ общую проводку (per-segment sink через интерфейс транскрайбера → процессор → VM), безопасны по потокам (существующий `Progress<T>` создан на UI-потоке и маршалит через `SynchronizationContext`), и **не трогают рендер-стек** — batch-путь открывает только `Demuxer + AudioDecoder`.

**Опционально / позже (тот же sink + проводка длительности):**

3. **Детерминированный бар + «mm:ss / total» (M)** — добавляет `Duration` в probe и позицию (`data.EndTime`) в sink; даёт ETA. Помнить про дискретность шагов и fallback на indeterminate при `Duration==0`.
4. **Throughput xN (S поверх бара)** — чистая арифметика в VM-handler.
5. **Indeterminate-спиннер (S)** — закрывает «дыру до первого сегмента».

**Видео-превью — отдельно и только по явному запросу.**

#### Явный вывод по идее «показывать запущенное видео»

**Дорого, и в batch-пути это не просто дорого, а вне пути.** Подтверждено кодом: `BatchAsrTranscriber → AudioReader.Open` создаёт только `Demuxer + AudioDecoder` (`SubtitlesASR.cs:356-386`); `MediaAudioProbe` открывает демуксер лишь чтобы выбрать аудиопоток и сразу его `Dispose`. Нигде в `FlyleafLib/MediaPlayer/Batch` нет `VideoDecoder`/`Renderer`/swapchain. Показ кадра или видео потребовал бы:

- открыть ОТДЕЛЬНЫЙ видеодекодер на каждый файл,
- декодировать кадр по запросу на текущем processed-timestamp (он, к слову, известен из `EndTime` сегмента),
- поднять WPF-рендер-поверхность (Vortice/D3D11, к которой `VideoDecoder` жёстко привязан в конструкторе),
- и всё это конкурирует за CPU/GPU с самими ASR-потоками.

Это усилие **L**, низкого эффекта, и декодирует данные, которые ASR не нужны.

**Чем заменить (по убыванию убедительности и по возрастанию цены):**
- **Поток живого текста (M)** — равно убедителен («вижу слова») и дёшев, на той же аудио-проводке. Это и есть правильная замена.
- **Детерминированный бар + xN realtime (M)** — «вижу движение к финишу + скорость», полностью на аудио.
- **Кадр-превью** оставить только если пользователь прямо потребует именно визуальные кадры; поток текста при усилии M полностью закрывает заявленную потребность в уверенности.

---

Проверенные файлы: `FlyleafLib/MediaPlayer/Batch/BatchAsrTranscriber.cs`, `BatchSubtitleModels.cs`, `BatchSubtitleProcessor.cs`, `MediaAudioProbe.cs`; `FlyleafLib/MediaPlayer/SubtitlesASR.cs`; `FlyleafLib/MediaFramework/MediaDemuxer/Demuxer.cs`; `LLPlayer/ViewModels/BatchSubtitlesDialogVM.cs`; `LLPlayer/Views/BatchSubtitlesDialog.xaml`. Грепы `WithProgressHandler/WithSegmentEventHandler/WithPrintProgress` — ноль совпадений (нативный Whisper.net progress-callback не подключён и не нужен).

---

## Trade-off анализ

- **Размер vs. фичи.** Крупнейшие выигрыши по размеру лежат в данных языковых функций: GPU-рантаймы (~166 МБ, без потери фич) ≫ Lingua (~72 МБ) ≈ IpaDic (~51 МБ) > Tesseract (~12.7 МБ). Но всё, кроме GPU-рантаймов, удаляет реальную пользовательскую ценность языкового плеера. **Лучший ROI без жертв — именно GPU-рантаймы.**
- **Чистота vs. coupling.** Translation даёт большой выигрыш по сложности кода (~3136 LOC), но он структурно вплавлен в Batch (keep) и `SubManager`. Его удаление — это рефакторинг (no-op переводчик + срезка `Translated*`), а не «удаление», и трогает frozen JSON-форму конфига.
- **Slimming vs. редефиниция продукта.** Удаление словаря/попапа/сайдбара и Vortice превращает продукт из «языкового плеера» в «headless-транскрайбер». Это не slimming, а смена продуктовой стратегии — вне scope без явного решения.
- **Frozen-стоимость.** Почти каждое удаление трогает ≥1 frozen-контракт (product-behavior + config-data + dependency-baseline) и `scripts/codex/verify-frozen.ps1`. Стоимость согласования часто превышает технический выигрыш для мелких фич (экспорт SRT, запись).
- **Модернизация — низкорисковая, высокоокупаемая.** Презентационные изменения (кэш вкладок, пулинг оверлея, онбординг, snackbar) не трогают runtime-контракты и дают наибольший UX-выигрыш на единицу риска.

---

## Последствия (что станет проще / сложнее, что пересмотреть)

**Станет проще:**
- Сборка/инсталлятор легче на ~166 МБ сразу после удаления GPU-рантаймов; до ~+136 МБ при удалении словарных данных.
- Меньше поверхностей настроек/диалогов → проще IA и онбординг.
- Презентационные правки ускоряют UI (нет фризов настроек, дешевле оверлей).

**Станет сложнее / что пересмотреть:**
- **Frozen-контракты придётся править** под каждое удаление: `dependency-baseline.md` (+ `verify-frozen.ps1`), `product-behavior-contract.md`, `config-data-contract.md`, при UI — `wpf-design-contract.md`. Требуется явное согласие владельца.
- **Миграция конфига:** удаление enum-значений (`WordClickAction.PDIC`, при записи — `KeyBindingAction.*`) ломает десериализацию сохранённых конфигов → нужен fallback/маппинг.
- **CPU-fallback smoke** обязателен после снятия GPU-рантаймов (`dependency-baseline:75` прямо требует ревью + smoke).
- **Batch-перевод:** если когда-либо удалять Translation — сначала сделать `IBatchSubtitleTranslator` опциональным/no-op, иначе сборка Batch падает.
- **Тесты** `FlyleafLibTests/MediaPlayer/Batch/*` придётся переработать при изменении Batch/Translation.

---

## Поэтапный план действий (чек-лист по фазам)

### Фаза 0 — Подготовка (без удалений)
- [ ] Снять текущий размер Release-сборки как baseline (по папкам runtime/Lingua/IpaDic/Tesseract).
- [ ] Подтвердить с владельцем целевую идентичность продукта: «языковой плеер» (оставляем словарь/перевод) vs. «транскрайбер» (можно резать больше). **FROZEN-решение — требует явного согласия.**

### Фаза 1 — Модернизация без контрактного риска (можно начинать сразу)
- [ ] Кэш вкладок `SettingsDialog` (S).
- [ ] Throttle записей resize в Config (S).
- [ ] Determinate-прогресс в диалогах загрузки моделей/движка/Tesseract (S).
- [ ] Пулинг элементов + общие обработчики + мемоизация токенизации в `SelectableSubtitleText` (этап 1) (M).
- [ ] Глобальный `SnackbarMessageQueue` + статус-чип ASR в `FlyleafBar` (M).
- [ ] Командная палитра (reuse CheatSheet), drag-and-drop открытие, empty-state (S–M).
- [ ] **Liveness (минимум): живой счётчик Subs + поток распознанного текста активного файла** — одна общая per-segment проводка `IBatchAsrTranscriber`→процессор→VM, не трогает рендер-стек (S+M).
- [ ] Liveness (далее): детерминированный бар + «mm:ss/total» + throughput xN (протянуть `Demuxer.Duration` в probe и `EndTime` сегмента в sink) + indeterminate-спиннер до первого сегмента (M).

### Фаза 2 — Безопасное облегчение (требует FLAG)
- [ ] **FROZEN:** Удалить GPU-рантаймы Whisper **Cuda + Vulkan** из `LLPlayer.csproj:28,31`; отфильтровать enum в `SettingsSubtitlesASR`; ревью `dependency-baseline.md:39,42,75` + `verify-frozen.ps1`; **CPU-fallback smoke**. (~166 МБ).
- [ ] **FROZEN:** Удалить онлайн-загрузчик субтитров (`OpenSubtitlesProvider` + `SubtitlesDownloaderDialog(VM)` + команда/keybind/DI). **Плагин `FlyleafLib/Plugins/OpenSubtitles.cs` НЕ трогать.** Правка product-behavior + config-data.
- [ ] **FROZEN:** Удалить PDIC (`PDICSender`, ветка `WordClickAction.PDIC`, `PDICPipeExecutablePath`) с миграцией enum в config-data.

### Фаза 3 — Облегчение с усилием (требует FLAG и решения по идентичности)
- [ ] **FROZEN:** OCR — удалить `SubtitlesOCR`, `TesseractModel`, диалог/вкладку, ветки в `Subtitles.Load`/`Reset`/`Refresh`, `Player.SubtitlesOCR`, пакет TesseractOCR, enum/config (`OCREngine`, `*OcrRegions`). Правка 3 контрактов. (~12.7 МБ).
- [ ] **FROZEN:** yt-dlp плагин — убрать проект из `LLPlayer.slnx`, шаг загрузки yt-dlp в `build-package/action.yml`/`ship.ps1`, маркеры в `dependency-baseline.md`. Изменение политики упаковки.
- [ ] **FROZEN (только при редефиниции в транскрайбер):** Словарь/WordPopup/сайдбар + LibNMeCab/IpaDic + Lingua + PDIC — переписать `SubtitlesControl.xaml` на `OutlinedTextBlock`/`TextBlock`, снять `Cmd*Sidebar*`, ~15 `Word/Sidebar` props. Правка product-behavior + wpf-design + config-data + dependency-baseline. (~123 МБ).
- [ ] **FROZEN (низкий ROI):** Запись/снимки — стабить действия, НЕ удалять enum-члены `KeyBindingAction` (или сделать миграцию по имени), иначе ломаются персистентные конфиги.

### Фаза 4 — Модернизация повышенного риска (требует FLAG)
- [ ] **FROZEN:** Гайд-онбординг ASR (`WelcomeDialog`) + флаг `completedOnboarding` (config-data).
- [ ] **FROZEN:** Действенная ошибка ASR через snackbar с deep-link (вместо тупикового модала).
- [ ] **FROZEN:** Поиск в настройках + basic/advanced split вкладки ASR (wpf-design).
- [ ] **FROZEN:** Реальный Cancel/revert настроек (снапшот конфига) (config-data).
- [ ] **FROZEN:** Токенизация темы + опциональные light/Mica (Вариант A; Mica-элементы Варианта B — отдельным FLAG, без бампа MDIX).

### Фаза 5 — Только при явном решении удалять Translation
- [ ] **FROZEN / RISKY:** Развязать Batch — сделать `IBatchSubtitleTranslator` опциональным/no-op (`BatchSubtitleProcessor`, `BatchSubtitleConfigSnapshot`, `BatchSubtitlesDialogVM:222`).
- [ ] **FROZEN / RISKY:** Нейтрализовать `SubTranslator` (`DisplayText = Text`), срезать `Translated*/UseTranslated` из `SubManager`/`SubtitleData`, перепривязать UI субтитров к `Text`; убрать 12 типов из JSON-маппинга; удалить DeepL.net. Меняет frozen `Config.SubtitlesConfig` + JSON-форму; переработать `BatchSubtitle*Tests`.

---

**Релевантные файлы (абсолютные пути):**
- Ядро ASR: `C:\Users\Maxim\Documents\GitHub\LLPlayer_ru\FlyleafLib\MediaPlayer\SubtitlesASR.cs`, `...\FlyleafLib\MediaPlayer\SubtitlesManager.cs`, `...\FlyleafLib\Engine\WhisperConfig.cs`, `...\FlyleafLib\Engine\WhisperCppModel.cs`, `...\FlyleafLib\MediaPlayer\Batch\*`
- Зависимости/упаковка: `C:\Users\Maxim\Documents\GitHub\LLPlayer_ru\LLPlayer\LLPlayer.csproj` (рантаймы строки 27–31), `...\FlyleafLib\FlyleafLib.csproj`
- Frozen-контракты: `C:\Users\Maxim\Documents\GitHub\LLPlayer_ru\docs\agent\{dependency-baseline,product-behavior-contract,config-data-contract,wpf-design-contract,media-runtime-contract}.md`
- UI для модернизации: `...\LLPlayer\Views\SettingsDialog.xaml(.cs)`, `...\LLPlayer\Controls\SelectableSubtitleText.xaml.cs`, `...\LLPlayer\Views\FlyleafOverlay.xaml(.cs)`, `...\LLPlayer\Controls\FlyleafBar.xaml`, `...\LLPlayer\Views\MainWindow.xaml`, `...\LLPlayer\Resources\MaterialDesignMy.xaml`