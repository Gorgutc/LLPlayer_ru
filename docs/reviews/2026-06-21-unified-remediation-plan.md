# LLPlayer — Единый поэтапный план устранения дефектов (2026-06-21)

> Артефакт двух multi-agent ревью (по 5 ревьюеров + adversarial-верификация + синтез).
> Объединяет **PASS 1** (корректность перевода/ASR — симптомы пользователя: *повторения/зацикливание* и *потеря текста*)
> и **PASS 2** (качество всего кода: мёртвый код, оптимизация, нелогичный код, дублирование).
>
> Статус реализации: Этапы 0–7 реализованы **кроме двух FROZEN-дефолтов** (`1.3` temperature, `1.5` NoContext),
> которые отложены по решению пользователя для отдельного обсуждения.

## Главный вывод

Симптомы — не один баг, а **цепочка отсутствующих предохранителей**. Модель иногда сбоит (её свойство), а код не
локализует сбой: нет anti-repetition параметров, нет детекции деградации, нет проверок на пустой/`null`/обрезанный
ответ. Два места **усиливают** сбой: KeepContext кладёт плохой ответ обратно в контекст (заражает следующие ~6 строк);
пустой результат кэшируется навсегда (`IsTranslated` ключуется по `null`); любой транзиентный сбой одного субтитра
выключает перевод всей дорожки.

---

## PASS 1 — корректность перевода/ASR (27 подтверждено)

| ID | Sev | Файл:строка | Суть |
|----|-----|-------------|------|
| 1.1 | Critical | OpenAIBaseTranslateService.cs:111 | KeepContext кладёт невалидированный ответ обратно в контекст → отравляет ~6 строк |
| 1.2 | High | OpenAIBaseTranslateService.cs:263 | Не отправляются frequency/presence/repetition_penalty |
| 1.3 | Medium | ITranslateSettings.cs:259 | **[FROZEN]** дефолт temperature=0 (Manual=ON) провоцирует петли |
| 1.4 | High | OpenAIBaseTranslateService.cs:188 | Нет детекции деградации / retry / reset |
| 1.5 | High | WhisperConfig.cs:122 | **[FROZEN]** NoContext=false → condition_on_previous_text=true (кросс-сегментные петли) |
| 1.6 | High | SubtitlesASR.cs:485 | ASR: нет дедупликации сегментов; пустые сегменты добавляются |
| 2.1 | Critical | OpenAIBaseTranslateService.cs:187 | null content (reasoning-модели) → NRE → отключает весь перевод |
| 2.2 | Critical | SubtitlesTranslator.cs:318 + SubtitlesManager.cs:953 | Пустой результат кэшируется навсегда → пустой субтитр не ретраится |
| 2.3 | High | OpenAIBaseTranslateService.cs:166 | finish_reason==length не проверяется |
| 2.4 | High | OpenAIBaseTranslateService.cs:307 | StripReasoning: пусто (reasoning-only) / утечка сырого `<think>` |
| 2.5 | High | SubtitlesTranslator.cs:223 | Оконная математика связывает countBackward с бюджетом вперёд |
| 2.6 | High | GoogleV1TranslateService.cs:146 | Хрупкий парс JSON → одна странная субтитра отключает всю дорожку |
| 2.7 | Medium | GoogleV1TranslateService.cs:137 | Склейка сегментов через `\n` + per-seg Trim → битый текст |
| 2.8 | Medium | MicrosoftTranslateServiceBase.cs:157 | Debug.Assert+null! маскирует битый 200 → NRE в Release |
| 3.1 | High | SubtitlesTranslator.cs:156 | Гонка singleton _translateTask + dispose CTS |
| 3.2 | Low | SubtitlesTranslator.cs:156 | Ранний выход на малом seek может выронить диапазон |
| 3.3 | Medium | SubtitlesASR.cs:487 | Кламп end-time может инвертировать start>end |
| 3.4 | Medium | SubtitlesASR.cs:979 | Язык пинится по первому (возможно тихому) чанку без валидации |
| 3.5 | Medium | GoogleV1/DeepLX/Microsoft/OpenAI | timeout-vs-cancel по английской строке исключения |
| 3.6 | Medium | AzureTranslateService.cs:17 | Кэш токена сбрасывается на любой ошибке → token thrash |
| 3.7 | Low | WhisperConfig.cs:181 | Опечатка: MaxSegmentLength проверяется дважды |
| 3.8 | Low | BingTranslateService.cs:9 | Мёртвое поле _settings |
| 3.9 | Low | DeepLXTranslateService.cs:25 | Per-instance HttpClient без пулинга |

## PASS 2 — качество кода (37 подтверждено, 1 отклонена)

**High (краши):** `Player.Open.cs:572` NRE при внешних субтитрах без видео · `VideoDecoder.cs:126` FindSWDecoder null-deref `codec->id`.
**Medium (логика):** `VideoDecoder.cs:1199` потеря рекурсии · `AudioDecoder.Filters.cs:36` deref `filter->name` · `Renderer.VF.D3.cs:50` hasD3Filters в цикле · `Remuxer.cs:174` DTS-карты не чистятся · `AppActions.cs:427` `+=` вместо `-=` (утечка) · `Utils.cs:653` off-by-one query · `Player.Screamers.cs:65` Log.Error на hot-path.
**Medium (perf hot):** `SelectableSubtitleText.cs:278` незамороженные кисти на слово.
**Dead code:** `ChildRenderer.cs` (целиком) · `AppActions.cs:757` shadowed-extension ToString → заголовок «SubtitlesPosition» · `ResizeBitmap`, `_subNum` (SubtitlesOCR) · `oldSpeed`, `GetDumpStreams`, `LogTrace`, `ZOrderHandler.WndProc`, DEVMODE-блок (NativeMethods) · `Remuxer.fmt`.
**Low:** прочие микро-оптимизации/чистки.
**Оставить как есть (публичный API / vendored):** `Audio.SamplesAdded`, `Language.ISO639_2T_TO_2B`, FlyleafLib `TicksToTimeConverter`, `WpfColorFontDialog/FontInfo`.
**Отклонено:** `Player.cs:396` `|` vs `||` — поведенчески идентично для bool.

---

## Поэтапный план

**Рабочий процесс:** ветка `codex/*` (или текущая рабочая); после каждого этапа `scripts/codex/verify.ps1`;
ручной дым по `docs/agent/manual-smoke-matrix.md`. Сборка с `-warnaserror` — код должен быть без предупреждений.

### Этап 0 — Critical: «глухие» сбои перевода *(потеря текста)*
- `OpenAIBaseTranslateService.cs` (2.1) — null-safe чтение `choices/message/content`.
- `SubtitlesTranslator.cs` (2.2) — не кэшировать пустой результат; `IsTranslated => !IsNullOrEmpty`.

### Этап 1 — High/Critical: зацикливание/деградация LLM *(повторения)*
- (1.1) гейт валидации перед enqueue в `_messageQueue`.
- (1.2/1.4) добавить `frequency/presence_penalty` (опц., default off) + детект вырождения + один retry со сбросом контекста.
- (2.3/2.4) проверка `finish_reason=="length"`; `StripReasoning` сигналит неудачу.
- (1.3) **[FROZEN — отложено]** дефолт temperature.

### Этап 2 — High: устойчивость провайдеров + гонка планировщика
- GoogleV1 (2.6 защитный парсинг, 2.7 склейка, 3.5 timeout/cancel), Microsoft/DeepLX (2.8), Azure (3.6),
  `SubtitlesTranslator` (3.1/3.2 гонка, 2.5 окно).

### Этап 3 — High: корректность ASR/Whisper
- (1.6) дедуп сегментов + фильтр пустых; (3.3) `end>=start`; (3.4) язык на непустом тексте; (3.7) опечатка.
- (1.5) **[FROZEN — отложено]** дефолт NoContext. Безопасная альтернатива: экспонировать
  Temperature/TemperatureInc/EntropyThreshold/LogProbThreshold (default unset → библиотечные дефолты).

### Этап 4 — High: краши media-pipeline
- `Player.Open.cs` NRE; `VideoDecoder.cs` FindSWDecoder + DecodeFrameNextInternal; `AudioDecoder.Filters.cs`.

### Этап 5 — Medium: логика рендера/ремуксера (cold-path)
- `Renderer.VF.D3.cs` hasD3Filters; `Remuxer.cs` Dispose DTS + двойной lookup + dead `fmt`.

### Этап 6 — Medium: hot-path perf + утечка + UI
- `SelectableSubtitleText.cs` frozen-кисти; `AppActions.cs` `+=`→`-=`; `WordPopup.xaml.cs` IsLoading; `GeneralConverters.cs` Visibility.

### Этап 7 — Low: безопасные механические чистки
- Удаления мёртвого private/internal-кода; опечатки; cold-path микро-оптимизации; dead-extension + починка заголовка «Subtitles Position».
- **Оставить:** публичный API библиотеки и vendored `WpfColorFontDialog`.

| Этап | Фокус | Риск | Симптом |
|------|-------|------|---------|
| 0 | Critical-сбои перевода | Низкий | Потеря текста |
| 1 | Зацикливание LLM | Средний | Повторения (+потеря) |
| 2 | Провайдеры + гонка | Средний | Потеря (+повторения) |
| 3 | ASR/Whisper | Средний | Повторения + потеря |
| 4 | Краши pipeline | Низкий-средний | Стабильность |
| 5 | Логика рендера | Низкий | Качество видео |
| 6 | Hot-path perf + UI | Низкий | Произв./UX |
| 7 | Чистки | Очень низкий | Гигиена |

**FROZEN (требует отдельного подтверждения):** 1.3 (temperature default), 1.5 (NoContext default).
