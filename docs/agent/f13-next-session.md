# Продолжение F-13: закрытие технического хвоста Settings — 2026-10-26

Продолжи только уже одобренный корректирующий блок по [плану](plans/2026-10-26-settings-audit-fixes.md).
Владелец после аудита явно разрешил исправить числовой UI, изоляцию ресурсов в тестах, доказательства,
документацию и Desktop delivery. Повторное одобрение этого scope не требуется. Полный F-13 parity остаётся
**IN PROGRESS**; новый функциональный блок автоматически не начинается.

## Источники и проверенная база

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
Перед продолжением сверь фактический branch/HEAD/status и новые receipts. Корректирующая ветка —
`codex/f13-settings-audit-fixes`; чужие dirty-файлы, worktrees и artifacts сохраняй.

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

## Подтверждённый дефект и корректирующий scope

Исходный read-only audit: `artifacts/dsh-two-session-audit-2026-10-26/AUDIT.md`, `verification.json`,
`CI-exact-main.json`. Он относится к `d1f5427`, до исправлений.

1. Headless ввод в реальный TextBox → focus/blur → Save воспроизвёл три RED: forward `1.5 → 2`, concurrency
   `0 → 2` и `65 → 2`; Save проходил без ошибки. Невалидный raw input должен дойти до существующей Save-validation
   и блокировать запись/live apply с понятной ошибкой. Диапазоны прежние: backward/forward — целые 0–1000,
   concurrency — целое 1–64. Сначала зафиксируй компилируемый RED, затем минимальный fix и GREEN того же теста.
   Проверь valid integer/spinner, Close/Esc discard и write-failure/retry через контрольную границу.
2. `EngineKeyBindingAdapterTests` оставлял owned controller/Player без Dispose. Гарантируй controller-then-Player
   cleanup и при assertion failure. Отдельно усиль проверки реального key-down/key-up после reapply и работающего
   custom delegate по существующему безопасному seam; production меняй только при новом воспроизводимом дефекте.
3. Согласуй текущие source docs, выполненные reviews, gates, CI, Desktop и Second Brain. У каждого результата
   должны быть собственные inputs/commit и наблюдаемый receipt. Будущий результат остаётся **PENDING** до проверки.

Свежие обычные suites на pre-fix `d1f5427` прошли: WPF 1440/0 skipped, Windows engine 1622/13 skipped,
UI 979/1 skipped. Дополнительные три input probes были RED; обычный suite не покрывал этот путь.
Exact-main CI Windows [40331221672](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/40331221672) и Linux
[40331222038](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/40331222038) прошёл verify/publish/upload.
Эти результаты не принимают новые исправления. Totals — наблюдения, а не фиксированные quality gates.

На момент аудита Desktop всё ещё выбирал `0811f615`/PR № 184: 479 файлов, ноль mismatch,
корректные direct EXE/no args/cwd/blue ICO. Обновления PR № 185 не было. Living vault также оставался на PR № 184.
После текущего closeout используй новые receipts; старую синюю иконку и её запуск не считай приёмкой нового payload.

## Исторические ошибки доказательств

FINAL3 PR № 185 привязан к `6e73ad8`; после docs receipt `0681af7` был test-only `e28e76c` с исправлением
xUnit1031. Утверждение «после FINAL3 менялась только документация» неверно. Финальный PR-head CI и exact-main
CI — отдельные receipts. Последние исторические delta reviews seq 7006/7008/7042 вернули FAIL; затем были fixes,
но нового независимого final-tree PASS в raw journal нет. Текущий `/review` фиксируется отдельно.

Linux PR № 185 run [40326448762](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/40326448762) падал на
Dispatcher wrong-thread и Outline `1e9999` Assert.NotNull. `c6c96c4` исправил Dispatcher test, отдельная причина
Outline остаётся **UNKNOWN**. xUnit1031 в Windows/Linux runs 40329219933/40329219932 исправлен в `e28e76c`;
финальные PR runs 40330061298/40330061376 и exact-main CI прошли. Прежние UNKNOWN PR № 170/172/173/181/183 сохраняются.

В старой DSH-сессии заявление «5/5 final PASS» было завышено: final product `24ec18f` проверили runtime/docs/
verification seq 7295/7297/7299; XAML PASS 7135 и .NET PASS 7172 относятся к более ранним inputs. Vault draft review
7397/7409 был FAIL без последующего независимого PASS. Не переписывай старые session bodies; сохраняй errata.

## Проверки, review и доставка

Используй все доступные агентские слоты через явный `spawn_agent`, с отдельным ownership файлов.
Минимальные gates для общего Avalonia-блока:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\verify-avalonia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\test-package-avalonia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\publish-avalonia.ps1
```

Полный Linux `scripts/linux/verify.sh` и publish/content checks нужны на фактически проверенном head через Linux CI;
локальный Linux укажи отдельно. Полные suites не заменяй focused-тестами. Обязателен отдельный spawned `/review`,
минимум `verification_reviewer` и профильные роли из review matrix; закрой Critical/Important и проверь delta.

После scoped product/tests и docs commits выполни разрешённый GitHub flow с PR/CI/merge и отдельным exact-main
readback. Затем на чистом финальном source HEAD запусти default fresh `scripts/windows/update-desktop-test-build.ps1`:
receipt `sourceCommit=HEAD`, новый payload/manifest, физические paths и ярлык direct EXE/no args/cwd/blue ICO
должны согласоваться. Сохрани предыдущие payload и WPF shortcut. Updater не запускает и не останавливает плеер.

Обнови только project-scoped allowlist Second Brain по vault-протоколу: completed session, primary index,
живой `_INDEX` с as-of/basis и одним продолжением, нужные Tests/snapshot, `session-close`, независимый review
и изолированный vault commit. Публичный релиз и push vault из этого не следуют.

Человеческий GO seq 4632 включал native acceptance и остальные Settings; seq 6047 уточнил три страницы.
Прежний запрет Computer Use/автоматического GUI не отозван. В текущем correction запуск player/launcher/CUA
не выполняется. Native GUI/DPI/OS-keyboard/real model/provider/Anki/GPU acceptance — **NOT_RUN**;
headless/native-library evidence не заменяет эти проверки. Не называй это отсутствием native GO в принципе.

В handoff назови фактические source commit/PR, gates/reviews, Desktop sourceCommit и vault result, ограничения
и единственную точку продолжения. Заверши этот корректирующий блок, затем остановись перед новым функциональным scope.
