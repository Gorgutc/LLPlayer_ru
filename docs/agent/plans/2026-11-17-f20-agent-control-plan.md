# F-20 — управление LLPlayer через MCP и локальный API

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` with explicit spawned owners and independent reviewers. Checkboxes track execution. The eight tasks below implement one F-20 card and one complete delivery.

**Goal:** Внешний harness с локальной или облачной моделью обнаруживает и вызывает все текущие возможности Avalonia Windows/Linux, включая автономную цепочку ASR → перевод → экспорт.

**Architecture:** Один `LLPlayer.ControlHost` владеет заданиями, настройками, моделями и GPU-допуском текущего пользователя. Avalonia подключается к тому же владельцу, отображает снимки и выполняет операции отображения через обратный локальный канал. MCP stdio и локальный HTTP API используют единые типизированные контракты.

**Tech Stack:** C#/.NET 10, переносимый Flyleaf/Core, Avalonia, ASP.NET Core 10 для отдельного companion host, официальный MCP C# SDK, существующий SQLite.

**Spec:** [Дизайн F-20](2026-11-17-f20-agent-control-design.md), [матрица возможностей](2026-11-17-f20-agent-control-capabilities.md), [промпт следующей сессии](2026-11-17-f20-agent-control-next-session-prompt.md).

## Статус и согласованные решения

План подготовлен 2026-11-17. Реализация начинается в следующей сессии. Исходная поставка — PR № 205, `main` `d9ed4337da4168e4d2e697b8f27cbf10d51a2be7`; перед выполнением сверить живой `main`.

- Подключение ограничено этим компьютером. MCP и HTTP API имеют одну поверхность операций.
- Агенту доступен весь функционал, включая сохранение настроек и скачивание моделей. Подтверждение каждого действия не требуется. Существующие проверки занятых ресурсов и явная политика перезаписи действуют для всех клиентов.
- Обработка работает с открытым GUI и без окна. Открытие GUI подключает продолжающееся задание без повторного inference.
- Перевод выполняется существующим провайдером плеера либо текущей моделью внешнего harness, которая возвращает переведённые реплики.
- Отсоединение клиента сохраняет работу. После сбоя host активные задания получают `Interrupted`; продолжение запускается явной командой.
- После F-20 следуют оставшиеся F-16, F-19 этап 3 и F-02-full, затем финальная приёмка F-13. Новые карточки не создаются.

## Глобальные требования

- Windows и Linux используют один исходник API/Core/Avalonia. WPF проходит свой полный гейт.
- Один текущий пользователь имеет один ControlHost. Host закреплён за активным профилем и физическим config-путём; конфликт другого профиля возвращает `profile_conflict`.
- Локальная авторизация выполняется при подключении. Loopback endpoint, файл обнаружения и токен принадлежат текущему пользователю. Токен и сохранённые секреты исключены из обычных ответов и журналов.
- `LLPlayer.Control` — лёгкая библиотека `net10.0` с DTO, контрактами и HttpClient-клиентом. Зависимостей Core/Flyleaf/Avalonia в ней нет. `LLPlayer.Mcp` зависит от неё; `ControlHost` — от неё и Core.
- `LLPlayer.ControlHost` использует `Microsoft.NET.Sdk.Web`, `net10.0` и ASP.NET Core 10 runtime. Это отдельно описанная зависимость companion. Обычная поставка WPF сохраняет свою runtime-границу.
- SDK MCP предварительно выбран версии 2.4.0 по исследованию подготовки. Перед добавлением pin проверить официальные API/версию и записать проверенное решение; обновление остальных зависимостей не требуется.
- Для журнала использовать уже закреплённые `Microsoft.Data.Sqlite` 9.0.17 и `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3. Модели, окружения, конфигурация, токены, задания и результаты хранятся вне immutable payload.
- GUI сохраняет native playback/render/dispatcher. Интерактивный ASR, OCR, диаризация, TTS и background processing исполняются через host; GUI не создаёт второго независимого владельца GPU или файлов моделей.
- Ожидание ответа внешней LLM освобождает собственный GPU-допуск LLPlayer после подтверждённого завершения его работы. Общая координация внешних LLM-серверов находится вне гарантии процессного допуска.
- Полная native-приёмка F-13, Computer Use, физический GPU и реальная модель требуют отдельного согласования. Протокольная и harness-проверка F-20 входит в реализацию.

## Внимание при review

1. Повтор запроса после разрыва соединения возвращает прежнее задание; разные параметры с тем же RequestId дают конфликт.
2. Смена медиа, потока или задержек во время host inference отклоняет устаревшую публикацию и сохраняет новые реплики.
3. Crash между публикацией файла и фиксацией результата сохраняет доказуемую границу эффекта; Resume не перезаписывает готовый результат молча.
4. Подключение GUI к background-заданию сохраняет единственный процесс inference и те же job/session IDs.
5. Ожидание внешнего перевода и ошибочный ответ не блокируют GPU и не повреждают исходные тайминги, стили или source revision.

## Файлы и интерфейсы

| Размещение | Ответственность |
| --- | --- |
| `LLPlayer.Control/Contracts/ControlContracts.cs`, `IPlayerControlService.cs`, `IControlJobService.cs` | Версионированные wire-контракты, дескрипторы возможностей и методы управления |
| `LLPlayer.Control/Client/ControlClient.cs` | Типизированный HTTP-клиент и подписка на события |
| `LLPlayer.Core/Automation/` | Каталог операций, оркестрация, сессии, хранилище и адаптеры текущих сервисов |
| `LLPlayer.ControlHost/` | Безоконный composition root, текущий пользователь, жизненный цикл, авторизация, HTTP и обратный канал |
| `LLPlayer.Mcp/` | Официальный MCP stdio server и преобразование к тем же операциям |
| `LLPlayer.Avalonia/Services/Automation/`, `AppHost*.cs` | Подключение к host, команды GUI и проверяемая публикация снимков |
| `FlyleafLibTests/Automation/`, `LLPlayer.Avalonia.Tests/Automation/` | Контракты, реальные companion-процессы, lifecycle и headless GUI-интеграция |
| `scripts/windows/`, `scripts/linux/`, существующие CI/валидаторы | Сборка, исполнение новых проектов, состав пакетов и регрессии |

Общие имена фиксированы дизайном; тела методов определяет реализация.

```csharp
ControlScope(ProfileId, SessionId?)
ControlRequest(RequestId, OperationId, Scope, ExpectedRevision, JsonElement Arguments)
ControlResult(JobId?, Revision?, JsonElement? Value, ControlError?)

IPlayerControlService.DescribeAsync(ControlScope, CancellationToken) -> Task<ControlCapabilities>
IPlayerControlService.GetStateAsync(ControlScope, CancellationToken) -> Task<ControlState>
IPlayerControlService.InvokeAsync(ControlRequest, CancellationToken) -> Task<ControlResult>
IControlJobService.GetAsync / ListAsync / CancelAsync / RetryAsync / ResumeAsync
```

`RequestId` — UUID и единственный ключ идемпотентности. Хранилище уникально по `(ProfileId, RequestId)` и сравнивает нормализованный fingerprint операции/аргументов/режима. Точный DTO каждого метода задания записать в `IControlJobService.cs` до параллельной реализации адаптеров.

Состояния задания: `Accepted`, `WaitingForResource`, `Running`, `AwaitingTranslation`, `Cancelling`, `AwaitingCleanup`, `Completed`, `Failed`, `Cancelled`, `Interrupted`.

### Task 1 — Контракты и полный каталог возможностей

**Files** — создать `LLPlayer.Control/LLPlayer.Control.csproj`, `Contracts/*`; создать `LLPlayer.Core/Automation/ControlOperationCatalog.cs`, `FlyleafLibTests/Automation/ControlContractTests.cs`; изменить `LLPlayer.slnx` и ссылки проектов.

**Interfaces** — публикует перечисленные DTO/сервисы и `ControlCapabilities` с параметрами, состоянием доступности, причиной недоступности и режимами выполнения каждой операции из матрицы.

- [ ] Зафиксировать DTO без native objects, делегатов, произвольных `Action` и reflection-доступа. У идентификатора медиа/дорожки/реплики есть scope и revision.
- [ ] Написать `Catalog_covers_every_capability_matrix_row`, `Request_roundtrip_preserves_scope_and_revision`, `Diagnostics_redacts_credentials`, `Unknown_operation_and_invalid_arguments_return_structured_error`.
- [ ] Получить содержательный RED на неполном каталоге/некорректной десериализации, затем реализовать каталог и получить GREEN. Сохранить команды, SHA и оба журнала.
- [ ] `dotnet build LLPlayer.Control -warnaserror`; `dotnet test FlyleafLibTests -warnaserror --filter FullyQualifiedName~ControlContractTests`. На Windows дополнительно portable `-f net10.0 -p:LLPlayerPortableTests=true` после соответствующего restore.
- [ ] Проверить границу зависимостей и скоммитить точный список исходников/тестов отдельно от новых tooling-правок.

### Task 2 — Владелец процесса, durable jobs и восстановление

**Files** — создать `LLPlayer.ControlHost/{LLPlayer.ControlHost.csproj,Program.cs,ControlHostLifetime.cs,ControlHostDiscovery.cs}`; создать `LLPlayer.Core/Automation/{ControlJobService.cs,ControlJobStore.cs,ControlSessionService.cs}` и `FlyleafLibTests/Automation/ControlJobLifecycleTests.cs`.

**Interfaces** — реализует `IControlJobService`; SQLite `<StateDir>/Automation/jobs.sqlite` хранит schema version, request fingerprint, job stages, checkpoints и ссылки на outputs. Запуск — `LLPlayer.ControlHost --serve` либо согласованное connect-on-demand клиента.

- [ ] RED `Same_request_reconnects_to_one_job`, `Same_request_with_changed_arguments_conflicts`, `Only_one_host_owns_user_profile`, `Another_profile_returns_profile_conflict`.
- [ ] RED `Disconnected_client_keeps_job_running`, `Restart_marks_active_jobs_interrupted`, `Resume_requires_explicit_request`, `Crash_after_file_publish_preserves_completed_effect`, `Ambiguous_Anki_effect_requires_explicit_retry_choice`.
- [ ] Реализовать транзакции, versioned schema migration и durable checkpoint перед необратимым внешним эффектом. Resume сверяет source/config/output fingerprints и опубликованные этапы. Секретные аргументы/снимки настроек не попадают в журнал; защищённый fingerprint позволяет различать повторы, восстановление не переисполняет команду изменения секрета.
- [ ] RED `Headless_host_starts_without_display_or_audio_device`; реализовать служебный последовательный dispatcher и bootstrap host. Процессный тест удаляет `DISPLAY`/`WAYLAND_DISPLAY` и проверяет готовность реального API без инициализации desktop/audio devices.
- [ ] Cancel сообщает завершение после остановки работ и process drain; неопределённая остановка даёт `AwaitingCleanup`. Shutdown прекращает admission и сохраняет журнал перед выходом.
- [ ] RED `Restart_does_not_admit_gpu_until_previous_owned_engine_tree_is_gone` с реальным тестовым деревом дочерних процессов. Журнал сохраняет process identity, start token и group/job ownership. Новый host подтверждает остановку прежнего принадлежащего job дерева; отсутствие старого host этого не доказывает. Неизвестный исход сохраняет `AwaitingCleanup` и блокирует новые GPU-задания и удаление занятых моделей, включая Linux-потомков после сбоя host. Диагностика и API остаются доступны.
- [ ] Собрать host и прогнать `ControlJobLifecycleTests` с дочерним процессом, принудительным завершением именно тестового host и повторным открытием его отдельной SQLite. Получить GREEN и scoped review.

### Task 3 — Общие ASR/перевод/batch/model/GPU-сервисы

**Files** — создать `LLPlayer.Core/Automation/{ControlWorkflows.cs,ControlResourceOwner.cs,ControlSettingsService.cs}`; адаптировать `RecognitionActions`, `BatchWorkflow`/`BatchRunFactory`, `LanguageResourceCoordinator`, `DiarizationWorkflow` и существующие settings stores по фактической необходимости; создать `FlyleafLibTests/Automation/ControlWorkflowTests.cs`.

**Interfaces** — `IPlayerControlService.InvokeAsync` маршрутизируется к существующему владельцу функции. `ControlHost` создаёт один `GpuWorkCoordinator` и один resource coordinator для GUI и MCP/API jobs.

- [ ] RED `Asr_job_completes_after_actual_engine_completion`, `Interactive_and_batch_share_one_gpu_owner`, `Model_delete_waits_for_active_reader`, `Cancellation_keeps_lease_until_process_exit`.
- [ ] Извлечь минимальный общий composition из `AppHost` и controller. Settings commits используют прежние validate/fresh merge/persist-before-apply, а все клиенты отправляют изменения host.
- [ ] Переиспользовать batch translator с сохранением текущего режима русского выхода. Полный перевод выбранного slot выполняет явное задание с нужным целевым языком; playhead-window перевод остаётся отдельной операцией.
- [ ] RED `Batch_target_retains_existing_russian_policy`, `Full_slot_translation_respects_requested_target`, `Waiting_for_external_translation_releases_gpu`; тесты используют управляемые ASR/TTS/LLM doubles и реальные lease/disposal paths.
- [ ] Получить GREEN и подтвердить, что WPF-клиенты прежних сигнатур и обычные UI-пути продолжают использовать поддерживаемые правила.

### Task 4 — Живой GUI и присоединение к фоновой обработке

**Files** — создать `LLPlayer.Control/Client/ControlClient.cs`, `LLPlayer.Avalonia/Services/Automation/{ControlConnection.cs,ControlCueProjection.cs,ControlGuiChannel.cs}`; изменить `AppHost*.cs`, затронутые controller/VM; создать `LLPlayer.Avalonia.Tests/Automation/ControlGuiIntegrationTests.cs`.

**Interfaces** — GUI регистрирует session и обработчики операций отображения/воспроизведения. Host отдаёт immutable cue/state snapshots и события с job/session/source revision. Обратный канал использует тот же типизированный каталог.

- [ ] RED `Gui_attaches_to_running_job_without_new_inference`, `Gui_close_preserves_headless_job`, `Native_playback_operations_execute_on_ui_dispatcher`.
- [ ] Убрать конкурирующие запуски ML и запись общих настроек из GUI composition. Локальное native playback/render остаётся у GUI, вход интерактивного ASR, выбранный stream и проекции передаются host с актуальными revisions.
- [ ] Для reopenable источника host выбирает тот же audio stream. Для уже поддерживаемого конечного non-reopenable источника GUI передаёт ограниченный PCM feed с sequence, sample clock, source revision, backpressure и отменой. Host сохраняет принадлежащий job входной снимок; после окончания intake обработка независима от GUI. Inference и GPU lease остаются у host.
- [ ] RED `Finite_pcm_feed_preserves_order_clock_and_cancellation`, `Feed_backpressure_is_bounded`, `Replaced_feed_rejects_old_samples`, `Source_close_during_intake_reports_interrupted`, `Live_asr_keeps_existing_rejection`. Закрытие source/GUI до завершения intake даёт явный `Interrupted`/`source_unavailable`; существующий отказ ASR для `IsLive` сохраняется.
- [ ] RED `Changed_audio_or_subtitle_source_rejects_late_host_cues`, `Subtitle_and_audio_delays_remain_correct`, `Two_clients_observe_same_job_and_one_config_writer`.
- [ ] Отделить lifetime job от lifetime GUI/MCP-соединения. Явное завершение job/host реализовать через согласованные команды; состояние UI-only операции без GUI возвращает честную availability.
- [ ] Получить GREEN на настоящем host-процессе и headless Avalonia с детерминированным engine. Проверить оба subtitle slot, замену одинакового URL и переподключение событий.

### Task 5 — Все операции матрицы и внешний перевод

**Files** — создать `LLPlayer.Core/Automation/Operations/` по функциональным владельцам и `FlyleafLibTests/Automation/ControlCapabilityTests.cs`; изменить существующие команды экспорта, словаря/Anki, настроек и OS-адаптеров в их местах владения; дополнить headless integration tests.

**Interfaces** — каждая строка матрицы получает descriptor, читаемое состояние и исполняемую типизированную операцию. Внешний перевод использует `AwaitingTranslation`, revision-bound cue snapshot и явную отправку ответов по cue IDs.

- [ ] RED `Every_available_capability_has_executable_handler`, `Full_agent_access_has_no_per_action_confirmation`, `Unsupported_platform_reports_actual_reason`.
- [ ] Покрыть настройки, media/playlist/stream/chapter, subtitles/ASR/OCR, models, batch/watch, diarization, текущий dubbing, exports, word/dictionary/Anki, command/debug/session/display и остальные строки матрицы.
- [ ] RED `Harness_translation_accepts_exact_cue_revision`, `Duplicate_or_missing_cue_ids_fail_atomically`, `Malformed_translation_preserves_source_text_styles_and_times`, `Partial_response_can_resume_explicitly`.
- [ ] Внешний перевод не сохраняет незапрошенное изменение провайдера. Политики длины/chunking/paging и язык задаются схемой, исходный текст передаётся как данные.
- [ ] RED `Export_preserves_existing_file_without_overwrite_flag`, `Explicit_overwrite_uses_atomic_writer`, `Batch_and_manual_outputs_are_equivalent`; переиспользовать существующий serializer и файловые владельцы.
- [ ] Получить GREEN. Матрицу обновить конкретными operation IDs, source paths и доказательствами исполнения, включая недоступность операции без присоединённого GUI.

### Task 6 — Локальный HTTP и MCP stdio

**Files** — создать `LLPlayer.ControlHost/{ControlApi.cs,ControlAuthorization.cs,ControlGuiEndpoints.cs}`, `LLPlayer.Mcp/{LLPlayer.Mcp.csproj,Program.cs,PlayerControlTools.cs}`; создать `FlyleafLibTests/Automation/{ControlProtocolTests.cs,McpProtocolTests.cs}`.

**Interfaces** — HTTP сериализует единые сервисы; MCP discovery/call/resource/progress обращается к тому же host через `ControlClient`. JSON stdout bridge принадлежит протоколу, диагностика направляется в stderr.

- [ ] RED `Local_http_and_mcp_invoke_same_operation`, `Mcp_initialize_lists_schemas_and_calls_real_host`, `Mcp_discover_and_tools_call_negotiate_supported_version`, `Bridge_exit_keeps_host_job`, `Protocol_cancellation_does_not_implicitly_cancel_durable_job`.
- [ ] Проверить negotiation и реальный tools call для современной версии протокола `2026-07-28` и совместимой `2025-11-25`. Клиент заявляет только реализованные capabilities; фактическую поддержку SDK/клиента подтвердить на каждой версии, сохранив протокольный transcript.
- [ ] Проверить официальные SDK API и pin перед restore. Подключение допускается с токеном текущего пользователя, endpoint bind только loopback; discovery не запускает inference/скачивание модели.
- [ ] RED `Unauthorized_connection_is_rejected`, `Remote_bind_is_rejected`, `Session_handle_cannot_mutate_another_session`, `Diagnostics_does_not_expose_tokens_or_provider_keys`.
- [ ] Проверить bounded bodies, paging транскрипта, структурированные ошибки, версию схем и reconnect события. Сервисные токены не помещать в аргументы процесса или tracked harness JSON.
- [ ] Прогнать реальные дочерние HTTP/MCP-процессы официальным клиентом на Windows/Linux. После GREEN выполнить независимое protocol/runtime review.

### Task 7 — Сборка, пакеты и полный регресс

**Files** — изменить `LLPlayer.slnx`, соответствующие `*.csproj`, `scripts/windows/{verify-avalonia,publish-avalonia,validate-avalonia-package,test-package-avalonia,test-desktop-test-build,desktop-build-retention,test-desktop-build-retention}.ps1`, `scripts/linux/{verify,publish}.sh` и тесты скриптов; актуализировать существующие workflow validators вместе с изменением CI-контракта при необходимости.

**Interfaces** — оба Avalonia-пакета содержат фактически запускаемые ControlHost/Mcp и их app-local runtime closure. Независимый MCP bridge использует лёгкий `LLPlayer.Control`, host — portable Core/Flyleaf.

- [ ] RED package fixtures для отсутствующих companion/DTO/deps/runtimeconfig, неверного TFM/RID и случайного включения SQLite jobs, токенов, конфигов, моделей, логов или outputs. Добавить зависимости companion build к process fixtures существующих suites.
- [ ] RED `Keep2_preserves_active_control_host_and_mcp_across_third_refresh` для apphost и `dotnet LLPlayer.ControlHost.dll`/`dotnet LLPlayer.Mcp.dll`. Сканирование hosted-процессов распознаёт все companion assemblies и сохраняет их payload/sourceArchive/packageSource dependencies при двух и более обновлениях GUI; остальные правила Keep2 и защиты неизвестных данных действуют полностью.
- [ ] Все новые проекты явно собираются и исполняются в canonical Windows/Linux gates. Отсутствие companion runtime/binary в требуемом протокольном тесте даёт failure с причиной; подтверждённые model/platform skips сохраняют свой scope.
- [ ] Проверить runtime/dependency notices и запуск из staged пакета и извлечённого архива. Описать отдельный ASP.NET Core runtime и условия startup без окна.
- [ ] Выполнить `scripts/codex/verify-fast.ps1`, полный `scripts/codex/verify.ps1`, полный `scripts/windows/verify-avalonia.ps1`, package tests и publish. На Ubuntu выполнить fetch FFmpeg, полный `scripts/linux/verify.sh` и publish.
- [ ] Обязательные команды новых проектов — `dotnet build LLPlayer.Control -warnaserror`, `dotnet build LLPlayer.ControlHost -warnaserror`, `dotnet build LLPlayer.Mcp -warnaserror`. Portable suite — `dotnet test FlyleafLibTests -f net10.0 -p:LLPlayerPortableTests=true -warnaserror`; GUI suite — `dotnet test LLPlayer.Avalonia.Tests -warnaserror`.
- [ ] Выполнить полный unfiltered suite после интеграционных исправлений; сохранить фактические результаты и причины каждого непройденного runtime gate. Гейты без новых правок/сбоев повторяются только по необходимости доставки.

### Task 8 — Реальный клиент, review и единая доставка

**Files** — обновить F-20 в `docs/agent/backlog.md`, связанные frozen contracts, setup/usage docs, capability evidence и scoped Second Brain. Квитанции и machine-specific harness config хранятся в ignored artifacts.

- [ ] Реальным MCP/API-клиентом обнаружить возможности, открыть fixture, выбрать аудиодорожку, выполнить ASR с детерминированным engine, перевести, экспортировать, прочитать result и повторно подключиться к тому же job.
- [ ] Через доступный настоящий harness, например Codex или DSH, выполнить естественно сформулированное поручение и сохранить calls/results. Запуск внешней модели должен быть реальным; установленный MCP, список tools и написанный config не доказывают harness-выполнение.
- [ ] Проверить оба пути перевода, background→GUI attach, disconnect, cancel/drain, crash→Interrupted→explicit Resume и отсутствие дублированных outputs. Реальную модель ASR/GPU и полную native F-13 приёмку отметить отдельно по действующему решению владельца.
- [ ] Провести независимый whole-branch `/review` с reviewers из repo matrix, закрыть Critical/Important и выполнить относящиеся к исправлениям проверки. Сопоставить каждый DoD дизайна с фактическим receipt.
- [ ] Доставить согласованный PR, дождаться WPF/Windows Avalonia/Linux CI, сверить reviewed tree/head перед merge и отдельный CI на итоговом main.
- [ ] На чистом итоговом Windows checkout выполнить default fresh `scripts/windows/update-desktop-test-build.ps1`; проверить sourceCommit, EXE target без аргументов, payload cwd, blue icon, receipts/пакет и Keep2. Использовать Windows PowerShell 5.1 для текущих updater/retention scripts.
- [ ] Обновить только scope LLPlayer_ru в Second Brain, выполнить `session-close`, commit и независимое readback. Зафиксировать итоговые SHAs, known limitations, PASS/NOT_RUN и следующий существующий F-16; дальнейшую реализацию начинать по очереди владельца.

## Порядок выполнения и владение

Задачи 1–4 последовательно закрепляют контракты и единственного владельца исполнения. Адаптеры матрицы в задаче 5 распределяются между spawned agents по непересекающимся функциональным файлам. HTTP/MCP в задаче 6 использует уже проверенные сервисы. Задачи 7–8 принимают весь F-20 как одну поставку.

Каждый worker получает этот план, дизайн, матрицу, точные пути владения и напоминание о совместной работе. Root координирует изменения общих сигнатур и единолично запускает сборки, изменяющие общие `obj/bin`. Коммиты app/runtime и agent/tooling разделяются; независимый final review обязателен.

Содержательный результат следующей сессии — исполняемое управление всем плеером из внешнего harness с долговечными заданиями и проверенным GUI/background переходом. Если обязательный реальный harness или runtime недоступен, зафиксировать конкретный блокер и фактически выполненные проверки, продолжить остальные разрешённые части и завершить доступную поставку без вымышленной приёмки.
