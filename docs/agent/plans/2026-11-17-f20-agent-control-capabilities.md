# F-20 — покрытие функций и настроек через MCP/API

Статус — **PLANNED**, 2026-11-17. Реализация запланирована на следующую сессию.
Матрица описывает существующие функции Avalonia Windows/Linux и согласованные точки управления ими.
Общая архитектура задана [дизайном](2026-11-17-f20-agent-control-design.md), порядок реализации —
[планом](2026-11-17-f20-agent-control-plan.md). Имена операций ниже — предложение для единого
типизированного контракта, который используют MCP и локальный HTTP API.

Фоновый ControlHost владеет ML-заданиями, GPU-допуском, настройками и ресурсами. GUI сохраняет native
player и принимает адресованные ему команды через IPC. Attach выбирает конкретный GUI-сеанс.
Согласованная функциональность относится к Avalonia Windows/Linux. Проверка совместимости WPF
сохраняется в плане поставки.

## Правила покрытия

- Подключённому клиенту согласован полный доступ к существующим функциям, включая скачивание и удаление
  управляемых моделей, сохранение настроек, явную перезапись результатов и установку/очистку credentials.
  Для чтения секретов возвращаются маска и признак наличия. Значения не попадают в каталог, события и ошибки.
- `R` в таблицах означает чтение. `W` означает действие с побочным эффектом. Это описание операции
  для клиента, а не дополнительный запрос подтверждения поверх согласованного полного доступа.
- Каждая операция описывает входную/выходную схему, текущую доступность, требования к GUI, ОС,
  ресурсам и состоянию медиа. Отсутствие камеры, внешнего движка, модели, провайдера или GUI имеет
  конкретную причину и способ устранения. Запись предпочтения и фактически применённое значение различимы.
- Фоновые операции обработки работают через общие сервисы. Команды окна выполняются в присоединённом
  GUI-сеансе. В фоновом режиме такая команда возвращает `requires_gui` с идентификатором нужного сеанса.
  Наличие каталога с пунктом «открыть диалог» не считается выполнением соответствующей операции обработки.
- Каталог не зависит от настроенных горячих клавиш. Параметризованные функции получают отдельные схемы.
  Типы native-движка, экземпляры view model и непрозрачные CLR-объекты не передаются по транспорту.
- Смена медиа, дорожки, слота или настроек сохраняет существующие проверки актуальности и порядок
  отмены/ожидания. Длительное задание сообщает фактический результат после окончания и остановки процессов.

## Общие операции

`capabilities.list/get`, `state.get`, `sessions.list/attach`, `jobs.get/list/cancel`, `events.subscribe`
описывают доступные функции, выбранную сессию, текущие задания, ошибки, прогресс и результаты.
Схемы должны различать статическую поддержку, текущую готовность и причины временной недоступности.
Формы JSON для медиа, дорожек, реплик, ресурсов и заданий используют стабильные публичные ID и ревизии.

`app.exit/restart` адресуются GUI-сессии и отсоединяют её от фоновых заданий. Отдельные `host.stop/restart`
закрывают admission и ожидают drain всех принадлежащих host заданий. Ответ на команду жизненного цикла
явно сообщает выбранного владельца и исход остановки.

## Матрица существующих возможностей

| Группа | Операции и параметры контракта | Доступ и исполнение | Основание и приёмочная проверка |
| --- | --- | --- | --- |
| 1. Медиа и воспроизведение | `media.open/cancel_open`, `media.recent/list/clear`, `media.neighbor.open`, `playback.get/play/pause/seek/set_speed/set_volume/set_mute`. Локальный путь или явный URL, время, громкость, скорость. | R/W. Обработка файла доступна фоновому хосту. Воспроизведение и его состояние принадлежат выбранной сессии. Повторный open того же пути имеет новую ревизию. | [IPlaybackController](../../../LLPlayer.Avalonia/Services/IPlaybackController.cs), [контроллер](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.cs), [навигация](../../../LLPlayer.Avalonia/ViewModels/MainWindowViewModel.LocalMedia.cs). Проверить повтор, отмену, ошибку и вытеснение open другим клиентом или GUI. |
| 2. Дорожки, главы, плейлисты | `streams.list/select`, `chapters.list/seek`, `playlist.list/select/next/previous/reopen`. Тип аудио/видео/субтитров, embedded/external, ID и ревизия текущего медиа, слот 0/1. | R/W. Фоновый анализ получает те же выбранные входы. Выбор для GUI проверяет живого владельца. Каталог возвращает текущий checked state и metadata. | [Streams](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.Streams.cs), [Playlist](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.Playlist.cs). Проверить разные источники с одинаковым индексом, устаревший ID, сохранение позиции/паузы и выбранной аудиодорожки. |
| 3. Субтитры и синхронизация | `subtitles.load/list/read/search/set_visibility`, `subtitles.seek_to_cue/synchronize`, `subtitles.set_delay`, `waveform.get/set_enabled`, `ab_loop.get/set/clear`. Текст/bitmap, source/translated/fallback, primary/secondary, literal/case/whole-word/regex. | R/W. Чтение и обработка доступны без окна. Синхронизация с playhead и A–B относятся к текущей сессии. Bitmap передаётся как ограниченный отдельный результат с метаданными. | [Sidebar](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.Sidebar.cs), [SubtitlesSidebarViewModel](../../../LLPlayer.Avalonia/ViewModels/SubtitlesSidebarViewModel.cs), [SubtitleExtras](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.SubtitleExtras.cs). Проверить обе дорожки, bitmap, неверный regex, поздние ответы и пустые субтитры. |
| 4. ASR и OCR | `recognition.start/pause/resume/cancel/status`, `ocr.run`, выбор движка/модели/языка/слота, параметров Whisper и установленных runtime. Возвращаются сообщения движка, прогресс и результат. | R/W. Обработка без окна обязательна. whisper.cpp и внешний faster-whisper используют существующие сервисы и GPU-допуск. Microsoft OCR доступен по фактической поддержке Windows. | [IRecognitionController](../../../LLPlayer.Avalonia/Services/IRecognitionController.cs), [RecognitionActions](../../../LLPlayer.Core/Services/RecognitionActions.cs), [RecognitionSettings](../../../LLPlayer.Core/Services/RecognitionSettings.cs). Проверить весь файл, cancel/drain, недостающую модель, CPU/GPU-очередь и OCR в поддерживаемой ОС. |
| 5. Перевод и словари | `translation.providers/list/check/models`, `translation.start/status/cancel`, `translation.slot.set_enabled`, `dictionary.lookup`, `translation.document.read/apply`. Целевой язык, provider/model, контекст и параметры, диапазон/весь документ, ревизия реплик. | R/W. Полный перевод документа доступен без окна. Клиент может использовать настроенный сервис плеера или собственную модель и вернуть переведённые тексты через roundtrip. Словарный lookup сохраняет существующую языковую семантику. | [TranslationSettings](../../../LLPlayer.Core/Services/TranslationSettings.cs), [TranslationViewModel](../../../LLPlayer.Avalonia/ViewModels/TranslationViewModel.cs), [контроллер](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.cs). Нужны обе реализации whole-document сценария, описанные ниже. |
| 6. Экспорт и скачивание субтитров | `subtitles.export`, `subtitle_download.search/page/download/load`. SRT/WebVTT/TXT, source/translated/fallback, BOM, метки спикеров SRT, путь и явная политика существующего файла. OpenSubtitles query/language/result/page. | R/W. Фоновому экспорту передаётся путь без file picker. OpenSubtitles использует предоставленные credentials и обычную сетевую ошибку. Загрузка в GUI связана с выбранным медиа и слотом. | [SubtitleExportSource](../../../LLPlayer.Avalonia/Services/SubtitleExportSource.cs), [SubtitlesExportViewModel](../../../LLPlayer.Avalonia/ViewModels/SubtitlesExportViewModel.cs), [OpenSubtitlesDownloadViewModel](../../../LLPlayer.Avalonia/ViewModels/OpenSubtitlesDownloadViewModel.cs). Сверить стили/тайминги/BOM, пустой результат, overwrite и смену медиа во время загрузки. |
| 7. Batch и Watch | `batch.scan/jobs/select_audio/set_included/start/status/cancel/retry`, `watch.get/start/stop`. Папка/recursive, задания, overwrite, serialize ASR/translate, CPU-while-active, dubbing, per-file audio. | R/W. Фоновая очередь работает без окна. Watch запускается явной командой. Смена папки отменяет прежнюю подписку. Повтор job сохраняет существующую семантику overwrite. | [BatchWorkflow](../../../LLPlayer.Core/Batch/BatchWorkflow.cs), [BatchSettings](../../../LLPlayer.Core/Batch/BatchSettings.cs), [BatchSubtitlesViewModel](../../../LLPlayer.Avalonia/ViewModels/BatchSubtitlesViewModel.cs). Проверить сканирование, частичный сбой/повтор, смену watch-root и завершение/отмену при отключении клиента. |
| 8. Модели и внешние ресурсы | `resources.list/get/download/delete/cancel`, `resources.directories/get`, `resources.source/get`, настройка существующего executable/model path. ID каталога, installed/present/available/in_use, размер и прогресс. | R/W. Фоновые операции используют текущую защиту активных моделей и принадлежащих приложению файлов. Автоустановка архива XXL доступна на Windows. Linux сообщает о ручном compatible executable. | [IModelManagementService](../../../LLPlayer.Core/Services/Models/IModelManagementService.cs), [ModelManagementService](../../../LLPlayer.Core/Services/Models/ModelManagementService.cs), [model admission](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.LanguageWorkflows.cs). Проверить загрузку/отмену/удаление, in-use и чужие/изменённые файлы. |
| 9. Слова, PDIC и Anki | `words.lookup/save/list/update/remove/clear/export`, `anki.send`, `pdic.lookup`. Detached слово/контекст/языки, редактируемые поля, фильтр, TSV/APKG, deck, результаты отправки. | R/W. Данные и экспорт доступны фоновому хосту. AnkiConnect обращается к существующему локальному сервису. PDIC требует Windows и доступного bridge. Clipboard/search browser требуют desktop-сеанса. | [WordLibrary](../../../LLPlayer.Core/Services/WordLibrary.cs), [WordManagerViewModel](../../../LLPlayer.Avalonia/ViewModels/WordManagerViewModel.cs), [AnkiConnectSender](../../../LLPlayer.Core/Services/AnkiConnectSender.cs), [PdicSettings](../../../LLPlayer.Avalonia/Services/PdicSettings.cs). Проверить dedup, редактирование/удаление, readonly при испорченном store, частичную отправку Anki и отсутствие PDIC на Linux. |
| 10. AI Insights | `insights.generate/status/cancel/copy/save`, `insights.vocabulary.merge`. Слот и source/translated transcript, выбранное существующее подключение LLM, summary/vocabulary и detached результаты. | R/W. Генерация/сохранение/merge доступны без окна. Clipboard относится к desktop-сеансу. Смена исходного transcript отклоняет устаревшие результаты. | [AiInsightsViewModel](../../../LLPlayer.Avalonia/ViewModels/AiInsightsViewModel.cs), [transcript owner](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.LanguageWorkflows.cs). Проверить cancel, generation invalidation и first-wins vocabulary merge. |
| 11. Дубляж и диаризация | `dubbing.voices/list`, `dubbing.cue_voice.get/set/clear`, `dubbing.render/status/cancel`, `dubbing.attach`, `diarization.start/status/cancel`. Имеющиеся default/custom/per-line voices, persistence, current-session batch bridge, F-03 SpeakerId. | R/W. Используются существующие локальные движки, GPU-допуск и подтверждённый drain. F-03 работает по выбранной локальной аудиодорожке. Настройка внешнего окружения возвращается как prerequisite. | [DubbingSettings](../../../LLPlayer.Core/Services/DubbingSettings.cs), [voice assignment](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.LanguageWorkflows.cs), [voice persistence](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.DubbingVoices.cs), [IDiarizationController](../../../LLPlayer.Avalonia/Services/IDiarizationController.cs). Проверить метки/override/сохранение, shared GPU и retry-stop. Будущие фазы F-16 учитываются отдельно после F-20. |
| 12. Медиабэкенды, устройства, запись | `backends.get/set_requested`, `devices.audio/list/refresh/select`, `capture.devices/formats/open`, `recording.start/stop/status`, `snapshot.take`, viewport/filter операции. Requested/effective decode/presentation, точный adapter/device/format ID. | R/W. Каталоги сообщают реальную поддержку ОС/оборудования. Presentation и экранный snapshot требуют соответствующего GUI/media surface. Capture/recording требуют существующих native-возможностей и готовой сессии. | [MediaBackends](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.MediaBackends.cs), [PlaybackSettings](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.PlaybackSettings.cs), [AudioSettings](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.AudioSettings.cs). Проверить stale device/format, requested/effective, fallback/recovery и запись результата. |
| 13. UI, диагностика, жизненный цикл | `ui.get/set`, `ui.window.open`, `commands.list/execute`, `app.about/diagnostics/new/clone/restart/exit`, clipboard/browser/reveal-path операции. Theme/fullscreen/topmost/sidebar, нужная сессия и typed параметры команды. | R/W. Изменение окна, диалоги, focus и desktop effects требуют attach к GUI. Фоновый host выдаёт точный `requires_gui`. Диагностика доступна host, действия exit/restart адресованы явно выбранному GUI или host по общему контракту жизненного цикла. | [IAppShell](../../../LLPlayer.Avalonia/ViewModels/IAppShell.cs), [app command owners](../../../LLPlayer.Avalonia/ViewModels/MainWindowViewModel.Commands.cs), [AppHost.Commands](../../../LLPlayer.Avalonia/AppHost.Commands.cs), [ExecutableCommands](../../../LLPlayer.Avalonia/Services/ExecutableCommands.cs). Проверить конкретного владельца, доступность, attach, сохранение pending jobs и drain перед выходом. |

## Восемь семейств настроек

Общие операции `settings.schema/read/validate/apply` адресуют семейство и конкретные поля.
`settings.credentials.set/clear` принимает новое значение либо явную очистку секретного поля.
Чтение возвращает имя credential, маску и наличие. Ответы различают сохранённый кандидат,
эффективное значение, необходимость reopen/restart и предупреждение загрузки store.

| Семейство | Полный существующий объём | Источник истины и запись |
| --- | --- | --- |
| `app` | Appearance, overlay primary/secondary/bitmap layout, Sidebar/search, player UI/mouse, theme/topmost, app/engine hotkeys, word actions/Anki deck, preferences/recent files. | [AppPrefs](../../../LLPlayer.Avalonia/Services/AppPrefs.cs), [SettingsViewModel](../../../LLPlayer.Avalonia/ViewModels/SettingsViewModel.cs) и его partials. Использовать нормализацию и fresh-field merge с persist-before-apply. |
| `playback` | Audio enable/filters/max/default volume/languages, software/hardware Video request, buffer/timeouts/seek, subtitle loading/language/resegment/VAD/word timing/denoise/export policies, startup log/profile. Runtime audio output/delays/viewport имеют отдельную session identity. | [PlaybackSettings](../../../LLPlayer.Avalonia/Services/PlaybackSettings.cs), [контроллер сохранения](../../../LLPlayer.Avalonia/Services/FlyleafPlaybackController.PlaybackSettings.cs). Поле следующего старта и текущая native-поддержка различимы. |
| `recognition` | ASR engine, Whisper/whisper.cpp/faster-whisper options, model/engine paths, prompts/presets, chunking, ordered runtime preferences, per-slot OCR engines/languages/regions. | [RecognitionSettings](../../../LLPlayer.Core/Services/RecognitionSettings.cs), [RecognitionActions](../../../LLPlayer.Core/Services/RecognitionActions.cs). Общие fallback-языки сохраняют Playback-владельца. |
| `translation` | Word/sentence/dictionary providers, target language, provider-specific endpoint/model/credentials/options, chat/context/sampling, concurrency/backward/forward limits, explicit Check/Get Models/Hello. | [TranslationSettings](../../../LLPlayer.Core/Services/TranslationSettings.cs), [TranslationViewModel](../../../LLPlayer.Avalonia/ViewModels/TranslationViewModel.cs) и partials. Load/save выполняют только соответствующий запрос клиента. Per-slot enable относится к session state. |
| `online_video` | Пути yt-dlp/Deno, ExtraArguments, max height, prefer video-with-audio и существующие online-open/playlist параметры. | [OnlineVideoSettings](../../../LLPlayer.Avalonia/Services/OnlineVideoSettings.cs), [OnlineVideoViewModel](../../../LLPlayer.Avalonia/ViewModels/OnlineVideoViewModel.cs). Применять текущие валидаторы аргументов и URL. |
| `pdic` | Явный путь Windows bridge, проверка/поиск пути и lookup текущего слова. | [PdicSettings](../../../LLPlayer.Avalonia/Services/PdicSettings.cs), [PdicSettingsViewModel](../../../LLPlayer.Avalonia/ViewModels/PdicSettingsViewModel.cs). На Linux каталог сохраняет функцию с причиной `unsupported_os`. |
| `batch` | Folder/recursive, overwrite, serialize ASR+translate, CPU-while-active/idle threshold, prefer Russian audio, GenerateDubbing, WatchFolder. | [BatchSettings](../../../LLPlayer.Core/Batch/BatchSettings.cs), [BatchWorkflow](../../../LLPlayer.Core/Batch/BatchWorkflow.cs). Explicit save/start/watch имеют собственные переходы и порядок компенсации совместного save голоса. |
| `dubbing` | Текущие DubbingConfig engine/model/default/custom voice, ducking/atempo/format и opt-in persistence per-line voices. | [DubbingSettings](../../../LLPlayer.Core/Services/DubbingSettings.cs), [AppHost.LanguageWorkflows](../../../LLPlayer.Avalonia/AppHost.LanguageWorkflows.cs). Сохранять merge текущего default voice с Batch и per-line precedence. |

## Обязательные дополнения для сценария ASR → перевод → экспорт

Существующий interactive translation toggle обслуживает воспроизведение и опережающий перевод.
F-20 должен дать явную операцию перевода **всех** реплик документа в выбранный целевой язык,
с прогрессом, отменой, проверкой заполнения и экспортом. Успешное включение toggle не означает
успешного окончания whole-document задания. Batch использует существующий pipeline там, где
его целевой язык и формат соответствуют запросу. Несовпадение требует общей реализации перевода
документа с явным языком в рамках F-20.

При переводе собственной моделью harness получает detached документ с media/document revision,
ID реплик, исходным текстом, языком и таймингами. Возврат задаёт только переведённый текст и целевой
язык для этих ID. Применение проверяет полный адресуемый набор и ревизию, а затем публикует результат
атомарно. Тайминги, исходный текст, стили, bitmap-владение, voice и SpeakerId сохраняются.
Устаревший, неполный, дублированный или чужой набор возвращает конкретную ошибку и возможность
прочитать свежий документ. Порционные ответы клиента также проходят контроль ревизии и явного
состава выбранного диапазона. Partial-result статус отличим от завершённого документа.

## Доказательства полноты при реализации

- Сверить меню, palette, app/engine action descriptors, восемь settings owners и все строки этой матрицы.
  Каждая существующая функция получает реализованную операцию либо конкретное требование GUI/ОС/ресурса.
  Общие ответы «не поддерживается» на целые группы обработки не закрывают покрытие.
- Для каждой операции проверить типы/границы входов, read/write-классификацию, observable result,
  currentness, отмену и повтор. Catalog/schema/read не запускают тяжёлые движки и внешние процессы.
- Одно изменение через API видно в соответствующем GUI-владельце и наоборот. Headless-путь использует
  те же настройки, сервисы и GPU/resource admission. Глобальные native-объекты одного процесса не делятся
  между независимыми engine instances без проверки существующего контракта.
- Проверить положительные и отрицательные Windows/Linux случаи в обоих транспортах. Для ключевого
  сценария доказать весь файл → ASR → все реплики → выбранный язык → экспорт и внешнюю модель roundtrip.
- Разделять выполненные тестовые движки, контрактные/headless проверки и отдельно подтверждённые
  реальные engines/providers/модели. Критерии поставки и нативной приёмки задаёт основной план F-20.

Матрица описывает F-20 одной крупной задачей. Последующие F-16, F-19 tier 3, F-02-full и финальная
приёмка F-13 сохраняют согласованный владельцем порядок после неё.
