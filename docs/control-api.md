# Управление LLPlayer Avalonia через MCP и локальный API

Установите пакет Avalonia для своей ОС, .NET 10 и ASP.NET Core 10 Runtime x64. На Windows действуют и штатные требования Avalonia, в том числе VC++ Redistributable. Используйте пути в распакованном каталоге.

## MCP по stdio

Укажите в настройках MCP клиента абсолютный путь к `mcp/LLPlayer.Mcp.exe` на Windows или `mcp/LLPlayer.Mcp` на Linux. Для клиента с TOML используйте такую конфигурацию.

```toml
[mcp_servers.llplayer]
command = "/absolute/path/to/LLPlayer/mcp/LLPlayer.Mcp"
```

Модель и её ключи настраиваются в клиенте. Для полного каталога вызовите `llplayer_capabilities`. Он показывает схемы операций, необходимость окна и ограничения ОС. `llplayer_state` возвращает сессии Avalonia и последние задания. Операции получают свой инструмент с префиксом `llplayer_` и точками имени, заменёнными на подчёркивания. Указывайте `requestId` (UUID) для команд с побочным эффектом. При повторе используйте тот же UUID и те же параметры.

Мост использует C# SDK 2.4.0. Он принимает MCP 2026-07-28 через `server/discover` и прежний handshake `initialize`. LLPlayer предоставляет операции `llplayer_job_get`, `llplayer_job_cancel`, `llplayer_job_retry` и `llplayer_job_resume`. `llplayer_host_stop` и `llplayer_host_restart` управляют уже работающим хостом.

## Русские субтитры рядом с видео из чата

Для фоновой обработки задайте мосту `LLPLAYER_SUBTITLE_ROOT` с абсолютным путём к вашей папке видео. В задаче указывайте полный путь, сохраняя регистр имени разрешённой папки. Мост предоставит инструменты распознавания, перевода, экспорта и проверки связанных заданий. ControlHost сам сформирует имя `<имя видео>.ru.srt` рядом с исходным файлом. Существующая дорожка защищена от перезаписи.

В проекте Codex, которому вы доверяете обработку этой папки, можно заранее разрешить четыре команды, которыми модель клиента запускает распознавание, применяет и фиксирует перевод, затем экспортирует SRT. Пути ниже приведены как пример для Windows. На Linux укажите путь к `LLPlayer.Mcp` и свой абсолютный путь к медиа.

```toml
[mcp_servers.llplayer_subtitles]
command = "C:/Applications/LLPlayer/mcp/LLPlayer.Mcp.exe"
enabled_tools = [
  "llplayer_subtitle_media_probe",
  "llplayer_subtitle_recognition_start",
  "llplayer_subtitle_recognition_omissions_read",
  "llplayer_subtitle_job_get",
  "llplayer_subtitle_translation_document_read",
  "llplayer_subtitle_translation_document_apply",
  "llplayer_subtitle_translation_document_commit",
  "llplayer_subtitle_export_subtitles",
]
default_tools_approval_mode = "writes"

[mcp_servers.llplayer_subtitles.env]
LLPLAYER_SUBTITLE_ROOT = "E:/Lessons"

[mcp_servers.llplayer_subtitles.tools.llplayer_subtitle_recognition_start]
approval_mode = "approve"

[mcp_servers.llplayer_subtitles.tools.llplayer_subtitle_translation_document_apply]
approval_mode = "approve"

[mcp_servers.llplayer_subtitles.tools.llplayer_subtitle_translation_document_commit]
approval_mode = "approve"

[mcp_servers.llplayer_subtitles.tools.llplayer_subtitle_export_subtitles]
approval_mode = "approve"
```

После подключения проекта достаточно задачи «Сделай русские субтитры к `E:/Lessons/урок.mp4` и сохрани рядом с видео через LLPlayer». Обработка проходит в ControlHost без открытия окна. Для перевода провайдером, выбранным в настройках LLPlayer, добавьте `llplayer_subtitle_workflow_create_subtitles` в `enabled_tools` и задайте ей отдельное одобрение. Результат проверяет `llplayer_subtitle_job_get`. Действующая политика клиента определяет, когда нужно дополнительное подтверждение. [Настройки MCP в Codex](https://learn.chatgpt.com/docs/extend/mcp?surface=cli#configtoml-examples)

Плагин LLPlayer для Codex подключает скилл `llplayer-subtitles`. Он описывает работу с черновиком и вычитку технических субтитров с сохранением терминов, клавиш и таймингов. В других клиентах можно подключить тот же файл как инструкцию для перевода. [Скилл llplayer-subtitles](../Plugins/llplayer-codex/skills/llplayer-subtitles/SKILL.md)

Если мост перезапущен между шагами, повторите исходный запрос с тем же `requestId` и теми же аргументами. Хост вернёт прежнее задание, и мост снова сможет читать его состояние.

## Локальный HTTP

Для отдельного профиля Avalonia и MCP задайте в окружении клиентов один абсолютный `LLPLAYER_CONFIG_DIR`. Для состояния задайте абсолютный `XDG_STATE_HOME` на Linux или `LOCALAPPDATA` на Windows. Аргумент хоста `--state-dir` должен указывать на подкаталог `LLPlayer` в выбранном каталоге.

Запустите `LLPlayer.ControlHost.exe --serve` или `./LLPlayer.ControlHost --serve`. Avalonia и MCP также могут запустить хост при первом подключении. Один хост обслуживает конфигурацию текущего пользователя. Для изолированной конфигурации можно указать `--config-dir` и `--state-dir`. Во всех клиентах, которые подключаются к одному хосту, используйте те же каталоги конфигурации и состояния.

После аварийного выхода запустите хост с исходными каталогами конфигурации и состояния. В `Control/control.lock` сохраняется путь к каталогу состояния, чтобы новый процесс мог прочитать журнал и завершить очистку ресурсов. Сменить каталог состояния можно после штатной остановки с подтверждённой очисткой.

Адрес, `instanceId` и `credential` записываются в приватный файл обнаружения. Его путь зависит от ОС.

- Windows — `%LOCALAPPDATA%\LLPlayer\Control\host.json`.
- Linux — `~/.local/state/LLPlayer/Control/host.json`.

Запросы отправляются на указанный в файле loopback-адрес. Передавайте `Authorization: Bearer <credential>` как заголовок и храните его только в локальной конфигурации клиента. При подключении проверьте `GET /v1/identity` по `instanceId`, версии и PID из файла. Браузерные запросы с заголовком `Origin` отклоняются.

- `GET /v1/capabilities` — каталог и схемы для текущего контекста.
- `GET /v1/state` — список подключённых окон, активные и последние задания. Для чтения выбранной сессии используйте `?sessionId=<UUID>`.
- `POST /v1/operations` — вызов с полями `requestId`, `operationId`, `scope`, `expectedRevision` и `arguments`. Обычный `scope` содержит `{"profileId":"default"}`. Для операций без ожидаемой ревизии передайте `null`.
- `GET /v1/jobs` и `GET /v1/jobs/{jobId}` — задания и результаты. Список принимает `cursor` и `limit`.
- `POST /v1/jobs/{jobId}/cancel`, `/retry` и `/resume` — команды с телом `{"jobId":"...","scope":{"profileId":"default"}}`.
- `GET /v1/events` — события заданий. Чтобы продолжить поток, передайте последний `id` в `Last-Event-ID`.
- `POST /v1/stop` — остановка после отмены и подтверждённой очистки. При `cleanup_pending` хост остаётся доступен для проверки заданий.
- `POST /v1/restart` — та же очистка с запуском нового экземпляра. Ответ `202` подтверждает принятие запроса. Сверьте новую квитанцию обнаружения и `instanceId` через `/v1/identity`.

## Окна и команды Avalonia

Выберите `sessionId` открытого окна из `llplayer_state` или `GET /v1/state`. Передайте его в MCP-вызове или в объекте `scope` для `POST /v1/operations`.

- `ui.surface.list` (`llplayer_ui_surface_list`) возвращает список окон и состояние каждого из них. Поля результата — `surface`, `visible` и `available`.
- `ui.surface.open` (`llplayer_ui_surface_open`) принимает `surface` и создаёт задание. Дождитесь `completed` и проверьте в результате `visible`. Повторный вызов для уже видимого окна возвращает `visible: true`. Открытие может быть недоступно, пока в сеансе открыт другой модальный диалог.
- `ui.sidebar.search.focus` показывает боковую панель и ставит фокус в поле поиска субтитров. Результат задания сообщает `sidebarSearchFocused`. Та же операция доступна как команда `app:ActivateSubsSearch`.
- `commands.list` показывает идентификаторы команд и отмечает поддержку автоматизации. `commands.execute` выполняет поддерживаемые команды. Для оконной команды результат может содержать `surface`. Например, `app:ToggleDebugOverlay` открывает или закрывает окно отладки, а `ui.surface.open` с `debugOverlay` открывает его.
- `ui.get` возвращает состояние интерфейса. `ui.set` принимает `fullscreen`, `sidebar`, `alwaysOnTop` и `theme` (`dark` или `light`). Тема и режим поверх окон сохраняются в настройках приложения.

Значения `surface` — `commandPalette`, `debugOverlay`, `cheatSheet`, `about`, `settings`, `playbackSettings`, `pluginOptions`, `playlist`, `wordManager`, `subtitleExport`, `subtitleDownload`, `aiInsights`, `batch`, `pdicSettings`, `onlineVideo`, `translation`, `recognition`, `dubbing`, `audioCapture`, `videoCapture`. Доступность зависит от ОС, состояния выбранного окна и ресурсов. Для открытия основного окна приложения служит `ui.window.open`. Сведения о приложении возвращает `app.about`.

## Субтитры и задания

1. Вызовите `media.probe` с абсолютным путём и выберите `streamIndex` из результата. Затем передайте путь, индекс и `sourceRevision` в `recognition.start`. Хост проверит, что исходный файл соответствует прочитанной ревизии. Выбор движка доступен через настройки распознавания или поле `engine`.
2. Дождитесь завершения ASR. Сохраните `jobId` и `revision` документа из результата. Для перевода настроенным провайдером передайте их в `translation.start` вместе с `targetLanguage`. Параметры `start` и `count` выбирают диапазон реплик с индексами от нуля.
3. Если модель клиента переводит сама, передайте `externalTargetLanguage` при запуске распознавания. Для готового документа вызовите `translation.document.begin` с его ревизией, целевым языком и нужным диапазоном. При статусе `awaitingTranslation` получите порции исходных реплик через `translation.document.read`, отправьте переводы с исходными `cueId` через `translation.document.apply` и опубликуйте их через `translation.document.commit`.
4. Используйте `export.subtitles` с идентификатором задания, ревизией документа, абсолютным путём и форматом. Для полного русского перевода по результатам распознавания с проверенной ревизией можно опустить `path` и передать `format: "srt"`, `text: "translatedOnly"`, `overwrite: false`. Хост возьмёт путь из сохранённого задания и запишет `<имя видео>.ru.srt`. Для нескольких локальных файлов и настроенного провайдера используйте `workflow.create_subtitles`. Чтобы сохранить русские SRT рядом с видео, опустите `outputPath` у соответствующих элементов `files`. Ожидайте завершения задания и сверяйте хеш с результатом операции.

При разрыве соединения работа продолжается в хосте. После сбоя проверьте статус и явно вызовите `resume` у прерванного задания. При несовпадении сохранённого входа или результата операция вернёт ошибку. Для внешнего действия с неопределённым исходом проверьте его на стороне сервиса перед явным подтверждением повтора.

### Предложения о пропущенных фразах

`recognition.start` и `workflow.create_subtitles` включают поиск пропусков для Faster Whisper. Передайте `detectAsrOmissions: false`, чтобы отключить поиск на один запуск. Если параметр отсутствует, поиск включён. В ограниченном MCP это поле передаётся внутри `input` команды распознавания или полного workflow. Общий профиль не меняется.

Результат распознавания возвращает `omissionDetection` с запрошенным режимом `enabled`, фактом выполнения `applied`, состоянием `status` и количеством предложений `totalCount`. У завершившегося workflow сводка относится к конкретному файлу. `unsupported` означает, что движок не поддерживается. `unavailable` означает, что сопоставимые результаты распознавания получить не удалось, например при выключенных таймкодах слов. `partial` означает неполную проверку. Для старых заданий сводка может отсутствовать.

Читайте предложения через `recognition.omissions.read`, в полном MCP через `llplayer_recognition_omissions_read`, в ограниченном через `llplayer_subtitle_recognition_omissions_read`. Передайте `jobId` и при необходимости `offset`, `limit` и `fileIndex` для workflow. Следующую страницу указывает `nextOffset`. `startMs` и `endMs` относятся к шкале исходного медиа. `reviewOnlyReason` отмечает фразы, которые требуют отдельного решения о границах показа. Предложения хранятся отдельно от исходного текста, перевода и экспорта SRT.

[Спецификация MCP](https://modelcontextprotocol.io/docs/2026-07-28/learn/versioning) · [MCP C# SDK 2.4.0](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.4.0)
