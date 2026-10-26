# Промпт для новой сессии F-13: настройки боковой панели

Продолжи работу над LLPlayer с чистым контекстом. Сначала сверь фактическое состояние, затем выполни только один
одобренный владельцем блок. Этот документ передаёт контекст и предложение; сам по себе он не разрешает реализацию
следующего блока.

> **Обновление 2026-10-26:** описанный ниже блок Subtitles Sidebar одобрен владельцем и реализован; этот документ остаётся историческим планом того блока. Актуальное состояние — [F-13 status](f13-status-2026-10-18.md) и `backlog.md`; новый промт следующей сессии перегенерируется после merge. Не используй раздел «Следующий блок» ниже как разрешение реализации.

## Репозиторий, источники и правила

- Репозиторий: `C:\Users\Maxim\Documents\GitHub\LLPlayer_ru`.
- Second Brain: `C:\Users\Maxim\Documents\GitHub\Second-brain`.
- LLPlayer — C#/.NET 10 desktop-приложение. Поддерживаемый Windows-продукт — WPF (`LLPlayer/`);
  F-13 — один общий Avalonia UI для Windows/Linux (`LLPlayer.Avalonia/`) и общий `LLPlayer.Core/`.
  Полная совместимость функций и нативная desktop-приёмка ещё не завершены. WPF остаётся поддерживаемым.
- Канонический backlog: `docs/agent/backlog.md`. Текущий статус и раздельные исторические доказательства:
  `docs/agent/f13-status-2026-10-18.md`.

`FlyleafLib/` — FFmpeg/media engine с WPF Windows и portable .NET 10 targets;
`FlyleafLibTests/` — engine tests. `WpfColorFontDialog/` — WPF-зависимость; `Plugins/YoutubeDL/` —
отдельный .NET plugin для yt-dlp. Tracked runtime assets находятся в `FFmpeg/`, `LLPlayer/lib/7z.dll`
и `LLPlayer/Assets/silero_vad.onnx`. Publish profiles и GitHub workflows описывают отдельные WPF,
Windows Avalonia и Linux build/package flows; не меняй их для настройки Sidebar.

Перед работой прочитай vault `AGENTS.md`, `1-Projects/LLPlayer_ru/_INDEX.md`, frozen `Conventions.md`
и единственную сессию по указателю «▶ Продолжить здесь». Затем прочитай repo `AGENTS.md`,
`docs/agent/subagent-review-matrix.md` и подходящие локальные skills. Подключённые плагины `@github`
и `@superpowers` обязательны, когда доступны; загрузи соответствующие skills и используй GitHub plugin
для сверки PR/default branch/CI. Если инструмент недоступен, сообщи об этом и не имитируй его вызов.

До изменения поведения прочитай frozen contracts: `product-behavior-contract.md`, `wpf-design-contract.md`,
`media-runtime-contract.md`, `config-data-contract.md`, `dependency-baseline.md`, `manual-smoke-matrix.md`
в `docs/agent/`. Следуй порядку авторитета из repo `AGENTS.md`. Не меняй frozen-решения вне явно
одобренного блока.

Используй доступные агентские слоты для ограниченных независимых задач через явный `spawn_agent`.
Роли и обязательное покрытие путей определяет review matrix. У каждого исполнителя должна быть своя
область файлов; предупреди его, что другие агенты работают параллельно и их изменения нельзя откатывать.
Перед сдачей обязателен независимый `/review`: отдельный review subagent, минимум `verification_reviewer`,
плюс все профильные роли по матрице. Закрой Critical/Important и проверь финальные изменения повторно.

## Что уже сделано: локальный корректирующий блок хоткеев

Последний подтверждённый merge базового этапа — app-shortcuts, часть 1, PR № 180,
main `e5b1d05baff0aaf5acb197bb9403d8387fd97f7d`, версия `0.3.62`.
Перед исправлениями чистый локальный `main` безопасно обновлён fast-forward с `d646f19` до `e5b1d05`.
Корректирующая ветка: `codex/f13-hotkey-repairs`.
Локальный product commit: `c45fd9583b691c41842d8f8c5a1f36572bf080cb`, база — `e5b1d05`.
Документация и этот промпт фиксируются отдельно от product commit.
Корректировка опубликована в PR [№ 181](https://github.com/Gorgutc/LLPlayer_ru/pull/181).
Подтверждённая premerge-контрольная точка: head `56117ee774d97f8014926e684e7c860978fa7f4a`.
Состояние merge, последующего head и main проверяй заново перед продолжением.

Владелец одобрил именно три исправления и согласование записей о проверках/продолжении:

1. `ChordCaptureButton` отклоняет Meta/Win/Super и неизвестные биты модификаторов, сохраняя прежний draft chord
   и активный захват. Исключение: Escape всегда отменяет захват, в том числе с неподдерживаемыми модификаторами.
2. `AppShortcutMap` принимает одиночное определённое имя клавиши или поддерживаемый alias; числовые,
   составленные через запятую и неопределённые значения отклоняются. Невалидный override при загрузке
   возвращает стандартные сочетания действия.
3. Уже открытая шпаргалка обновляется после успешного Save переназначений либо Reset с последующим Save,
   сохраняя активный фильтр. Повторное открытие окна для обновления не нужно.

Изменённые product-файлы: `LLPlayer.Avalonia/Controls/ChordCaptureButton.cs`,
`LLPlayer.Avalonia/Services/AppShortcuts.cs`, `LLPlayer.Avalonia/Views/MainWindow.axaml.cs`;
регрессии: `LLPlayer.Avalonia.Tests/HotkeyValidationRepairTests.cs`
и `LLPlayer.Avalonia.Tests/CheatSheetHotkeyRefreshTests.cs`.
Новых preferences, engine bindings, зависимостей или изменений инфраструктуры этот блок не вводит.

Наблюдавшиеся локальные результаты текущих исправлений:

| Проверка | Результат |
| --- | --- |
| Компилируемый intentional RED до production-исправлений | 66 total: 29 failed, 37 passed, 0 skipped; actual exit 1 |
| Тот же focused regression после исправлений | 66 passed, 0 failed, 0 skipped; actual exit 0 |
| Полный WPF gate | 1 440 passed, 0 failed, 0 skipped |
| Полный Windows Avalonia gate | engine: 1 622 passed, 0 failed, 13 skipped; UI: 901 passed, 0 failed, 1 skipped |
| Windows package-contract tests | 53 fixtures PASS; ZIP readback: 38 files PASS |
| Локальный Windows Avalonia publish/content | PASS: 479 files; ZIP 180 445 676 bytes; SHA-256 `d80c923f220d7dfdb5768a836968d4c9e7a0985634d1ca5eb461eca3bf0cc8a5` |

Это фактические результаты одного этапа, а не фиксированные ожидаемые totals для следующего запуска.
Полные suites должны оставаться без фильтрации. Не переноси CI или package hashes PR № 180 на исправления.

### Подтверждённая premerge-контрольная точка PR № 181

Эти результаты относятся именно к head `56117ee774d97f8014926e684e7c860978fa7f4a`.
Последующая правка документации или новый head требуют собственной сверки, а результаты main после merge
фиксируются отдельно.

| Состояние | Подтверждённый результат |
| --- | --- |
| Публикация корректировки | PR № 181 создан; head `56117ee774d97f8014926e684e7c860978fa7f4a` |
| Windows CI | run `40193094026`, attempt 1, SUCCESS; WPF: 1 440 passed / 0 skipped; engine: 1 622 passed / 13 skipped; UI: 901 passed / 1 skipped; publish и upload завершены |
| Linux CI | run `40193094196`, attempt 1, SUCCESS; полный verify: engine 1 621 passed / 14 skipped; UI 900 passed / 2 skipped; publish и upload завершены |
| Product reviews | Профильные независимые reviews: 0 Critical / 0 Important на проверенных входных файлах |
| Review документации и промпта | Первичный независимый checkpoint: 0 Critical / 0 Important; minor по исторической точности исправлен. Финальный review этого последующего delta ещё нужен |
| Vault prepublication session-close gate 2 | PASS: 107 unit tests, 0 hard, 0 broken links, 14 несвязанных debt; baseline 763/763, migrations 38/38, state 1/1 |
| Предыдущая попытка vault gate | Gate 1: FAIL из-за одной JSON wikilink; исправлено. Important независимого review устранён |
| Vault публикация и финальный commit | Vault не push; итоговый scoped commit на этой контрольной точке не подтверждён |

Перед работой прочитай фактические PR № 181 head/state, default branch/main и live vault handoff по указателю
из `_INDEX.md`. Итоговый handoff текущей корректировки может содержать более поздние факты и отдельную
exact-main проверку. Не выводи merge, CI нового head, post-merge CI или финальный vault commit из этой
premerge-таблицы. Новая сессия должна начинаться с проверенной базы, содержащей исправления;
старый `e5b1d05` сам их не содержит. Если обязательный closeout предыдущего блока ещё открыт,
сначала заверши его в уже разрешённых границах и зафиксируй результат.

## Уточнение исторических доказательств DSH

Аудит исходной сессии: ignored `artifacts/dsh-session-audit-2026-10-25/AUDIT.md`.
Историческое заявление «финальный delta PASS у всех пяти» было завышено: реальные независимые reviewers
были, но финальный product `24ec18f` проверили runtime/docs/verification — raw seq 7295/7297/7299.
Последние XAML PASS 7135 и .NET PASS 7172 относятся к более ранним checkpoints; до финального дерева
менялись нормализация shortcuts, тесты и XAML HelpText. Эти два PASS не подтверждают финальное дерево.

Независимый vault draft review вернул FAIL: запрос 7397, ответ 7409. В журнале нет последующего
независимого PASS этого draft. Позднейшее прямое подтверждение исправлений учитывается отдельно.
Успешный draft session-close gate — другой факт; он не заменяет независимый review.
Старые sessions сохраняют исходный текст с датированным erratum, live-записи содержат актуальную правду.
Исторические HC-22/HC-27b waivers и неизвестные причины старых инцидентов не переопределяй.

## Следующий блок: предложение настроек Sidebar

**PROPOSED / IMPLEMENTATION NOT APPROVED текущим запросом на исправление хоткеев.**
Если новое человеческое сообщение уже явно одобряет этот блок и его defaults, выполняй его без повторного
запроса разрешения. Если такого GO нет, сначала представь ограниченный план и решение по defaults,
затем дождись одобрения до product-изменений. Не считай запись «следующий этап» разрешением реализации.

Предложение: перенести оставшиеся настройки страницы WPF **Subtitles Sidebar** в существующий Settings hub
общего Windows/Linux Avalonia UI. Источник: `LLPlayer/Controls/Settings/SettingsSubtitles.xaml:810–884`;
defaults: `LLPlayer/Services/AppConfig.cs:182–228`. WPF не меняется.

Семь WPF UI-строк содержат девять значений. Show Sidebar и Sidebar Width уже представлены
`AppPrefs.SidebarVisible` и `SidebarWidth` (220–900 DIP). Они сохраняются, не дублируются новой схемой.
Предлагаются ровно семь новых persisted preferences:

| Preference | Поведение и предлагаемый default |
| --- | --- |
| `SidebarLeft` | Сторона панели; `false` — справа |
| `SidebarTextMask` | Маска текста после текущего cue; `false` |
| `SidebarShowOriginalText` | Показывать исходный текст вместо переведённого display text; `false` |
| `SidebarSubPadding` | Неотрицательный целочисленный отступ строки; `5` DIP |
| `SidebarFontFamily` | Только шрифт текста панели; requested family `Segoe UI` |
| `SidebarFontSize` | Только размер текста панели; `16` DIP |
| `SidebarFontWeight` | Только вес текста панели; `Normal` |

Defaults следуют WPF. Сейчас Avalonia использует размер `14` и отступ `8,6`; их изменение заметно
пользователю и входит в предлагаемое решение для владельца. Не описывай его как сохранение прежнего вида.
При отсутствии Segoe UI на Linux используй существующий font fallback без установки шрифтов.
Конкретные безопасные числовые пределы и валидацию размера/отступа зафиксируй в одобряемом плане.
Новые font style/color/inheritance keys и изменения overlay-шрифтов сюда не входят.

### Обязательные детали реализации после GO

- Settings остаётся detached draft: Close/Esc отбрасывают изменения. Save валидирует сырые значения,
  сливает только принадлежащие ему поля со свежими preferences и сначала сохраняет candidate,
  затем применяет его к живому состоянию. Ошибка записи не меняет live state и не закрывает Settings.
  Не теряй waveform, shortcuts и другие несвязанные preferences.
- Для original text нужен минимальный snapshot-шов приложения: `CueInfo` сейчас несёт только display text.
  Добавь исходный текст через `Services/IPlaybackController.cs` и `FlyleafPlaybackController.GetCues`;
  engine-модель и WPF config не меняются. Сохрани совместимость fake-controller и существующих тестов.
- WPF-маска (`LLPlayer/Converters/SubtitleConverters.cs:43–65`) заменяет текст равным числом `_`,
  когда `cue.Index > CurrentIndex`; до первого cue (`-1`) все строки будущие. Прошлые и текущая не маскируются.
  Исходные данные не затираются. Word lookup не должен получать скрытый исходный текст маскированной строки.
- При показе original text верно выставляй `IsTranslated` и язык word lookup. Переключатель панели
  не меняет перевод overlays или engine translation switches. Поиск использует выбранный original/display
  text, а не подчёркивания маски; при смене выбора активный фильтр пересчитывается.
- Обновляй отображение после перевода, append cues, замены media, seek, изменения текущего cue
  и перехода к состоянию без текущего cue. Сохрани highlight, seek по строке и append/replacement-семантику.
- Left/right docking должен сохранять splitter, remembered width, узкое окно, минимальную ширину video
  и fullscreen. В `MainWindow.axaml.cs` выбор колонки сейчас жёстко привязан к индексу 2.
- Применяй шрифт и padding только к sidebar cue rows; переиспользуй font catalog и существующие validators.
  Secondary-sidebar selection, новые search modes и нативные bitmap thumbnails не входят в этот блок.

Основные файлы внутри `LLPlayer.Avalonia/`: `Services/AppPrefs.cs`, `Services/IPlaybackController.cs`,
`Services/FlyleafPlaybackController.cs`, `ViewModels/SettingsViewModel.cs`, `ViewModels/MainWindowViewModel.cs`,
`ViewModels/SubtitlesSidebarViewModel.cs`, `Views/SettingsWindow.axaml`, `Views/MainWindow.axaml`,
`Views/MainWindow.axaml.cs`. Регрессии находятся в `LLPlayer.Avalonia.Tests/`.
Для переиспользования смотри `SidebarAndPopupTests`,
`TransportSidebarLayoutTests` и существующие Settings/font tests.

Engine key-binding editor, app-vs-engine collisions, новые shortcuts/actions, Translation/Word/Anki parity,
dependency upgrades, packaging redesign и другие страницы Settings остаются отдельными более поздними блоками.
Не выбирай их автоматически после завершения этой страницы.

## Проверки, публикация и границы

После одобрения работай в `codex/*` от проверенной актуальной базы. Сохрани чужие dirty-файлы и старые
worktrees/artifacts. Для новых исправлений поведения добавляй осмысленные детерминированные регрессии,
фиксируй intentional RED там, где есть безопасный seam, и проверяй реальное поведение, а не копию реализации.

Минимальный набор для общего Avalonia-блока:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify-fast.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\verify-avalonia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\test-package-avalonia.ps1
```

Linux: полный `scripts/linux/verify.sh`, при необходимости через фактический Linux CI,
с обязательной сверкой проверенного commit. Дополнительные publish/package gates определяет repo `AGENTS.md`
и фактический scope. WPF packaging сохраняет отдельный publish приложения и `Plugins/YoutubeDL`;
источник правил — `.github/actions/build-package/action.yml`. Native assets и publish profiles не меняй
в рамках Sidebar. Полные suites нельзя заменять focused-тестами или фиксированным числом PASS.

Для Sidebar проверь persistence/defaults/invalid input, discard/write-failure/restart, независимость overlay,
original/display lookup и поиск, privacy маски, lifecycle обновлений, layout/splitter и font fallback.
Делегируй review профильным ролям по матрице; свяжи каждый PASS с фактическими финальными файлами/commit.
После исправлений не переноси старый review на новые semantic changes без проверки соответствующего delta.

**Computer Use/CUA, запуск GUI/плеера/launcher, реальные ASR/OCR/translation/TTS/model/provider/GPU прогоны,
установка программ, шрифтов или моделей, WSL setup и reboot запрещены без отдельного разрешения владельца.**
Headless tests и разрешённые script/CI gates допустимы; они не доказывают native desktop/keyboard/glyph/DPI
acceptance. Незапущенное отмечай `NOT_RUN / DEFERRED BY OWNER`; не заявляй прохождение по старым receipts.

Заверши одобренный блок по действующему repo GitHub Flow: раздельные scoped product/docs commits,
проверки, независимый `/review`, фактические push/PR/CI/merge при действующей авторизации, затем
readback main и post-merge CI. Не выдавай локальный commit за merge. Публичный релиз и push vault
требуют соответствующего разрешения и не следуют автоматически из product merge.

Синхронизируй Second Brain по его `AGENTS.md`: точный allowlist, одна completed session, primary-запись
в terminal month shard и recent navigation, актуальный `_INDEX.md` с `as_of`/basis/одним продолжением,
project-scoped session-close, независимый review и изолированный vault commit. Историю сохраняй;
уточнения старых утверждений добавляй датированным erratum. Если что-то неясно, сообщи владельцу;
если ответ меняет одобряемое поведение, выясни его до зависимых изменений.

В итоговом отчёте раздели реализацию, локальные проверки, CI, merge/package delivery и native acceptance.
Назови фактические commit/PR, ограничения и единственную следующую точку продолжения. После одного
одобренного блока остановись; следующий продуктовый этап требует нового задания владельца.
