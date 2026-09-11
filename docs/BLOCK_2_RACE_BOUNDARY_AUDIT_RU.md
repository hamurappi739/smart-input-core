# Smart Input — блок 2: race/preflight/boundary audit

Дата: 2026-09-08  
Область: Rust не подключался к live replacement; C# live-путь и SendInput не расширялись.

## CONFIRMED

- Финальный race/order-профиль: **141/141**.
- Отдельный boundary/layout regression-профиль: **157/157**.
- FocusedCorrection-профиль: **205/205**.
- Synthetic seed safety counters: `AA=0`, `WU=0`, `ExactChanged=0`.
- Synthetic recovery: `94.07%`; действующий safety-floor — `90%`.
- Проверены ordering-сценарии preflight → token buffer → boundary delivery, watcher/context invalidation, deferred replay, Undo, rejection learning, punctuation/snippet boundary и protected-context gates.
- Устаревшая подготовленная коррекция теперь отклоняется, если её исходный токен не совпадает с актуальным буфером. Deferred boundary при этом доставляется ровно один раз через гарантированный cleanup-путь.
- Обратная раскладка с низкой частотностью не стала общим разрешением: она допускается только для явного проверенного anchor; обычный fallback сохраняет сильный частотный порог.
- В отчётах сохраняются только агрегаты, enum-статусы, частотные диапазоны и privacy-safe case-id/digest. Сырые токены в диагностические файлы не добавлялись.

## LIKELY

- Наблюдавшиеся ранее дубли первой буквы и «каша» на границе ввода согласуются с применением устаревшей prepared-коррекции или повторной доставкой boundary при изменении контекста.
- Проверка актуальности буфера и финальная доставка boundary должны снять этот класс гонок в live-пути, если adapter передаёт события в том же порядке, что и тестовый контракт.

## UNKNOWN

- Фактическая установка `WH_KEYBOARD_LL`, состав событий и injected-флаги в Telegram Desktop.
- Реальная видимость Unicode `SendInput` и Backspace в конкретном окне Telegram.
- ResidentHost RAM/CPU/latency; это отдельный блок 3.
- Поведение на всех сторонних приложениях и в защищённых полях без ручного прогона.

## Изменения

- `CurrentTokenBuffer` получил безопасный snapshot для проверки актуальности подготовленной пары.
- `AutomaticLayoutCorrectionEngine` отклоняет stale prepared correction внутри `try/finally`, поэтому boundary не теряется.
- `BoundedCandidateApplyGuard` и `JointCorrectionDecisionService` синхронизированы по узкому reverse-layout policy.
- `LayoutCorrectionAnchors` содержит только ограниченные обязательные layout-regression anchors; это не замена полноценной языковой модели.
- Corpus harness учитывает anchors при формировании ожидаемой политики и не ослабляет safety-проверки для произвольных случаев.

## Итог блока 2

Блок 2 принят для перехода к измерениям. Контракт гонок и boundary-порядка зелёный, но это ещё не доказательство реального Telegram-ввода. Следующий шаг — блок 3: отдельный измерительный запуск ResidentHost с RAM, CPU и latency без записи пользовательского текста.
