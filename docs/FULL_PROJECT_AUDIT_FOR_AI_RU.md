# Полный аудит SmartInput для независимого технического разбора

**Дата:** 2026-09-06  
**Статус документа:** снимок текущего рабочего дерева, а не описание желаемой архитектуры.  
**Назначение:** передать независимой нейросети или разработчику вместе со всем каталогом проекта.  
**Главное правило для ревьюера:** не считать функцию реализованной только потому, что в проекте есть класс, тест или страница UI. В этом документе явно разделены `LIVE`, `PREVIEW/AUDIT-ONLY`, `EXPERIMENTAL` и `НЕ ЗАКРЫТО`.

---

## 1. Краткий вывод

SmartInput — Windows-приложение на .NET 8 для локальной помощи при наборе текста. Его собственный движок умеет наблюдать клавиатуру, на границе слова пытаться исправлять неверную раскладку, отдельные опечатки, сниппеты и один безопасный тип пунктуации. Замена текста, Undo, Double Shift, Safe Mode и защита полей объединены в общий pipeline.

В проекте также есть безопасный адаптер исследованного Caramba/KBM rule-pack. Он пока **не является живым движком Caramba**: модель используется только для audit/preview и очень ограниченного allow-list. Недоказанные `outputId`, таблицы правил и classifier не имеют права менять внешний текст.

Главная текущая пользовательская проблема — нестабильность автоматической коррекции английской раскладки в русскую в реальных приложениях. Нужно отличать три независимые причины:

1. ввод сделан в приложении Safe Mode (например, Codex/CEF, IDE или терминал), где автозамена намеренно заблокирована;
2. пара была дважды отменена Double Shift и сохранена как запрещённая для автоисправления;
3. собственный консервативный decision engine выбирает `Wait` из-за неоднозначности или неполноты словарных форм.

Не следует устранять это отключением защит, прямой очисткой пользовательских данных или агрессивным "переводом всего". Требуется воспроизводимый тест в Notepad/WordPad, диагностика без текста пользователя и дальнейшее улучшение candidate/lexicon pipeline.

---

## 2. Обязательные ограничения для любого изменения

### 2.1. Privacy

- Введённый текст, кандидаты, замены, буфер токена, выделенный текст и содержимое буфера обмена не пишутся в лог, telemetry, crash report или сетевой сервис.
- Core-функции работают офлайн. Сетевые модели, LLM и голосовой ввод — отдельные будущие opt-in возможности, не часть текущего hook.
- Допускаются только агрегаты: счётчики, время, хэшированные synthetic `caseId`, причина блокировки и версия модели.
- Настройки, пользовательский словарь, snippets и пары отклонённых исправлений хранятся локально в `%AppData%\\SmartInput`.

### 2.2. Safety

Приоритет блокировок:

```text
Emergency Pause
  > SecureInput / неизвестный контекст
  > user-excluded application
  > Safe Mode application/window class
  > обычное разрешённое приложение
```

- Автоматические действия запрещены в secure input, UnknownContext, excluded app и Safe Mode.
- В Safe Mode отдельные явные ручные команды могут быть разрешены политикой; автоматическая замена не должна выполняться.
- URL, e-mail, пути, код, идентификаторы, смешанный алфавит и опасные токены защищаются от обычной автоматической коррекции.
- Только одна внешняя replacement transaction на одну границу слова.
- Любой внешний replace проходит через `SafeTextReplacementService` / Windows implementation и регистрируется в `CorrectionUndoService`.

### 2.3. Undo и обучение

- Double Shift сразу после успешной автооперации должен вернуть исходный текст вместе с boundary-символом.
- После **двух** отмен конкретной пары `(source, replacement, CorrectionKind)` `CorrectionRejectionPolicy` подавляет именно эту автооперацию.
- Это правило **не должно** блокировать ручную конвертацию выделенного текста.
- В UI есть путь «Мой словарь → Отклонённые исправления → Разрешить снова».
- Resident host теперь наблюдает изменения local learning store: разрешение пары в UI должно подхватываться без перезапуска host.
- Нельзя удалять или печатать содержимое пользовательского файла обучения в диагностике.

---

## 3. Проекты решения и зависимости

`SmartInput.sln` содержит девять проектов:

| Проект | Назначение | UI/Win32 |
|---|---|---|
| `src/SmartInput.Core` | модели, алгоритмы, policy, engines, portable integration | нет |
| `src/SmartInput.Platform.Abstractions` | контракты OS-интеграций | нет |
| `src/SmartInput.Platform.Windows` | hook, SendInput, tray, active app, caret, overlays | Win32 |
| `src/SmartInput.Infrastructure` | JSON persistence, словари, CSM1, recovered model adapters | нет UI |
| `src/SmartInput.Runtime` | runtime DI и coordinator-слой без Avalonia | Windows contracts |
| `src/SmartInput.ResidentHost` | лёгкий постоянный WinExe: hook + tray + runtime | Win32, без Avalonia |
| `src/SmartInput.App` | Avalonia settings UI, ViewModels, страницы | Avalonia |
| `tests/SmartInput.Core.Tests` | алгоритмические, persistence, safety, corpus/audit тесты | test host |
| `tests/SmartInput.App.Tests` | UI/DI/startup tests | Avalonia.Headless |

Критическое направление зависимостей:

```text
App (Avalonia UI) ─┐
                   ├─> Runtime ─> Core <─ Infrastructure
ResidentHost ──────┘       │          ^
                            └── Platform.Abstractions <── Platform.Windows
```

**Инвариант:** `SmartInput.Core` не должен ссылаться на Avalonia, Win32 или конкретный Windows service. Платформенный код должен быть за interfaces в `Platform.Abstractions`.

Основные точки DI:

- `src/SmartInput.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs` — общий runtime composition.
- `src/SmartInput.App/DependencyInjection/ServiceCollectionExtensions.cs` — UI registrations.
- `src/SmartInput.ResidentHost/Program.cs` — постоянный host.

---

## 4. Запуск и процессы

### 4.1. Обычное UI-приложение

`SmartInput.App` — Avalonia-приложение с настройками, словарём, пунктуационным preview и KBM comparison page. Полный UI тяжёлый по памяти, его нельзя использовать как репрезентативный resident-процесс.

Специальные режимы:

- `SMARTINPUT_TRAY_ONLY=1` — tray-first режим обычного приложения: окно создаётся по запросу.
- `SMARTINPUT_UI_ONLY=1` — settings-only окно без второго keyboard hook; используется resident host при выборе «Настройки».

### 4.2. Resident host — основной путь для реального теста ввода

`SmartInput.ResidentHost` — отдельный WinExe без Avalonia. Он инициализирует global input, автоматическую коррекцию, глобальные клавиши, tray, native overlays и reload локальных settings/learning files. При открытии настроек он запускает `SmartInput.exe` с `SMARTINPUT_UI_ONLY=1`.

Актуальная developer-команда:

```powershell
$env:SMARTINPUT_UI_EXE = "C:\Users\shuly\Desktop\Автоматический Т9 на комп\src\SmartInput.App\bin\Release\net8.0-windows\win-x64\SmartInput.exe"
& "C:\Users\shuly\Desktop\Автоматический Т9 на комп\src\SmartInput.ResidentHost\bin\Release\net8.0-windows\win-x64\SmartInput.ResidentHost.exe"
```

Перед этим надо закрыть другой экземпляр `SmartInput.exe`/`SmartInput.ResidentHost.exe`, иначе два hook-а могут создавать двойные boundary или конфликтовать друг с другом.

### 4.3. Где допустимо проверять

Для автокоррекции и автопереключения — Notepad, WordPad, Word, обычные поля браузера и редакторы кода после отдельной проверки policy. **Нельзя считать ошибкой**, если функция не срабатывает в:

- терминалы, песочницы, удалённый рабочий стол и игровые оконные классы;
- редакторы кода и IDE не блокируются встроенным Safe Mode по умолчанию; при необходимости их можно добавить в исключения;
- PowerShell, Windows Terminal, cmd/conhost;
- excluded applications, игры по известным class names, secure input.

Для терминалов, игр, песочниц, удалённого рабочего стола и secure input это ожидаемое Safe Mode поведение. Список встроенных правил: `DefaultSafeModeRules`.

---

## 5. Live pipeline собственных исправлений

### 5.1. Поток данных

```text
WindowsInputMonitor / boundary interceptor
  → LiveLayoutCorrectionCoordinator
  → token/context buffers + policy
  → LiveLayoutBoundaryGate
  → AutomaticLayoutCorrectionEngine
      → snippets / punctuation / joint spelling-layout decision
      → SafeTextReplacementService
      → CorrectionUndoService
      → notification (без текста)
```

Ключевые файлы:

- `src/SmartInput.Core/Services/AutomaticLayoutCorrectionEngine.cs`
- `src/SmartInput.Core/Services/LiveLayoutBoundaryGate.cs`
- `src/SmartInput.App/Services/LiveLayoutCorrectionCoordinator.cs`
- `src/SmartInput.App/Services/DoubleShiftUndoCoordinator.cs`
- `src/SmartInput.Platform.Windows/Services/WindowsTextReplacementService.cs`
- `src/SmartInput.Platform.Windows/Services/WindowsBoundaryKeyInterceptor.cs`

### 5.2. Раскладка

Собственный движок использует физическое RU↔EN keyboard mapping, `WrongLayoutDetectionService`, language/dictionary evidence и `JointCorrectionDecisionService`. Он рассматривает варианты:

1. оставить token без изменений;
2. spelling в текущем языке;
3. прямую смену раскладки;
4. combined: смена раскладки в памяти + одна bounded spelling-правка;
5. `Wait`, если нет единственного безопасного победителя.

Положительные сценарии покрывались тестами: `ghbdtn → привет`, `руддщ → hello`, `gtie → пишу`, короткие white-list service pairs (`z → я`, `ns → ты`, `jyf → она`) и unknown short names (`мущ → veo`, `пзг → gpu`). Реальное применение зависит от settings, policy, boundary и rejection learning.

**Нынешний живой риск:** не все русские словоформы присутствуют в compact local dictionary. Поэтому длинные фразы английской раскладкой могут получить `Wait`, хотя физическая конвертация понятна. Не нужно лечить это добавлением слов вручную по одному: нужен масштабируемый RU/EN word-form source и отдельная оценка фразовой/морфологической уверенности.

### 5.3. Орфография

Используются:

- compact `sidict` word index в `HunspellWordFormProvider.cs`;
- локальные embedded lexicons `ru_50k.csv`, `en_50k.txt`;
- bloom-фильтр русских словоформ;
- bounded candidate generator/ranker;
- integration adapters для SymSpell (`SymSpell 6.7.3`) и WeCantSpell.Hunspell (`7.0.1`) — они feature-gated/сравнительные и не должны без review незаметно стать агрессивным live path.

Логика принципиально должна сохранять точное известное слово: `начала`, `начало`, `дает`, `даёт`, `дать`, `даст`, `дают`, `меня`, `нас` и т. п. Наличие слова в словаре важнее частотного ранжирования. `е/ё` используется для membership/equivalence, но не как безусловная rewrite-инструкция.

Преднамеренные слабые места:

- Объекты из четырёх и более символов защищаются лучше, чем короткие слова.
- Bounded edit-distance=1 не обязан восстановить все сложные ошибки (`стрвнно`, `кмнда`, `здpвствуйте` и похожие могут требовать deletion+insertion/двух правок или более полного morphology index).
- Без sentence grammar model нельзя безопасно выбирать между правильными близкими словоформами.
- Если словарь или candidate set даёт двух убедительных родителей, должен быть `Wait`, а не угадывание.

### 5.4. Известный незакрытый corpus-аудит

В проекте есть тяжёлые audit/fuzz/mutation suites: `MaximumCorpusAuditTests`, `Seed42RecoveryGateTests`, `CorpusAuditBaselineTests`, `BruteForce*`, `OperationClusterRegressionTests` и смежные. Исторически после нескольких улучшений были достигнуты:

- exact known words changed = 0;
- `WrongCombined = 0`;
- BF/oracle disagreement = 0 в выборке;
- signature index misses = 0 в выборке;
- mandatory catalog = 104/104.

Но итоговый критерий старого corpus acceptance **не закрыт полностью**. Последние известные стадии имели trade-off между `AmbiguousApplied=0` и unique recovery ≥95%. Публиковавшиеся промежуточные числа нельзя считать итоговой сертификацией current tree. Перед следующим алгоритмическим этапом независимый ревьюер должен:

1. заново выполнить canonical seed 42, 1337, 20260903;
2. подтвердить независимость verifier от production gates;
3. измерить `AmbiguousApplied`, `WrongUniqueTarget`, `WrongDirectLayout`, `WrongCombined`, `ExactWordsChanged`, recovery;
4. не заменять проблему искусственным ужесточением, которое отправит почти всё в `Wait`.

### 5.5. Как правильно расследовать текущую EN→RU жалобу

Не логировать фразу пользователя. Сначала подтвердить:

1. что активен **один** resident process;
2. что Protection, Automatic Layout и соответствующие language flags включены;
3. что тест идёт в Notepad, а не Safe Mode;
4. что после token есть boundary (например, обычный пробел);
5. что конкретная source→replacement pair не заблокирована двумя Double Shift undo;
6. что UI action «Разрешить снова» реально reload-ится resident host;
7. что Windows replacement service не вернул blocked/failed status.

Только затем анализировать language score/dictionary evidence. Нельзя сохранять или отправлять набранную пользователем фразу как diagnostic sample.

---

## 6. Пунктуация

### 6.1. Live spacing cleanup — `LIVE`, но узкий

`IPunctuationCorrectionService` / `PunctuationCorrectionService` выполняют только одну безопасную операцию: удаляют **ровно один** случайный ASCII-space перед `, . ! ? : ;`.

Примеры ожидаемого поведения:

```text
"Привет ," → "Привет,"
"Как дела ?" → "Как дела?"
"Это важно !" → "Это важно!"
```

Сервис не должен трогать URL, email, пути, identifiers, code, tab/newline, ellipsis, abbreviation, decimal/version и multi-space formatting. Использует тот же Undo: Double Shift восстанавливает space+punctionation.

`RecentTextContextBuffer` хранит только краткий in-memory context для этой операции, не историю на диске.

### 6.2. Semantic punctuation preview — `PREVIEW`, не hook

Есть `IPunctuationProvider`, `PunctuationPreviewService` и ограниченный `RuleBasedPunctuationProvider`. Он делает предложения точки/вопросительного знака в конце и запятых перед небольшой группой союзов. Пользователь явно запускает preview в UI, применяет в окне и может отменить. Это не полная русская грамматика и не автоматическая пунктуация во время каждого нажатия.

### 6.3. Модели на будущее

Исследован вариант локального ONNX RUPunct-small. Он допустим только как offline provider, который:

- запускается вне keyboard hook;
- возвращает предложения знаков, а не переписывает слова;
- работает сначала исключительно через preview+Undo;
- имеет benchmark по precision/latency/memory и безопасные фильтры;
- добавляется только после проверки лицензии и упаковки tokenizer/model.

Не использовать три Shift: Double Shift уже занят Undo.

---

## 7. Caramba / KBM / recovered model

### 7.1. Что доказано и реализовано

`SmartInput.Infrastructure` содержит безопасную обработку переданного CSM1/model package:

- structural container validation;
- Ed25519 verification before decrypt;
- ChaCha20-Poly1305 with header as AAD;
- Zstandard decompression under size bound;
- SHA-256 verification, atomic install and rollback;
- DAWG bounds checking;
- output-link parsing into opaque `outputId`;
- forward/reverse substring matcher loops returning opaque results;
- exact KBM `prepared route → DAWG → outputId → indexed text-pool` candidate provider;
- model hash, pool bounds and canonical witness validation.

Ключевые области:

- `src/SmartInput.Core/Integration/KbmCandidateContracts.cs`
- `src/SmartInput.Core/Integration/KbmAuditComparisonService.cs`
- `src/SmartInput.Core/Integration/KbmAllowListCorrectionService.cs`
- `src/SmartInput.Core/Integration/CorrectionOwnershipGate.cs`
- `src/SmartInput.Infrastructure/RecoveredRules/`
- `src/SmartInput.Core/RecoveredRules/`

### 7.2. Что **не доказано**

Нельзя утверждать, что recovered pack уже делает Caramba 1:1. Не подтверждены:

1. точная normalisation/preprocessor до matcher;
2. runtime binding A/B и смысл маршрутов;
3. окончательный terminal bit и язык блоков;
4. rule-table decoder;
5. classifier `outputId → decision/score/replacement`;
6. полная policy/Undo/SendInput семантика исходной программы.

`outputId` — opaque ID, не слово, не замена и не confidence score. Match в DAWG сам по себе не разрешает внешний replace.

### 7.3. Разрешённые режимы ownership

`CorrectionOwnershipGate` имеет режимы `Disabled`, `AuditOnly`, `Preview`, `AllowList`, `Live`.

- **Disabled/AuditOnly:** безопасны; live SmartInput остаётся единственным владельцем замены.
- **Preview:** показывать кандидата без внешнего применения.
- **AllowList:** разрешён только для явно доказанных отношений, через existing safety/rejection/Undo pipeline. В docs указаны anchors `commersant → kommersant`, `infact → in fact`, `Аксенов → Аксёнов`.
- **Live:** не готов и не должен включаться на основании recovered data.

Default DI без `SMARTINPUT_KBM_MODEL_DIR` регистрирует `NullKbmCandidateProvider`. С подключённой моделью comparison UI показывает только сравнительный результат; кнопки произвольного external apply быть не должно.

### 7.4. Что дать ревьюеру вместе с проектом

Передавать весь каталог `C:\Users\shuly\Desktop\SmartInput-Engine-Handoff-2026-09-06` и начинать с:

- `00_READ_ME_FIRST_RU.md`;
- `research/CARAMBA_ENGINE_HANDOFF_FINAL_ANSWER_RU.md`;
- `research/CARAMBA_VS_OWN_ENGINE_DECISION_RU.md`;
- `docs/MODEL_AUDIT_STATUS_RU.md`;
- `docs/KBM_PROVIDER_INTEGRATION_RU.md`.

Рекомендуемая стратегия — hybrid: SmartInput владеет live safety/replacement/Undo, recovered model сначала работает в audit/preview; любое расширение allow-list требует test evidence.

---

## 8. Dictionary и память

### 8.1. Словари

Встроенный русский и английский baseline — не полноценный морфологический анализатор всех форм. Источники:

- `src/SmartInput.Core/Dictionaries/Lexicons/ru_50k.csv`;
- `src/SmartInput.Core/Dictionaries/Lexicons/en_50k.txt`;
- `src/SmartInput.Core/Dictionaries/Lexicons/ru_RU_forms.bloom`;
- generated compact packages `src/SmartInput.App/Dictionaries/Hunspell/{ru_RU,en_US}.sidict`.

`tools/SmartInput.DictionaryCompiler` строит compact UTF-8 index с offset table. Это экономит память против `HashSet<string>` на каждую форму, но полнота forms всё равно определяется исходным словарём.

**Нужное направление:** легально полученный полноформный RU/EN lexical source, binary FST/DAWG или memory-mapped sorted index, morphology only as evidence layer, а не как механизм, который переписывает известные слова.

### 8.2. Память — измеренные, а не обещанные цифры

На текущей машине ранее измерялось:

| Режим | Working Set | Private memory | Интерпретация |
|---|---:|---:|---|
| Обычный Avalonia UI | примерно 180–210 MB | примерно 153–201 MB | UI/Skia/.NET дороги |
| ResidentHost, idle | примерно 80–84 MB | примерно 46–49 MB | текущий лучший live путь |
| ResidentHost NativeAOT experiment | примерно 67 MB | примерно 52 MB | меньше WS, но не private |

Цель **30–40 MB private** относится только к resident hook+tray host, не к открытому Avalonia UI. На текущем .NET Windows-host она ещё не достигнута. Нельзя жертвовать safety/Undo/словарём ради фиктивного GC heap limit.

Принятые меры:

- UI вынесен из permanent host;
- compact dictionary representation;
- prediction и overlay lazy-init;
- Workstation GC / conserve-memory;
- source-generated JSON context;
- NativeAOT profile существует, но экспериментальный.

Известные следующие шаги: measurement after 1000 boundaries + 10-minute idle; memory-map/FST dictionary; module/dependency audit; packaging both EXE together. Не включать KBM full model в resident by default.

---

## 9. UI и текущие пользовательские функции

Основные разделы Avalonia UI:

- Главная / защита / emergency pause;
- Языки;
- Исправления (раскладка, орфография, пунктуация, notifications);
- Мой словарь (user words, NeverAutocorrect, rejected pairs → Allow again);
- Шаблоны текста;
- Приложения / excluded apps / Safe Mode;
- Горячие клавиши;
- Конфиденциальность;
- Дополнительно / compatibility diagnostics;
- Проверка пунктуации (manual preview);
- Сравнение KBM (audit/preview).

Тёмная тема была добавлена в UI как пользовательская настройка; её стоит отдельно проверить в real Windows high-contrast/light/dark scenarios. Это UI-задача, не влияет на correction decision.

---

## 10. Тесты: что есть и как их трактовать

### 10.1. Нормальные быстрые проверки

Последняя локальная проверка после resident-learning reload изменения:

```powershell
dotnet test tests\SmartInput.Core.Tests\SmartInput.Core.Tests.csproj `
  -c Debug --no-restore `
  --filter "FullyQualifiedName~CorrectionUndoTests"

dotnet build src\SmartInput.ResidentHost\SmartInput.ResidentHost.csproj `
  -c Release --no-restore
```

Результат: **19/19 CorrectionUndo tests passed**, host build succeeded with 0 warnings / 0 errors.

Ранее проходили focused sets для live layout, joint pipeline, punctuation, boundary ordering, Double Shift, automation safety и Safe Mode; App test suite ранее фиксировалась как 142 passed. Эти числа — evidence конкретных запусков, но не substitute для нового полного run после будущих значительных изменений.

### 10.2. Полезные группы тестов

| Область | Файлы/наборы |
|---|---|
| layout и joint | `WrongLayoutDetectionTests`, `JointCorrectionPipelineTests`, `UnknownLayoutAndServiceWordTests` |
| exact-word regressions | `ExactWordProtectionAndRegressionTests`, `MandatoryRegressionSuiteTests` |
| punctuation | `PunctuationCorrection*`, `PunctuationPreviewServiceTests`, `RuleBasedPunctuationProviderTests` |
| Undo и learning | `CorrectionUndoTests`, `CorrectionRejectionLearningTests`, `BackspaceRejectionLearningTests` |
| policy | `SafetyPolicyTests`, `AutomationSafety*`, `ApplicationPolicy*` |
| boundary/hook | `BoundaryOrderingTests`, `BoundaryKeyPairingTests`, Windows platform tests |
| compact dictionaries | `CompactWordIndexTests`, `RussianWordFormBloomFilterTests` |
| recovered model | `Csm1*`, `DawgBlockReaderTests`, `RecoveredRulePackTests`, `KbmCandidateProviderTests` |
| heavy audit | `MaximumCorpusAuditTests`, `Seed42RecoveryGateTests`, `BruteForce*`, fuzz/regression tests |

### 10.3. Проверка сборки

```powershell
dotnet build SmartInput.sln -c Release --no-restore
dotnet test SmartInput.sln -c Release --no-restore
```

Полный `dotnet test` может запускать тяжёлый corpus audit и потреблять заметные время/память. Ревьюер обязан отделить:

- обычные unit/integration failures;
- expected historical acceptance failures из corpus gate;
- тестовую инфраструктуру/timeout;
- реальный defect live path.

Не следует скрывать heavy tests или ослаблять assert только для зелёного CI.

---

## 11. Ручная матрица QA (без сохранения текста)

Проверять следует с одним resident host и включёнными Protection/Automatic Layout/Autocorrect/Punctuation. Записывать только имя приложения, feature и enum результата (`works`, `partial`, `blocked`, `not_tested`), без фраз пользователя.

Минимальный smoke set:

| Проверка | Ожидаемое поведение |
|---|---|
| `ghbdtn` + пробел в Notepad | `привет`, если пара не learned-suppressed |
| `руддщ` + пробел в Notepad | `hello`, если score/policy допускают |
| `z` + boundary | `я` (явный whitelist) |
| `мущ` + boundary | `veo` как guarded unknown short-name path |
| `Привет ,` | `Привет,` через spacing cleanup |
| Double Shift после apply | возвращает source + boundary |
| два Undo одной пары | дальнейшее auto apply пары блокируется |
| "Разрешить снова" в UI | resident host вновь допускает pair без restart |
| password field | никакой автооперации |
| Codex / terminal / IDE | auto action заблокирован Safe Mode |

Для реального ручного QA использовать `docs/COMPATIBILITY_MATRIX.md`, не вставлять реальные приватные сообщения в issue/report.

---

## 12. Приоритетный план работ

### P0 — стабилизировать live EN→RU и boundary

1. Сделать privacy-safe instrumentation с причиной `policy / learned-suppressed / no-boundary / no-candidate / Wait / replacement-failed`, без source/candidate text.
2. Реально проверить Notepad, Word, Chrome/Edge, Telegram и исключённые приложения. Не тестировать в Codex как показатель layout defect.
3. Проверить, что два host-а не работают одновременно и что UI-only process не регистрирует input hook.
4. Расширить RU/EN lexical coverage законным источником и проверить memory budget.
5. Разделить standalone physical-layout strong evidence от weak dictionary score для длинной фразы, не отменяя protection exact known words.

### P1 — завершить автокоррекцию инженерно, а не набором исключений

1. Зафиксировать independent corpus specification и frozen seed corpus.
2. Повторно закрыть ambiguity/wrong-target/recovery gates на 42/1337/20260903.
3. Не генерировать тысячи bespoke rules на synthetic mutation samples.
4. Сделать separate operation provenance / candidate universe common spec и independent verifier.
5. Любая агрессивность измеряется также по exact-word preservation и реальному typing latency.

### P2 — punctuation

1. Сохранить current spacing cleanup как единственную hook automation.
2. Улучшать semantic punctuation только в manual preview.
3. Оценить ONNX RUPunct-small локально: license, package size, CPU p95, precision on non-ASR text, safety filters.
4. Не добавлять LLM/сеть в hook.

### P3 — KBM/Caramba hybrid

1. Продолжать audit-only research: preprocessor, A/B binding, rule tables, classifier.
2. Собрать synthetic fixtures/known native traces без пользовательского текста.
3. Только после доказательства дать preview, затем tiny allow-list; не включать Live.
4. SmartInput сохраняет единственный ownership внешней replacement/Undo/policy.

### P4 — память и упаковка

1. Сделать installer, где UI и host лежат рядом.
2. Измерять private bytes, working set, managed heap, cold start и peak отдельно для host/UI.
3. Проверить memory map/FST dictionary и lazy model load.
4. NativeAOT — отдельный benchmark, не переключать по умолчанию без end-to-end input QA.

---

## 13. Вопросы к независимой нейросети / ревьюеру

1. Найти реальную причину EN→RU `Wait` на длинных фразах и предложить улучшение, которое не отключает Safe Mode, exact-word protection или learned rejection policy.
2. Предложить лицензируемый, компактный и полноформный RU/EN lexical source; дать оценку размера на диске/RAM и формата (FST/DAWG/mmap). Не рекомендовать загружать гигантский `HashSet<string>` в resident host.
3. Проверить `JointCorrectionDecisionService`, candidate generation, `OperationPrecisionGate`, `BoundedCandidateApplyGuard`, `WrongLayoutDetectionService` на разрыв между unit/corpus/live behavior.
4. Спроектировать diagnostic enums и aggregate metrics без raw text, пригодные для user-reported failure.
5. Проверить resident host reload semantics settings+learning и race conditions file watchers.
6. Проверить, что CSM1/KBM никогда не может bypass current `CorrectionOwnershipGate`, safety policy, rejection policy или Undo.
7. Составить reproducible test strategy: fast suite, nightly corpus, real app matrix, memory benchmark.
8. Для пунктуации выбрать безопасный offline preview provider; оценить ONNX runtime overhead и legal packaging.

---

## 14. Что **не надо** делать

- Не включать recovered Caramba/KBM как произвольный live engine.
- Не объявлять DAWG lookup доказанным словом/replacement.
- Не убирать `Wait` глобальным понижением порогов.
- Не добавлять пользовательские фразы в тестовые fixtures или логи.
- Не отключать CEF/IDE/terminal Safe Mode ради демонстрации.
- Не обходить Double Shift learning удалением пользовательского JSON.
- Не запускать два независимых global keyboard hook процесса.
- Не заявлять, что 30–40 MB уже достигнуты: current resident private memory пока около 46–49 MB на этой машине.
- Не считать 104 mandatory tests или один seed полной гарантией реального ввода.

---

## 15. Полезные документы в репозитории

- `ARCHITECTURE.md` — исходное разделение слоёв.
- `PRIVACY_ARCHITECTURE.md` — privacy policy.
- `SAFETY_LAYER.md` — policy states и Safe Mode.
- `docs/RESIDENT_HOST_RU.md` — отдельный resident process.
- `docs/MEMORY_OPTIMIZATION_PLAN_RU.md` — измерения и честные ограничения памяти.
- `docs/HUNSPELL_DICTIONARIES.md` — локальные словари.
- `docs/PUNCTUATION_PROVIDER_RESEARCH_RU.md` — локальная пунктуация/ONNX направление.
- `docs/MODEL_AUDIT_STATUS_RU.md` — confirmed/unknown recovered model.
- `docs/KBM_PROVIDER_INTEGRATION_RU.md` — KBM provider и запрещённые шаги.
- `docs/IMPLEMENTATION_ROADMAP.md` — staged hybrid plan.
- `docs/COMPATIBILITY_MATRIX.md` — ручная QA матрица.

---

## 16. Финальная формулировка состояния

**SmartInput уже имеет собственный live typing pipeline с безопасной заменой и undo.** Он не является завершённым аналогом Caramba: coverage словарей, EN→RU live reliability и corpus acceptance требуют дальнейшей работы. **Recovered Caramba/KBM model не является live engine**, а корректно удерживается в audit/preview/узком allow-list режиме. Основной ближайший результат должен быть не «больше агрессивных замен», а измеримо надёжная коррекция в разрешённых приложениях при сохранении privacy, Safe Mode, exact-word protection и Double Shift Undo.
