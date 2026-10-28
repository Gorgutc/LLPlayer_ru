# Продолжение F-13: проверка и доставка исправления word selection — 2026-10-28

Сначала сверь repo/vault AGENTS, branch/HEAD/status, GitHub PR/main/CI, Desktop receipt и live Vault.
Текущий bounded scope — review/исправление уже доставленного
[word/phrase selection](plans/2026-10-27-f13-word-selection.md): sidebar должен ставить playback на паузу при
нажатии на открытое слово, сохраняя masked guard. Ветка `codex/f13-word-selection-review-fixes`, база merged main
`7e68b0a67f1044be65b126d88b3a2f0b1ac6434e`. **Computer Use и запуск плеера запрещены.** Старое разрешение native
audit ниже историческое и не отменяет этот запрет. Новые функции F-13, styling, провайдеры и модели не входят
в исправление; следующий функциональный блок требует нового owner GO.

Sidebar fix закоммичен отдельно: product `4318340fc495fa06ffb0c4d81242186c018d49ef`, только три app/test файла.
Compiled RED **2 expected failures/15 passed/0 skipped** (`BBA9B6DB`); initial GREEN 17/0/0 (`A136C8E5`).
После Dispose семи ignored capture results final focused GREEN **17 passed/0 failed/0 skipped**, exit 0,
лог `artifacts/local-model-selection-review-2026-10-28/selection-GREEN-cleanup.log`.
Свежие full WPF/Windows Avalonia gates, package checks, independent final review и дальнейшая доставка ещё
**PENDING на committed-product/draft-docs checkpoint**. Local Linux **NOT_RUN**.
Текущий статус и раздельные historical/fresh receipts —
[F-13 status](f13-status-2026-10-18.md#current-word-selection-review-repair--2026-10-28).

Продолжи первый фактически незавершённый шаг исправления: отдельный docs commit, свежие полные gates и cumulative
domain `/review`, затем обновление docs только фактическими receipts, living-Vault snapshot/session-close и PR с required
Windows/Linux CI. Перед merge повторно сверь GitHub/другие PR и living Vault; после merge проверь exact actual-main
CI, выполни fresh Desktop из final clean HEAD без запуска и scoped Vault closeout. Каждый этап получает свой
receipt; прежние CI/Desktop/Vault PR № 188 не принимают этот новый fix. Raw local/review logs прежнего этапа,
отсутствующие по документированным путям, остаются **UNVERIFIED** до retrieval DSH; не восстанавливай их из claims.

PR № 187/test isolation уже MERGED / DELIVERED на `9c4154b1`: required actual-main CI прошёл, обе исходные
Important CLOSED FOR NEW MAIN; исторические scheduler/writer причины UNKNOWN. PR № 188/selection также
MERGED / DELIVERED на `7e68b0a`, Desktop и Vault closeout завершены. Final PR head `4ce2f4c` имеет отдельные
runs `40504924534`/`40504924581`; прежние `40502751339`/`40502751511` относятся к `affca63`. Actual-main runs:
`40505617519`/`40505617536`. Final PR/main UI totals: Windows 1042/0/1, Linux 1041/0/2; local 1041/0/1 был до
десятого теста `9f5bd7e`. Причина прежнего local OpenAL FAIL **UNKNOWN**. Не повторяй завершённые этапы из-за
старого pending и не считай headless/CI native acceptance. После доставки исправления отчитайся и остановись
перед новым функциональным блоком. Полный F-13 parity остаётся **IN PROGRESS**.

## Предыдущий Settings audit checkpoint — исторический контекст

Ниже сохранён прежний prompt и его датированные receipts. Это история Settings-closeout, а не активный план нового блока.

Сначала сверь фактический GitHub/source/Desktop/vault checkpoint, затем закрой только оставшуюся часть уже
одобренного [корректирующего блока](plans/2026-10-26-settings-audit-fixes.md). Исправления числового UI и
изоляции adapter-тестов **реализованы и локально проверены** на `116a4ba`; не реализовывай их заново.
Свежий Desktop-кандидат `fcf4b49` прошёл [ограниченный native Windows audit](f13-native-visual-audit-2026-10-26.md)
с отдельными PASS/mock/seed/NOT_RUN/NOT_ESTABLISHED; повторять его без новых изменений или метода проверки не нужно.
На candidate checkpoint corrective PR/main CI, final-main Desktop и vault closeout ещё NOT_RUN; позднейшее состояние
определяют actual receipts. Если merge, final delivery, bounded visual evidence и vault closeout подтверждены,
отчитайся и остановись перед новым функциональным scope; иначе продолжи первый невыполненный шаг.
Полный F-13 parity остаётся **IN PROGRESS**.

Владелец явно разрешил исправления и технический closeout; повторное одобрение этого scope не требуется.
Новым прямым сообщением он также разрешил Computer Use/запуск Windows GUI для визуальной проверки всех последних
изменений с появления Desktop-иконки; наблюдавшиеся результаты и оставшиеся границы уже записаны в native audit.
Переход к новым функциям требует отдельного задания.

## Источники и проверенная база Settings audit — история

Прочитай vault `AGENTS.md`, project `_INDEX.md`, frozen `Conventions.md` и сессию по единственному указателю
«▶ Продолжить здесь». Затем прочитай repo `AGENTS.md`, `docs/agent/subagent-review-matrix.md`, локальные
`llplayer-context-keeper` / `llplayer-instruction-drift` и подходящие frozen contracts. Используй `@superpowers`
и подключённый `@github`; GitHub plugin нужен для сверки PR, default branch и CI. Сообщай о недоступных
инструментах и неоднозначных требованиях. Исторический vault checkpoint сверяй с текущим source, а не наоборот.

Канонический backlog — [backlog.md](backlog.md); текущие и исторические receipts —
[F-13 status](f13-status-2026-10-18.md). LLPlayer — C#/.NET 10 desktop: WPF остаётся поддерживаемым Windows
продуктом, Avalonia UI и Core общие для Windows/Linux. Сохрани WPF, engine, config/defaults, native assets,
зависимости и отдельные package flows вне корректирующего scope.

Проверенная база аудита — PR [№ 185](https://github.com/Gorgutc/LLPlayer_ru/pull/185), **MERGED** в
`d1f5427ff9a5793b358d6154507af69d2870c6e4`; финальный PR head — `e28e76c190e380e7d7f085a883dc265149387f66`.
Код исправлен в `b36655d40a75f8724c01d79ed745926415faed18`, два новых C# файла получили BOM-only нормализацию в
`0e4635a38b90e1f0b6fc2564e7fd6918c0a682fd`; полные local gates выполнялись на чистом committed
`116a4baa7acb49aca7480634df8b6c998cc2b1aa`. Историческая candidate branch — `codex/f13-settings-audit-fixes`.
Перед продолжением прочитай фактические branch/HEAD/status, GitHub PR/main/CI и live vault handoff: ветка могла
быть смёржена после этой записи. Сохраняй чужие dirty-файлы, worktrees и artifacts.

Visual candidate — `fcf4b4912c23f080f4db1a5a005e669d6372a566`, build
`fcf4b4912c23-20261026T151130963Z-1aca0724`, receipt SHA-256
`d0f05437168ad861cb31287c6e5eb37ef0539660ba4f6e9ec766b2eb3b2109f2`. Он fresh-published и проверен в изолированном
профиле; application/test source с `116a4ba` не менялся. Приёмка этого кандидата не переносится автоматически
на финальный merged-main payload.

## Уже сделано; повторно не реализовывать

- PR [№ 181](https://github.com/Gorgutc/LLPlayer_ru/pull/181): три исправления app-hotkeys.
  Его premerge head `56117ee` и старый prompt — исторические checkpoints.
- PR [№ 182](https://github.com/Gorgutc/LLPlayer_ru/pull/182): schema-2 Desktop updater, direct EXE,
  physical paths, синяя per-build ICO и rollback.
- PR [№ 183](https://github.com/Gorgutc/LLPlayer_ru/pull/183): Sidebar, семь prefs и approved WPF defaults,
  original/display/mask/search, typography и left/right docking.
- PR [№ 184](https://github.com/Gorgutc/LLPlayer_ru/pull/184), main `0811f615`: bounded engine-key editor
  built-in строк и app↔engine collision blocking. Add-row/action-assignment/custom-action UI вне этапа.
- PR № 185: три translation-параметра, Word click action, default Anki deck и Word Manager count/metadata/
  безопасный Clear all. LLM sampling/region maps, provider probes, word context-menu editor, PDIC,
  AnkiConnect endpoint и APKG options этим этапом не реализованы.

Описание Sidebar из прежней версии этого файла было историческим планом завершённого блока.
Его границы и результаты сохранены в [разделе Sidebar](f13-status-2026-10-18.md#bounded-avalonia-subtitles-sidebar-settings-block)
и Git history. Не запускай старый Sidebar/premerge workflow заново.

## Исправленный дефект и проверенный code-stage checkpoint

Исходный read-only audit: `artifacts/dsh-two-session-audit-2026-10-26/AUDIT.md`, `verification.json`,
`CI-exact-main.json`. Он относится к `d1f5427`, до исправлений.

1. Headless TextBox → focus/blur → Save воспроизводил `1.5 → 2`, `0 → 2`, `65 → 2` без ошибки Save.
   `IntegerNumericUpDown`, подключённый только к трём полям Translation, теперь сохраняет raw invalid text для
   существующей Save-validation. Диапазоны прежние: backward/forward — целые 0–1000, concurrency — целое 1–64.
   Компилируемый numeric RED: 24 failed/12 passed/0 skipped. Expanded GREEN2 покрывает повторный blur,
   valid integers/spinner, исправление ввода, culture/NumberFormat, DataContext, discard и write-failure/retry.
2. Adapter cleanup гарантирует controller-then-Player Dispose при construction/assertion failure; lifecycle RED
   до исправления — 2 failed/0 passed. Реальные key-down/key-up, repeated apply и сохранённый custom delegate
   проверены с FFmpeg fixtures. Expanded focused GREEN2: 60 passed/0 failed/0 skipped (47 numeric + 6 adapter +
   7 существующих Translation-dialog cases), exit 0. Отдельный no-native run: 6 skipped/0 failed, exit 0;
   skip не принимается за выполнение dispatch.
3. WPF full на `116a4ba`: 1440 passed/0 skipped, exit 0. Windows full attempt 1: FAIL — engine
   1621 passed/1 failed/13 skipped, downstream UI NOT_RUN; OpenAL child max 120.3333 ms при limit 100 ms,
   mean 23.954245 ms, 51 valid/54 reads, 0 slow reads, 3 mapping changes, max read 0.0715 ms, 0 underrun restarts.
   Независимая runtime/verification triage: engine/Core/engineTests/deps/fixtures не менялись против `d1f5427`,
   это не прежний slow-read sampler gap; причина **UNKNOWN**. Единственный unchanged full diagnostic attempt 2
   прошёл: engine 1622/13 skipped, UI 1031/1 skipped, exit 0. Это не диагноз flaky, host cause или audio fix.
4. Отдельный unfiltered UI: 1031/1 skipped; package contracts: 53 fixtures/38-file ZIP; exits 0.
   Независимые .NET/UI/runtime/verification и docs instruction/architecture reviews: 0 Critical/Important,
   anchor Minor исправлен. Поздний docs receipt delta проверяется отдельно перед commit. Source/log hashes —
   [в текущем статусе](f13-status-2026-10-18.md#current-owner-approved-settings-audit-correction--2026-10-26).

Свежие обычные suites на pre-fix `d1f5427` прошли: WPF 1440/0 skipped, Windows engine 1622/13 skipped,
UI 979/1 skipped. Дополнительные три input probes были RED; обычный suite не покрывал этот путь.
Exact-main CI Windows [40331221672](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/40331221672) и Linux
[40331222038](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/40331222038) прошёл verify/publish/upload.
Эти результаты не принимают новые исправления. Totals — наблюдения, а не фиксированные quality gates.

На момент аудита Desktop всё ещё выбирал `0811f615`/PR № 184: 479 файлов, ноль mismatch,
корректные direct EXE/no args/cwd/blue ICO. Обновления PR № 185 не было. Living vault также оставался на PR № 184.
Это pre-fix наблюдение. Прочитай новый receipt/sourceCommit и live vault state, прежде чем считать delivery
невыполненным или запускать updater повторно. Старую синюю иконку и её запуск не считай приёмкой нового payload.

## Исторические ошибки доказательств

FINAL3 PR № 185 привязан к `6e73ad8`; после docs receipt `0681af7` был test-only `e28e76c` с исправлением
xUnit1031. Утверждение «после FINAL3 менялась только документация» неверно. Финальный PR-head CI и exact-main
CI — отдельные receipts. Последние исторические delta reviews seq 7006/7008/7042 вернули FAIL; затем были fixes,
но нового независимого final-tree PASS в raw journal нет. Новые corrective reviews фиксируются отдельно и не меняют этот факт.

Linux PR № 185 run [40326448762](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/40326448762) падал на
Dispatcher wrong-thread и Outline `1e9999` Assert.NotNull. `c6c96c4` исправил Dispatcher test, отдельная причина
Outline остаётся **UNKNOWN**. xUnit1031 в Windows/Linux runs 40329219933/40329219932 исправлен в `e28e76c`;
финальные PR runs 40330061298/40330061376 и exact-main CI прошли. Прежние UNKNOWN PR № 170/172/173/181/183 сохраняются.

В старой DSH-сессии заявление «5/5 final PASS» было завышено: final product `24ec18f` проверили runtime/docs/
verification seq 7295/7297/7299; XAML PASS 7135 и .NET PASS 7172 относятся к более ранним inputs. Vault draft review
7397/7409 был FAIL без последующего независимого PASS. Не переписывай старые session bodies; сохраняй errata.

## Native audit: что уже наблюдалось и что остаётся открытым

Выполнены generated-media open/play/end/seek, F/Esc, A-B/waveform, Sidebar left/mask/search/font22/padding9 Save
и restart, dual overlay styling, native UIA invalid-number edit/focus + pointer Save, corrected 1/1000/64 persistence.
Word Manager проверен с синтетическими словами: правки/metadata/filter, first-Clear arm/disarm без удаления,
deck draft discard/session ownership; default-path TSV/APKG имеют три записи, позднее popup Save добавил четвёртую.
F1/filter и Main H/Ctrl+B, native Enabled H Save и Reset+Save проверены; тот же открытый sheet 461 сохранил фильтр.

Loopback mock показал RU overlay с English Original Sidebar, word/cue clipboard и popup `world`→`мир`/Save.
Режимы Word и font tuples seeded только в закрытом изолированном профиле: это не GUI ComboBox acceptance.
Primary Bold/Italic отрисован; seeded secondary Consolas только прочитан, его render не подтверждён.
Два resize attempts оставили 1282×792; minimum-size/high-DPI NOT_ESTABLISHED/NOT_RUN. Native capture/rebind/
collision/held-key-up, physical typing в owned dialog, custom export path, новый RU SRT picker и destructive Clear
не приняты. Реальные providers/models/ASR/OCR/GPU/audible output/AnkiConnect/native Linux NOT_RUN.
P3 Light-sidebar black shadow отложен в отдельный bounded styling proposal; здесь его не исправляй и не меняй
overlay defaults. Owned windows и mock закрыты, пользовательский профиль не использовался.

## Исторический маршрут Settings publication/delivery tail

Сначала установи текущие результаты corrective PR/main CI, final publish/Desktop и project-scoped vault closeout.
На candidate checkpoint они NOT_RUN; позднее могли быть выполнены. Не дублируй merge, уже актуальный
Desktop payload или завершённую vault session. Если код и receipts совпадают с проверенными inputs, продолжи
с первого невыполненного шага. Новый semantic delta требует подходящих повторных gates и независимого review.

Используй все доступные агентские слоты через явный `spawn_agent`, с отдельным ownership файлов.
Команды обязательных gates для изменённого общего Avalonia-блока:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\verify-avalonia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\test-package-avalonia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\publish-avalonia.ps1
```

Локальные WPF/Windows/package gates и source reviews на `116a4ba` уже описаны выше, включая первый FAIL.
Не объявляй их first-attempt PASS и не запускай заново без новых изменений или нерешённого вопроса.
Полный Linux `scripts/linux/verify.sh` и Windows/Linux publish/content checks нужны на фактически проверенном
corrective head через CI; local Linux — отдельный NOT_RUN на этом Windows host. Полные suites не заменяй
focused-тестами. Перед сдачей нужен отдельный spawned `/review`, минимум `verification_reviewer` и профильные
роли из review matrix; проверяй новые delta и закрывай Critical/Important.

Если публикация ещё не завершена, продолжи разрешённый GitHub flow с reviewed docs commit, PR/CI/merge
и отдельным exact-main readback. Не выводи новые PR number/main SHA/CI results из старых receipts.
Если Desktop ещё не подтверждён для финального clean source HEAD, запусти default fresh `scripts/windows/update-desktop-test-build.ps1`:
receipt `sourceCommit=HEAD`, новый payload/manifest, физические paths и ярлык direct EXE/no args/cwd/blue ICO
должны согласоваться. Сохрани предыдущие payload и WPF shortcut. Updater не запускает и не останавливает плеер.

Визуальный Windows audit кандидата уже записан. Не выдавай его NOT_RUN/NOT_ESTABLISHED cells за PASS и не
повторяй неудачные automation gestures вслепую. Если дальнейший разрешённый smoke воспроизведёт дефект,
исправь его в согласованных границах, повтори подходящие gates/review и обнови candidate. Финальные
publication/main/Desktop/vault должны соответствовать принятому source; candidate visuals сохраняют свою identity.

Если project closeout ещё не завершён, обнови только project-scoped allowlist Second Brain по vault-протоколу: completed session, primary index,
живой `_INDEX` с as-of/basis и одним продолжением, нужные Tests/snapshot, `session-close`, независимый review
и изолированный vault commit. Публичный релиз и push vault из этого не следуют.

Человеческий GO seq 4632 включал native acceptance и остальные Settings; seq 6047 уточнил три страницы.
Прежний запрет Computer Use/автоматического GUI соблюдался до нового прямого визуального GO; Windows
GUI/CUA разрешены в указанном audit scope. Bounded observations уже есть; отсутствующие native/DPI/key-phase
результаты остаются открытыми по case matrix, а не получают общий PASS.
Native Linux и real model/provider/Anki/GPU acceptance остаются отдельными: разрешение и результаты Windows
visual audit не доказывают их приёмку. Headless/native-library evidence также не заменяет native-наблюдения.

В handoff назови фактические source commit/PR, gates/reviews, Desktop sourceCommit и vault result, ограничения
и единственную точку продолжения. Заверши этот корректирующий блок, затем остановись перед новым функциональным scope.
