# Большой запрос: полный разбор Caramba и план подключения к SmartInput

## Цель

Нужно не просто расшифровать отдельные функции Caramba, а подготовить полную
техническую схему, как её подтверждённое поведение можно перенести в SmartInput
своей поддерживаемой реализацией.

Важно: recovery-пакет принадлежит владельцу проекта, но неизвестные бинарные
правила нельзя подключать в рабочую автозамену на основании догадок. Любая
неподтверждённая часть должна оставаться `audit-only`.

## Что уже есть в SmartInput

Текущая архитектура разделена на Core, Infrastructure, Platform.Windows и App.
Ключевые существующие компоненты:

```text
SmartInput.Core
  ILayoutConversionService / KeyboardLayoutConverter
  IWrongLayoutDetectionService
  IAutocorrectionService
  JointCorrectionDecisionService
  AutomaticLayoutCorrectionEngine
  LiveLayoutBoundaryGate
  SafeTextReplacementService
  CorrectionUndoService
  Double Shift Undo
  SafetyPolicyEvaluator / AutomationSafetyService
  CorrectionRejectionLearningStore / CorrectionRejectionPolicy
  PunctuationCorrectionService
  PunctuationPreviewService
  IPunctuationProvider
  RuleBasedPunctuationProvider

SmartInput.Infrastructure
  Csm1ModelRepository / Csm1ContainerReader
  DawgBlockReader
  RecoveredRulePack (audit-only)
  RecoveredBoundaryPreparation (audit-only)

SmartInput.Platform.Windows
  global keyboard monitor
  SafeTextReplacement via Win32
  keyboard layout service
  tray, hotkeys, secure-context detection

SmartInput.App
  startup/shutdown coordinators
  tray lifecycle
  settings/UI/navigation
  manual preview pages
```

Текущий live-поток SmartInput:

```text
Windows input monitor
  → safety/policy context
  → token/boundary detection
  → JointCorrectionDecisionService
  → SafeTextReplacementService
  → CorrectionUndoService
  → Double Shift Undo
```

Ограничения, которые нельзя нарушать:

- recovered rule-pack не вызывает Win32, hook или `SendInput`;
- Core не зависит от Windows;
- policy проверяется до любого предложения или применения;
- URL, email, пути, код, mixed-script, secure/password fields, Safe Mode,
  excluded apps, emergency pause и Protection OFF должны блокировать применение;
- текст не отправляется в сеть и не попадает в diagnostics;
- Double Shift остаётся существующей отменой, тройной Shift не добавляется;
- автоматическая замена должна быть одной Undo-транзакцией;
- при неоднозначности решение — `Wait`/`NoChange`.

## Что уже доказано по Caramba

- Есть forward и reverse substring matcher с перезапуском на каждом byte offset.
- Переходы и `0x100 output-link` подтверждены.
- A ID точно связаны с `group-2.field-3`.
- B ID точно связаны с `group-5.field-2`.
- `group-5.field-2.field3` адресует `group-5.field-3`.
- `0x68ED0` делает outer trim UTF-8 whitespace.
- `0x81D538` и `0x81D53E` — static descriptors.
- `0x39C20` создаёт owned 24-byte output object и вызывает writer `0x82F70`.
- После reverse collection используются 32-byte match records и 16-byte table.
- Все эти факты уже реализованы у нас только в audit-only адаптере.

## Что остаётся исследовать

Нужно закрыть следующие вопросы по EXE/debug/source/recovery-артефактам.

### A. Полная цепочка подготовки

1. Что делает `0x82F70` для descriptor streams:
   `01 02 C0 00` и `01 02 C0 01 03 00`?
2. Каковы opcode grammar, branch conditions и output bytes?
3. Какой exact buffer после `0x39C20` поступает в reverse matcher?
4. Что происходит с `е/ё`, регистром, Unicode normalization, layout mapping,
   повторными буквами и boundary markers?
5. Какие slices из структуры `0x7A960` соответствуют тексту, контексту и
   предыдущему токену?
6. Как обрабатываются пробел, дефис, Tab, Enter, punctuation, Backspace,
   смена окна и смена языка?

### B. Runtime model binding

1. К какому decoded buffer принадлежат:
   `global 0x140A79028`, `global 0x140A33350` и parameter `RDX` в `0x68200`?
2. Где создаются эти wrapper objects?
3. Где находятся внутренние record arrays и их размеры?
4. Как доказать SHA/размер decoded A/B без хеширования wrapper object?
5. Какой runtime object передаётся каждому из A-forward, A-reverse,
   B-forward, B-reverse?
6. Можно ли доказать язык/режим каждого объекта?

### C. Decoder и rule semantics

1. Что означает 32-byte match record по каждому offset?
2. Что означают поля `+0x08`, `+0x10`, `+0x18`, `+0x1C`?
3. Какая runtime 16-byte table читается через `index * 16`?
4. Какая runtime 24-byte table читается через `index * 24`?
5. Как они связаны с recovered `group-2`, `group-5` и auxiliary?
6. Что делает predicate `0x84DD0`?
7. Где score, weight, priority, candidate ranking и ambiguity decision?
8. Какая точная цепочка `outputId → rule → candidate → decision`?

### D. Policy, replacement и Undo

1. Найди call graph от decoder до `NoChange`, `Wait`, `Suggestion`, layout,
   spelling или replacement.
2. Найди связи с URL/email/path/code и secure input.
3. Найди terminal/IDE/excluded-app policy.
4. Найди Protection, emergency pause и user settings.
5. Найди replacement transaction, `SendInput`/clipboard и rollback.
6. Найди состояние, которое нужно для Double Shift Undo.
7. Отдели Caramba-specific policy от общей Windows-обвязки.

## Что нужно вернуть для каждой части

Для каждого утверждения ставь:

```text
CONFIRMED — доказано инструкциями или повторяемым synthetic trace
LIKELY — сильная гипотеза, но не полный data-flow
UNKNOWN — доказательств недостаточно
```

Нужны:

1. call graph с RVA/символами;
2. псевдокод и ABI;
3. структуры и размеры буферов;
4. synthetic traces только на заранее заданных строках;
5. hashes/длины вместо пользовательского текста;
6. таблица `source → prepared bytes → matcher → outputId → table → decision`;
7. точная причина каждого места, где data-flow обрывается.

## Как спроектировать подключение к SmartInput

Предложи архитектуру, совместимую с текущим кодом. Она должна иметь четыре
независимых слоя:

```text
ICarambaPreprocessor (только после доказательства)
  → IRecoveredMatcher (opaque IDs)
  → IRecoveredRuleDecoder (opaque candidate metadata)
  → ICorrectionDecisionAdapter (typed decision)
```

Ожидаемый безопасный контракт:

```text
Prepare(input, context)
  → PreparedInput | Skip(reason)

Match(prepared, automaton, direction)
  → OpaqueMatch[]

Decode(matches)
  → Candidate[] | UnsupportedModelSemantics

Decide(candidates, SafetyContext)
  → NoChange | Wait | Suggestion | Apply

Apply(decision)
  → существующая SafeTextReplacementService
  → существующая CorrectionUndoService
```

Правила миграции:

### Этап 1 — audit adapter

- Не менять live correction.
- Добавить только synthetic traces и агрегированную телеметрию.
- Сравнивать recovered output с текущим SmartInput без применения.

### Этап 2 — candidate preview

- Декодер возвращает только typed candidate metadata.
- Пользователь видит предложение в отдельном preview.
- Нажатие «Применить» проходит через существующие policy и Undo.
- Любой неизвестный флаг даёт `Unsupported`/`Wait`.

### Этап 3 — controlled integration

- Подключать только доказанные типы решений.
- Сначала allow-list конкретных операций и приложений.
- Обязательные regression, ambiguity, URL/code и secure tests.
- Kill switch и настройка отката на текущий движок.

### Этап 4 — live autocorrect

Разрешён только после доказанной точности, нулевых safety-регрессий,
стабильного Undo и независимого corpus-аудита. Если хотя бы один участок
classifier остаётся `UNKNOWN`, recovered path должен возвращать `Wait`.

## Обязательные acceptance tests

Подготовь тестовый план и fixtures для:

- `ghbdtn → привет` и `руддщ → hello`;
- `мущ → veo` без ручного словаря;
- spelling: `превет`, `жызнь`, `машына`, `стрвнно`;
- нормальные формы `начала`, `начало`, `дать`, `даёт`, `дают`;
- неоднозначные опечатки → `Wait`;
- URL, email, path, code, mixed-script → `NoChange`;
- terminal/IDE/excluded app → `NoChange`;
- secure/password field → `NoChange`;
- emergency pause и Protection OFF → `NoChange`;
- Double Shift после каждой применённой коррекции;
- пунктуационный preview отдельно от recovered model;
- память, latency и отсутствие token text в telemetry.

## Финальный ответ

Верни:

1. текущий статус всей цепочки;
2. доказанные и неизвестные узлы;
3. call graph от preprocessor до apply;
4. схему всех runtime tables;
5. рекомендуемый API и DI-регистрацию для SmartInput;
6. поэтапный migration plan с rollback;
7. acceptance matrix и expected results;
8. список необходимых debug/source артефактов;
9. явное решение, какие части можно подключить сейчас, а какие нельзя.

Не предлагай прямую подмену текущего движка recovered raw-правилами. Сначала
докажи цепочку `transform → matcher → outputId → decoder → classifier → policy
→ decision`, затем интегрируй её через существующие SafeApply и Undo.
