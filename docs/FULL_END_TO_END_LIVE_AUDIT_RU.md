# Полный end-to-end аудит SmartInput Core

Дата: 2026-09-07  
Рабочая ветка: `C:\Users\shuly\Desktop\Автоматический Т9 на комп`  
Режим: локальный Release-аудит, Rust только shadow/audit

## Итог

В ходе аудита подтверждены два независимых дефекта live-контура. Первый —
возможность одновременно запустить два процесса, каждый из которых
устанавливает глобальный `WH_KEYBOARD_LL`; оба процесса видят одну физическую
клавишу и оба могут запустить replacement. В ходе аудита действительно были
обнаружены `SmartInput.exe` и `SmartInput.ResidentHost.exe`, после чего
live-контур был разделён: resident host владеет hook, а UI запускается только
с `SMARTINPUT_UI_ONLY=1`.

Второй дефект непосредственно объясняет наблюдение `пgpt`: при первом событии
для валидного foreground-приложения координатор инициализировал контекст и
возвращал `true`, из-за чего обработчик выбрасывал текущее событие. Физически
первая буква уже попадала в приложение, но внутренний token-buffer начинался
со второй буквы. При последующем ручном toggle ядро меняло только укороченный
буфер, оставляя первую физическую букву перед результатом. Теперь первое
обычное событие обрабатывается, а подавленная boundary при смене контекста
по-прежнему доставляется fail-open.

Дополнительно закрыта гонка между hook-thread и асинхронным coordinator:
boundary gate теперь прикладывает полный hook-thread token snapshot к
конкретному boundary-событию. Core использует этот snapshot при spelling и
layout decision, а Double Shift — как источник исходного токена. Поэтому даже
если rolling buffer временно укорочен, replacement получает правильную длину
и не оставляет первую букву перед результатом.

Дополнительная гонка была в моменте между асинхронным boundary-handler и
быстрым следующим вводом. Она закрыта непрерывным `SendInput`-batch,
bounded-деферальным буфером, отменой pending manual action и упорядоченной
обработкой live-coordinator.

Ручное двойное нажатие Shift теперь не зависит от конкретного слова: оно
конвертирует последний обычный латинский/кириллический токен через layout
mapper, а после успешной автоматической коррекции сначала выполняет Undo.
Поля URL/email/path/code и mixed-token не переключаются автоматически и не
переключаются ручным fallback’ом.

## CONFIRMED

### 1. Конфигурация

Из локального `%AppData%\\SmartInput\\settings.json` прочитаны только флаги
конфигурации, без пользовательского текста:

```text
protection=true
automatic_layout=true
autocorrect=true
external_spelling_provider=true
punctuation=true
excluded_applications=0
```

Следовательно, пропуски орфографии не были вызваны выключенным spelling-флагом.
Rust DLL не является источником live replacement.

### 2. Фактический live-путь

```text
WH_KEYBOARD_LL
  -> HookObserved
  -> character resolver (ToUnicodeEx + foreground layout)
  -> boundary interceptor + KeyDown/KeyUp pairing
  -> safety policy
  -> Core joint decision
  -> SafeTextReplacementService
  -> one contiguous Backspace + Unicode SendInput batch
  -> replacement result
  -> exactly-one boundary delivery
```

`WindowsInputMonitor` применяет named mutex
`Local\\SmartInput.KeyboardRuntime.v1`. Второй runtime в той же
интерактивной Windows-сессии не получает право создать второй глобальный
hook и завершается с безопасной ошибкой. Это backstop даже при ошибочном
ручном запуске UI-процесса без UI-only environment.

### 3. Повтор букв

Подтверждённые механизмы, которые исправлены:

- второй процесс больше не может параллельно наблюдать и заменять тот же
  ввод;
- replacement формируется одним `SendInput` вызовом, а не отдельными
  вызовами для каждого Backspace/символа;
- Double Shift запускает replacement только после `ShiftUp`, когда второй
  Shift уже физически отпущен; дополнительно перед `SendInput` проверяется
  фактическое состояние Shift через `GetAsyncKeyState`, чтобы очередь
  `ShiftUp` не оставляла первый Backspace под модификатором (`пgpt`);
- первый обычный символ после старта host или получения фокуса больше не
  отбрасывается при инициализации application context; именно это устраняет
  укороченный внутренний токен и ведущую физическую букву при ручном toggle;
- полный токен из hook-thread preflight передаётся вместе с конкретным
  boundary-событием; Core и Double Shift больше не восстанавливают длину слова
  только из потенциально отставшего async rolling buffer;
- genuine KeyDown во время replacement удерживается в bounded queue;
- pending Double Shift action отменяется при следующем обычном KeyDown;
- `RecentTextContextBuffer` защищён lock и version counter;
- отложенные реальные клавиши после успешного replay синхронизируются с
  внутренними token-buffer’ами, иначе следующий boundary терялся бы внутри
  ядра;
- live correction observer обрабатывает события последовательно до снятия
  replacement barrier.

### 4. Double Shift

Исправлено:

- удалён hard-coded fallback только для одного служебного слова;
- добавлена layout-конверсия любого single-script токена;
- после успешной автоматической замены двойной Shift вызывает `Undo` с
  исходным boundary-текстом;
- если Undo недоступен, выполняется детерминированный ручной toggle текущего
  обычного слова без добавления лишнего пробела;
- URL/path/email/dotted identifier не выделяются как последний токен;
- действие отменяется новым пользовательским вводом до фактического
  replacement;
- окно двойного Shift вынесено в `AppSettings.DoubleShiftWindowMilliseconds`.
  Значение по умолчанию — 350 мс, runtime ограничивает его диапазоном
  150–1000 мс.

### 5. Орфография и morphology

Hunspell действительно используется для runtime-проверки форм, а SymSpell —
для ranked candidate lookup. Обнаружен отдельный дефект: Hunspell мог принять
форму с пропущенной второй согласной как morphology-known и ранний
`ShouldPreserveExactWord` запрещал исправление. Добавлено общее правило для
уникальной dictionary-backed вставки двойной согласной с частотным порогом;
это не список отдельных слов.

На расширенном локальном наборе WordProbe:

```text
cases=121
apply=118
wait=2
no_change=1
```

Проверены классы пропуска/лишней буквы, перестановки, гласные, окончания,
мягкий знак, формы глаголов и длинные русские слова. `wait`/`no_change`
остаются предусмотренным результатом для неоднозначных или недостаточно
частотных кандидатов.

### 6. Защищённые токены

Автоматическая замена остаётся запрещённой для URL, email, путей, кода,
CamelCase, mixed alphanumeric tokens, all-caps аббревиатур и других
protected-классов. Это правило не ослаблялось.

### 7. Unit/integration результаты

После патчей:

```text
SmartInput.App.Tests                         148/148 passed
LiveAutocorrectionIntegrationTests            20/20 passed
JointCorrectionPipelineTests                  90/90 passed
ExactWordProtectionAndRegressionTests        107/107 passed
BoundaryOrderingTests                          12/12 passed
Rust-backed engine cases                        3/3 passed
DoubleShiftUndoCoordinatorTests               13/13 passed
Rust cargo debug tests                         11/11 passed
Rust cargo release tests                       11/11 passed
```

`dotnet build SmartInput.ResidentHost -c Release --no-restore` после последнего
патча завершился с 0 errors и 0 warnings. После добавления token handoff
перезапущены: весь `SmartInput.App.Tests 148/148` и
`LiveAutocorrectionIntegrationTests 20/20`.

`ScorerAudit` на held-out-наборе из 95 синтетических случаев подтвердил:

```text
Core false_apply=0
Core wrong_target=0
Rust false_apply=1
Rust wrong_target=0
liveReplacement=false
rawTextLogged=false
```

Rust продолжает работать только в shadow/audit. Его решение не вызывает
`SendInput`, не меняет policy и не заменяет Core.

### 8. ResidentHost

Чистый Release ResidentHost стартует, поднимает hook и публикует только
агрегатный status pipe. Вторая попытка запуска завершается mutex-защитой.
В проверенном idle-снимке:

```text
IsMonitoring=true
LastPolicyState=Allowed
WorkingSet примерно 105 MB
Private memory примерно 67 MB
```

Эти цифры относятся к C# resident с загруженным Hunspell и не являются
размером Rust DLL. Они выше целевого 30–40 MB compact-core и требуют
отдельной mmap/FST-оптимизации.

## LIKELY

### 1. Причина пользовательской каши

Два источника уже подтверждены кодом и локальными наблюдениями: второй
глобальный hook и отбрасывание первого события при инициализации/смене
фокуса. Код теперь защищён от обоих механизмов. Точная частота каждого
механизма именно в Telegram всё ещё не измерена физической трассой.

### 2. Почему отдельный консольный тест проходил

Консольный/WordProbe и прямой Core вызов проверяют candidate и replacement
без реального `WH_KEYBOARD_LL`, `ToUnicodeEx`, boundary KeyUp и Telegram
обработки `SendInput`. Они подтверждают словари, но не весь Windows event path.

### 3. Почему часть слов остаётся без исправления

Небольшая часть morphology-known или frequency-low форм сознательно получает
`Wait`/`NoChange`. Это ограничение precision-first режима, а не ошибка
replacement. Ослаблять этот gate глобально опасно: возрастут ложные
исправления имён, терминов и редких слов.

## UNKNOWN

Следующие пункты в этой сессии не доказаны физическим вводом:

1. видимый результат replacement именно в Telegram Desktop после последнего
   патча;
2. реальные значения `HookObserved -> CharacterResolved ->
   BoundaryIntercepted -> Policy -> Decision -> Replacement -> Boundary`
   при фокусе Telegram;
3. принимает ли конкретная версия Telegram весь Unicode `SendInput` batch;
4. фактическая частота повторов после запуска только нового ResidentHost;
5. поведение Secure Input и Safe Mode в каждом внешнем приложении.

Причина: Computer Use в текущем сеансе не предоставил native Windows apps,
поэтому Telegram нельзя честно объявить протестированным. Кодовый анализ и
локальные тесты не заменяют этот ручной этап.

Полный corpus/seed-42 diagnostic suite также не считается зелёным: в нём
остались старые oracle/report failures (`NonR1RecoveryGate`,
`RecoveryLossAnalysisReport`, `R1ResidualAnalysisReport`,
`AmbiguityAcceptanceDiagnostic`, `Seed42RecoveryGate`). Они воспроизводились
до текущих live-патчей и относятся к acceptance-метрикам corpus, а не к
Windows replacement path. Быстрые live suites и Release build зелёные.

## Изменённые участки

- `src/SmartInput.Platform.Windows/Services/WindowsInputMonitor.cs` — single
  owner mutex, replacement deferral, complete replay reconciliation;
- `src/SmartInput.Platform.Abstractions/Input/KeyboardObservationEventArgs.cs`
  — marker deferred replay и hook-thread completed-token handoff;
- `src/SmartInput.Platform.Abstractions/Input/IBoundaryKeyInterceptor.cs` —
  boundary result и gate contract для completed-token handoff;
- `src/SmartInput.App/Services/LiveLayoutCorrectionCoordinator.cs` — ordered
  live event processing и сохранение первого символа при инициализации/
  смене application context, передача hook-token в Core;
- `src/SmartInput.Core/Models/TokenInputEvent.cs` и
  `src/SmartInput.Core/Services/AutomaticLayoutCorrectionEngine.cs` —
  использование полного token snapshot на boundary;
- `src/SmartInput.Core/Services/LiveLayoutBoundaryGate.cs` и
  `src/SmartInput.Platform.Windows/Services/WindowsBoundaryKeyInterceptor.cs`
  — формирование и привязка snapshot к boundary-событию;
- `src/SmartInput.App/Services/DoubleShiftUndoCoordinator.cs` — generic toggle,
  cancellation, context versioning, physical-Shift release check,
  configurable window;
- `src/SmartInput.Platform.Abstractions/Input/IKeyboardModifierState.cs` и
  `src/SmartInput.Platform.Windows/Input/WindowsKeyboardModifierState.cs` —
  чтение физического состояния Shift перед ручным replacement;
- `src/SmartInput.Core/Services/RecentTextContextBuffer.cs` — thread safety and
  mutation version;
- `src/SmartInput.Core/Engines/RussianOrthographyHeuristics.cs` — generic
  morphology-aware double-consonant recovery;
- `src/SmartInput.Core/Models/AppSettings.cs` — Shift-window setting;
- `src/SmartInput.Runtime/DependencyInjection/RuntimeServiceCollectionExtensions.cs`
  — settings/converter wiring;
- `tests/SmartInput.App.Tests/DoubleShiftUndoCoordinatorTests.cs` — generic
  layout, URL guard, configured window and cancellation tests;
- `tests/SmartInput.Core.Tests/LiveExternalSpellingRegressionTests.cs` —
  morphology regression cases.
- `tests/SmartInput.Core.Tests/LiveAutocorrectionIntegrationTests.cs` —
  generic duplicate-initial-letter recovery cases.

## Как запускать следующий ручной этап

Сначала должен работать только один resident host. Settings UI можно запускать
через tray; он не должен создавать hook. После последней пересборки проверено:
`resident_process_count=1`, `IsMonitoring=true`.

```powershell
$env:SMARTINPUT_UI_EXE = "C:\Users\shuly\Desktop\Автоматический Т9 на комп\src\SmartInput.App\bin\Release\net8.0-windows\win-x64\SmartInput.exe"
& "C:\Users\shuly\Desktop\Автоматический Т9 на комп\src\SmartInput.ResidentHost\bin\Release\net8.0-windows\win-x64\SmartInput.ResidentHost.exe"
```

В Notepad или Telegram нужно проверить отдельными строками:

- layout EN→RU и RU→EN;
- обычные опечатки с пропуском/лишней буквой;
- быстрое продолжение фразы сразу после замены;
- двойной Shift после автоисправления;
- двойной Shift на обычном слове, которое не прошло авто-кандидат;
- URL, email, path, code, all-caps и mixed token;
- отсутствие двойного пробела и изменения запятой на точку.

В status pipe должны расти только aggregate counters; исходные и исправленные
слова в логах/отчётах появляться не должны.

## Решение о передаче нейронке

Передавать можно уже сейчас как baseline Core/runtime и shadow Rust ABI:

- live authority — C# Core;
- Rust — только дополнительный scorer/audit;
- двойной Shift — детерминированная команда ядра;
- replacement — один безопасный Windows transaction;
- privacy/safety gates сохранены;
- известные ограничения и неподтверждённый Telegram-этап явно перечислены
  выше.

Подключать Rust к live replacement или ослаблять policy до завершения
физического Telegram-прогона нельзя.

## Последний автоматический прогон — 2026-09-07

CONFIRMED:

- Release solution build: 0 ошибок, 0 предупреждений.
- `SmartInput.App.Tests`: 148/148.
- Live/Core suites по boundary, correction, undo, exact-word и stress: 231/231.
- Double Shift и layout-related App suite: 13/13.
- Rust debug и release tests: успешно; аварий и падений FFI не обнаружено.
- WordProbe: 121 синтетический case, 118 Apply, 2 Wait, 1 NoChange.
- ResidentHost: 1 процесс; Settings UI: 1 процесс; `IsMonitoring=true`.
- Aggregate status pipe после прогона: hook observed=2800, filtered=86,
  character resolved=2059, boundary intercepted=6, policy evaluated=2709,
  decision evaluated=2072, replacement attempted=2, replacement result=2,
  boundary delivered=6.

Ограничение: физический ввод именно в Telegram Desktop из текущей среды не
выполнялся, потому что native UI недоступен для автоматизации. Поэтому факт
видимого результата в Telegram остаётся UNKNOWN и не подменён результатом
unit/integration-тестов. Rust по-прежнему shadow/audit-only.

## Live replay hardening — 2026-09-07

После ручного прогона обнаружена дополнительная причина смешанного ввода:
отложенные физические события воспроизводились VirtualKey-кодами после
асинхронного переключения раскладки. Это могло превратить русскую клавишу в
латинскую и рассинхронизировать внутренний буфер с видимым текстом.

Исправлено:

- для deferred KeyDown сохраняется символ, разрешённый на hook-thread;
- символьные пары KeyDown/KeyUp воспроизводятся Unicode INPUT с сохранённым
  символом;
- VirtualKey replay оставлен для модификаторов и навигационных клавиш;
- matching KeyUp удерживается до replay соответствующего KeyDown;
- coordinator использует сохранённое разрешение и не вычисляет его второй раз
  уже после смены раскладки;
- добавлены регрессии `WindowsDeferredReplayInputTests`: 2/2.

Текущие проверки после патча: `SmartInput.App.Tests` 148/148; replay
regressions 2/2; Release build без ошибок. Физический Telegram-прогон всё ещё
требует ручной проверки на машине пользователя.

Отдельно: persistent learning содержит достигший порога отказ для одного
reverse-layout варианта после двух Double Shift Undo. Это штатное поведение
защиты, а не дефект словаря; повторное разрешение выполняется в UI через
«Мой словарь» → «Отклонённые исправления» → «Разрешить снова».

## Hook-resolution hardening — 2026-09-07

Дополнительно исправлена рассинхронизация между hook-thread и dispatcher:
`WindowsBoundaryKeyInterceptor` теперь передаёт в память полное
`KeyboardCharacterResolution` каждого наблюдаемого KeyDown. Координатор больше
не вызывает `ToUnicodeEx` повторно для того же физического события после
возможной смены раскладки. Это распространяется на обычные символы,
границы, Backspace/reset и неопределённые события; raw text в диагностику не
попадает.

После этого патча:

- x64 Release ResidentHost: 0 ошибок; 2 существующих предупреждения компилятора
  в Core/Infrastructure, не связанные с патчем;
- x64 Release SmartInput.App: 0 ошибок, 0 предупреждений;
- Core live/boundary/layout/spelling suite: 194/194;
- App suite: 148/148;
- deferred replay regression suite: 2/2.

Физический результат в Telegram по-прежнему нельзя объявлять подтверждённым из
этой среды: native-окно недоступно для автоматизированного ввода. Для этого
нужен один ручной прогон после запуска именно x64 Release ResidentHost.

## Comma-as-Russian-б hardening — 2026-09-07

Исправлен отдельный случай английской раскладки: клавиша запятой является
физической клавишей русской `б`. Ранее она преждевременно завершала токен и
давала разрыв внутри русского слова. Теперь запятая внутри/в начале
латинского preflight-токена сохраняется до следующей границы; при наличии
строго подтверждённого словарного кандидата заменяется весь токен целиком.
Обычная русская пунктуация не проходит этот путь.

Регрессии: `LiveLayoutBoundaryGateTests`, `WindowsBoundaryKeyInterceptorTests`
и replay — 18/18. Физический ввод в Telegram после этого изменения остаётся
неподтверждённым до ручного прогона пользователя.

## Input-latency and boundary-release hardening — 2026-09-08

По жалобе на задержку/пропажу пробела проведён отдельный аудит порядка
событий. Подтверждённая причина была составной:

1. `LiveLayoutCorrectionCoordinator` синхронно ожидал полную async-обработку
   каждого события, включая обычный KeyDown, хотя такой KeyDown уже был
   доставлен целевому приложению.
2. После подавления границы `WindowsInputMonitor` сохранял 250-мс grace-barrier
   даже после завершения успешной коррекции. Следующие реальные клавиши могли
   задерживаться или попасть в replay в неправильном порядке.
3. При истечении grace-интервала монитор мог начать replay, пока suppressed
   boundary ещё находилась в очереди/обрабатывалась. Это объясняет риск каши и
   повторов при быстром наборе после коррекции.

Исправлено:

- обычные события теперь передаются coordinator в фоновой последовательной
  очереди; синхронно ожидается только физически подавленная граница;
- добавлен bounded счётчик queued/in-flight suppressed boundaries;
- пока такая граница не завершена, deferred user events не replay-ятся;
- после доставки/отказа границы barrier снимается немедленно, без лишних 250 мс;
- fail-open и exactly-once boundary delivery сохранены;
- Secure Input, Safe Mode, исключения, Double Shift и Rust shadow-only policy
  не ослаблялись.

CONFIRMED после патча:

- `SmartInput.App.Tests`: 148/148;
- live/boundary targeted Core suite: 29/29;
- отдельная coordinator/boundary regression subset: 13/13;
- `SmartInput.App` x64 Release: 0 ошибок, 0 предупреждений;
- `SmartInput.ResidentHost` x64 Release: 0 ошибок, 0 предупреждений.

UNKNOWN:

- фактическая задержка и видимый результат в Telegram Desktop после этой
  пересборки требуют ручного ввода на машине пользователя;
- из текущей среды нельзя честно подтвердить реальный hook trace Telegram.

## Ручной критерий приёмки после перезапуска

Проверять следует только после запуска актуальных x64 Release-бинарников:

- правильное русское/английское слово + пробел: пробел появляется сразу;
- ошибочное слово + пробел: максимум одна замена и ровно один пробел;
- быстрое продолжение фразы: следующий токен не должен дублироваться или
  прилипать к предыдущему;
- Double Shift после автоматической замены: одно детерминированное Undo;
- URL, email, path, code, all-caps и mixed token остаются без автоизменения.

Этот раздел не утверждает, что Telegram уже физически протестирован: до
ручного прогона пользователя его результат остаётся UNKNOWN.

## Context-rebase hardening — 2026-09-08

В ходе проверки подтверждён ещё один независимый рассинхрон live-пути.
`LiveLayoutBoundaryGate` получает KeyDown в hook-потоке раньше, чем queued
обработчик coordinator успевает снять свежий snapshot foreground-контекста.
При смене окна coordinator сбрасывал gate после уже принятого первого
символа. Core при этом добавлял текущий символ заново, а preflight-gate
оставался без него. Следующая граница могла анализировать укороченный токен,
что объясняет дублирование/пропуск первой буквы и нестабильное решение при
быстром вводе.

Минимальное исправление:

- после context reset текущий hook-resolved Character один раз заново
  добавляется в `LiveLayoutBoundaryGate`;
- boundary повторно не добавляется: suppressed boundary при смене контекста
  по-прежнему доставляется fail-open ровно один раз;
- Backspace/Reset/Uncertain не переигрываются в gate;
- raw text и пользовательские строки в диагностику не добавляются.

CONFIRMED:

- `SmartInput.Core.Tests` live/boundary targeted subset: 30/30;
- `SmartInput.App.Tests`: 148/148;
- `SmartInput.App` x64 Release: 0 ошибок, 0 предупреждений;
- `SmartInput.ResidentHost` x64 Release: 0 ошибок, 0 предупреждений.

UNKNOWN:

- физический Telegram-прогон после этого патча ещё не выполнен в этой среде;
- измерение видимой задержки и проверка Double Shift требуют ручного ввода;
- исправление не подключает Rust к live replacement и не меняет защитные
  режимы.

## Secure-input detector hardening — 2026-09-08

Проверка исходного Win32-кода подтвердила, что `GUITHREADINFO.Flags` ошибочно
использовался как будто в нём есть secure-input flag `0x40`. Этот бит не был
надёжным признаком защищённого поля.

Сделан отдельный минимальный патч:

- удалён ложный `0x40`-путь;
- для стандартных Win32/WinForms password-контролов проверяется реальный
  стиль `ES_PASSWORD`;
- распознаются явно именованные password/credential-классы;
- ошибка чтения handle/class/style возвращает `Unknown`, а не разрешает
  автоматизацию;
- для незащищённых стандартных контролов поведение не изменяется;
- Rust, live replacement и остальные защитные режимы не затрагивались.

CONFIRMED:

- detector regression tests: 6/6;
- `SmartInput.App` x64 Release: 0 ошибок, 0 предупреждений;
- `SmartInput.ResidentHost` x64 Release: 0 ошибок, 0 предупреждений;
- после перезапуска resident status: `IsMonitoring=true`.

LIMITATION / UNKNOWN:

- кастомные password-поля Chromium/Electron/WPF и собственные поля приложений
  не обещают универсальное Win32-распознавание; их нужно покрывать отдельным
  Safe Mode/allow-list или UI Automation/TSF адаптером;
- физическая проверка password-поля в Telegram и других приложениях ещё не
  выполнялась.

## Selected-text safety hardening — 2026-09-08

Следующим отдельным шагом закрыт риск ручной коррекции выделения через
Clipboard.

Изменения:

- перед `Ctrl+C` проверяется изменение clipboard sequence number; устаревший
  буфер больше не принимается за выделение;
- исходные clipboard-форматы временно сохраняются и восстанавливаются только
  если буфер не менялся параллельно и все форматы удалось безопасно сохранить;
- неподдерживаемый или слишком большой формат приводит к отказу без
  очистки Clipboard;
- сама замена выделения больше не использует `Ctrl+V` и не перезаписывает
  Clipboard: отправляется один Backspace и одна непрерывная Unicode-батч-
  последовательность;
- операции по-прежнему выполняются только после Core safety policy.

CONFIRMED:

- selected-text/manual correction regression subset: 30/30;
- Platform.Windows Release build: 0 ошибок, 0 предупреждений;
- App/ResidentHost Release rebuild: 0 ошибок, 0 предупреждений;
- после перезапуска resident status: `IsMonitoring=true`.

UNKNOWN:

- физическая замена выделения ещё не проверена в каждом приложении;
- приложения, которые не поддерживают обычный Backspace/Unicode `SendInput`,
  потребуют отдельного UI Automation/TSF адаптера.

## Hook latency micro-hardening — 2026-09-08

Сделан один узкий performance-шаг без изменения порядка событий. На boundary
обычного слова, которое уже подтверждено словарём как корректное в исходном
языке, `LiveLayoutBoundaryGate` больше не запускает повторную синхронную
проверку неверной раскладки. При этом отдельное решение о возможной
орфографической ошибке сохраняется.

Это уменьшает лишнюю работу внутри `WH_KEYBOARD_LL` для нормального набора и
не переносит suppression/replay в неконтролируемый фоновой поток. Кандидаты
неверной раскладки по-прежнему проходят существующие пороги и guard.

CONFIRMED:

- live/boundary/ordering/replay плюс detector/selected-text regression subset:
  39/39;
- App x64 Release: 0 ошибок, 0 предупреждений;
- ResidentHost x64 Release: 0 ошибок, 0 предупреждений;
- после перезапуска resident status: `IsMonitoring=true`.

Следующий performance-шаг, если ручной прогон снова покажет задержку, —
предвычисление bounded-кандидата вне hook с готовым snapshot и fail-open при
неготовности. Его пока не включал, чтобы не менять проверенный порядок
boundary-событий.

## Bounded hook preflight — 2026-09-08

Предвычисление для потенциальной ошибки раскладки теперь включено. При наборе
символов gate запускает отменяемый background-preflight для точного snapshot
текущего токена. На boundary hook-путь принимает только готовый результат,
совпадающий с текущим токеном и поколением буфера.

Правила безопасности:

- если preflight ещё не готов, устарел или завершился с ошибкой, gate работает
  fail-open и не блокирует boundary ради ожидания фоновой задачи;
- результат старого токена не может примениться к новому токену;
- существующие быстрые специальные случаи (служебная запятая и короткий
  внутренний layout-case) остаются синхронными;
- Core spelling-path, Secure Input, Safe Mode, исключения и Double Shift не
  изменялись;
- Rust по-прежнему остаётся только audit/shadow и не вызывает live replacement;
- raw token/context не попадают в диагностические счётчики и отчёты.

CONFIRMED:

- Core live/boundary/ordering/replay, layout, autocorrection, secure-input и
  selected-text regression subset: 104/104;
- App tests: 148/148;
- SmartInput.App x64 Release: 0 ошибок, 0 предупреждений;
- SmartInput.ResidentHost x64 Release: 0 ошибок, 0 предупреждений;
- после перезапуска найден ровно один ResidentHost и один UI-процесс;
- resident status сообщает `IsMonitoring=true`.

LIKELY:

- тяжёлая проверка неверной раскладки больше не должна задерживать сам
  `WH_KEYBOARD_LL` boundary при готовом preflight или при безопасном отказе;
- возможна потеря отдельной layout-коррекции при слишком быстром наборе, если
  preflight не успел завершиться. Это предпочтительнее блокировки ввода и
  повторной печати.

UNKNOWN:

- фактическая субъективная задержка и полнота замен в Telegram после этой
  сборки требуют ручного ввода в Telegram; автоматического native-прогона в
рамках этой проверки нет.

## Atomic local learning persistence — 2026-09-08

Усилено сохранение локального пользовательского состояния. Learning-файл и
пользовательский autocorrect-словарь теперь сериализуются во временный файл с
последующей заменой назначения. Основной файл не обнуляется до завершения
сериализации и flush.

Дополнительно:

- повреждённый или превышающий локальный размерный лимит JSON игнорируется с
  fail-open поведением вместо падения resident-пути;
- временные файлы удаляются после успешной или неуспешной записи;
- сохранения correction-rejection learning сериализуются через один async
  gate, поэтому конкурентные Undo/rejection-события не перетирают друг друга
  устаревшим snapshot;
- локальный словарь по-прежнему хранит необходимые для работы ядра значения
  только локально; диагностические сообщения не содержат token text;
- live replacement, Rust/shadow, Safe Mode, Secure Input и Double Shift не
  изменялись.

CONFIRMED:

- persistence/learning regression subset: 46/46;
- App tests после остановки процессов: 148/148;
- SmartInput.App x64 Release: 0 ошибок, 0 предупреждений;
- SmartInput.ResidentHost x64 Release: 0 ошибок, 0 предупреждений;
- после перезапуска найден ровно один ResidentHost и один UI-процесс;
- resident status сообщает `IsMonitoring=true`.

UNKNOWN:

- аварийное завершение непосредственно между flush и заменой назначения
  требует отдельного fault-injection теста; при штатном процессе обрезанный
  destination-файл больше не создаётся;
- физический live-прогон в Telegram остаётся ручной проверкой.

## Resident user-dictionary hot reload — 2026-09-08

Добавлен отдельный watcher для локального пользовательского autocorrect-
словаря. Когда settings UI атомарно заменяет файл, resident host получает
событие, ждёт завершения записи и загружает новый snapshot без перезапуска
процесса.

После успешной загрузки live token/preflight buffer сбрасывается. Это не даёт
кандидату, рассчитанному на старом словаре, пересечь границу обновления.

Безопасность:

- watcher дебаунсится и не выполняет I/O внутри `WH_KEYBOARD_LL`;
- отменённые/устаревшие reload-запросы не применяются;
- ошибка локального файла не останавливает resident host;
- при остановке host watcher и все pending reload-задачи корректно закрываются;
- raw token text не добавляется в логи;
- Rust/shadow, live replacement, Safe Mode, Secure Input и Double Shift не
  изменялись.

CONFIRMED:

- ResidentHost watcher integration tests: 2/2;
- App tests: 148/148;
- SmartInput.App x64 Release: 0 ошибок, 0 предупреждений;
- SmartInput.ResidentHost x64 Release: 0 ошибок, 0 предупреждений;
- после перезапуска: `HostProcessCount=1`, `UiProcessCount=1`,
  `IsMonitoring=true`.

UNKNOWN:

- физическая скорость обновления после конкретного клика в settings UI не
  измерялась в Telegram; автоматическая проверка использует тот же
  атомарный файловый протокол и реальный `FileSystemWatcher`.

## Concurrent dictionary I/O hardening — 2026-09-08

Закрыт узкий Windows-режим, при котором ResidentHost мог читать локальный
словарь в тот же момент, когда settings UI публиковал новый атомарный файл.
Чтение теперь разрешает `ReadWrite|Delete` sharing: текущий reader сохраняет
стабильный handle, а новый snapshot может быть опубликован без блокировки
замены. Операция публикации также имеет ограниченный retry для краткого lock
от антивируса или индексатора.

Добавлен тест замены файла при удерживаемом reader-handle. Watcher-тесты
проверяют reload и завершение без фоновых reload после Dispose.

CONFIRMED:

- Core persistence subset: 47/47;
- ResidentHost watcher tests: 2/2;
- App tests: 148/148;
- SmartInput.App x64 Release: 0 ошибок, 0 предупреждений;
- SmartInput.ResidentHost x64 Release: 0 ошибок, 0 предупреждений;
- после очистки дубликата запущены ровно один Host и один UI;
- resident status: `IsMonitoring=true`.

UNKNOWN:

- поведение стороннего антивируса при длительном эксклюзивном захвате файла
  всё ещё зависит от его политики; retry намеренно bounded и не блокирует
  keyboard hook.

## User-dictionary input bounds — 2026-09-08

Усилена граница пользовательского словаря. Каждая запись теперь проверяется
до попадания в runtime snapshot:

- допускаются только известные языковые enum-значения;
- частотность должна быть конечным числом в диапазоне `0..1`;
- пустые, control-содержащие и чрезмерно длинные значения отбрасываются;
- snapshot ограничен 8192 уникальными `(language, lookupKey)`-записями;
- при переполнении новая запись отклоняется, а при загрузке сохраняется
  детерминированный ограниченный snapshot;
- casing отображения сохраняется, lookup остаётся case-insensitive;
- user entry продолжает иметь приоритет над built-in frequency, а
  `NeverAutocorrect` продолжает блокировать автоматическое изменение;
- live replacement и Rust не менялись.

CONFIRMED:

- user-dictionary normalization/bounds tests: 27/27;
- App tests: 148/148;
- SmartInput.App x64 Release: 0 ошибок, 0 предупреждений;
- SmartInput.ResidentHost x64 Release: 0 ошибок, 0 предупреждений;
- после перезапуска: один Host, один UI, `IsMonitoring=true`.

LIKELY:

- локальный словарь больше не сможет через malformed entry внести `NaN`,
  бесконечную частотность или неограниченный объём в scoring-path;
- ограничение не влияет на обычный пользовательский словарь и типичные
  бренды/идентификаторы.

UNKNOWN:

- полноценная миграция уже существующего файла с намеренно некорректными
  значениями не требуется: такие записи безопасно пропускаются при reload.

## Block 1 — Full regression/stress run — 2026-09-08

Выполнен полный доступный regression/stress-контур без подключения Rust к
live replacement и без изменения текущего C#-пути:

- `SmartInput.Core.Tests`: 1598 тестов; 1585 passed, 13 failed, 0 skipped;
- `SmartInput.App.Tests`: 148/148 passed;
- `SmartInput.ResidentHost.Tests`: 2/2 passed;
- встроенный `WordProbe`: 121 case, 118 apply, 2 wait, 1 no-change;
- `ScorerAudit` с настоящей Rust DLL и held-out fixture: 95 case;
- native scorer в этом audit-only запуске не получил права на live replacement;
  `liveReplacement=false`, `rawTextLogged=false`.

Тяжёлый mutation/corpus harness обработал 250000 мутаций на каждом из 8
seed-запусков. На всех seed наблюдались одинаковые свойства: recovery rate
около 94.22–94.36%, `wrongCombined=0`, `invalidOracle=0`; acceptance-тесты
не прошли из-за ненулевых ambiguous/wrong-confident случаев, которые их
текущие критерии требуют считать нулевыми. Это нагрузочный baseline, а не
разрешение ослабить safety-gate.

Классификация 13 Core failures:

- 8 — `MaximumCorpusAuditTests` (full/held-out acceptance criteria);
- 4 — seed-42 recovery/ambiguity diagnostic expectations;
- 1 — Non-R1 recovery oracle, где ожидался `Apply`, а текущая политика вернула
  консервативный `Wait`.

CONFIRMED:

- обычные Core/App/ResidentHost regression suites запускаются и завершаются;
- новые watcher/persistence/bounds и live-boundary unit/integration проверки
  не дали новых падений в своих таргетированных наборах;
- failure pattern крупного corpus воспроизводится на разных seed и не является
  единичным случайным сбоем;
- exact known/preserved/protected-контуры в corpus-аудите не дали raw-text
  диагностических утечек; отчёт использует только агрегаты и digest/case-id;
- отсутствие внешнего WordProbe stress-файла зафиксировано: файл с таким
  именем не входит в текущую рабочую папку, поэтому его результат не выдаётся
  за выполненный.

LIKELY:

- текущий главный regression-риск находится в acceptance-критериях
  ambiguous-correction gate, а не в сборке или базовом runtime-пути;
- 13 падений требуют отдельного решения: либо уточнить oracle/критерий,
  либо ужесточить candidate gate; автоматическое повышение числа замен сейчас
  небезопасно.

UNKNOWN:

- физический ввод в Telegram и реальное поведение Win32 hook не проверялись в
  этом блоке и относятся к отдельным этапам;
- RAM/CPU/latency именно ResidentHost не измерялись этим corpus testhost и
  относятся к блоку 3;
- гонки preflight/watcher/Undo/boundary относятся к блоку 2;
- причины пользовательских двойных букв в live replacement нельзя объявить
  закрытыми только по этому офлайн-прогону.

## Block 2 — Concurrent preflight/watcher/Undo/boundary checks — 2026-09-08

Проверен конкурентный контур без подключения Rust к live replacement. Целью
было исключить применение устаревшей операции после нового ввода, повторный
запуск Undo и сброс новой транзакции завершившейся старой фоновой задачей.

Исправления:

- `CorrectionUndoService` получил version-guard транзакции и single-flight
  interlock: второй Undo не запускает второй replacement; после async policy,
  replacement и learning persistence проверяется, что транзакция всё ещё
  актуальна;
- `CorrectionRejectionBackspaceService` теперь не инвалидирует новую запись,
  если старое async-сохранение rejection завершилось позже;
- `LiveLayoutCorrectionCoordinator.ResetBuffer()` сериализован с обработкой
  input через тот же processing gate; внутренние reset-вызовы не создают
  deadlock;
- settings/user-dictionary/learning watchers синхронизируют проверку shutdown
  с регистрацией фоновой задачи, ждут активные reload-задачи при остановке и
  не применяют результат после Dispose; отменённые CTS не закрываются до
  завершения своей задачи;
- завершение reload после shutdown повторно проверяет disposed/cancellation
  перед reset live buffer.

CONFIRMED:

- Core race/boundary targeted suite: 68/68 passed;
- App Double Shift/shutdown targeted suite: 14/14 passed;
- ResidentHost watcher suite: 3/3 passed;
- ResidentHost x64 Release build: 0 ошибок, 0 предупреждений;
- добавленный stale-preflight тест доказал, что результат worker-а после
  `ResetPendingToken` отбрасывается по generation/token guard;
- добавленный in-flight watcher тест доказал, что `DisposeAsync` дожидается
  активной reload-задачи и не выполняет reset после shutdown;
- добавленные Undo/backspace tests доказали отсутствие replacement старой
  транзакции, отсутствие двойного concurrent replacement и сохранение новой
  tracking-транзакции;
- Safe Mode, Secure Input, Double Shift policy, Rust/shadow и live replacement
  в этом блоке не ослаблялись и не подключались.

LIKELY:

- ранее наблюдавшиеся редкие сбои вида «старое async-действие влияет на новый
  ввод» в этих проверенных путях устранены на уровне состояния и жизненного
  цикла задач;
- сериализация внешнего reset с live processing убирает окно, в котором
  watcher мог сбросить буфер посреди boundary correction.

UNKNOWN:

- физический `WH_KEYBOARD_LL`/Telegram-прогон с реальным таймингом ещё не
  выполнен; этот блок проверяет event-модель и lifecycle, но не Windows desktop
  input delivery;
- отсутствие двойных букв и задержка пробела в физическом вводе требуют блока
  3 (метрики) и отдельного реального прогона адаптера;
- fault injection именно в native hook thread и аварийное завершение процесса
  во время reload остаются отдельным hardening-этапом.

## Block 3 — ResidentHost: RAM, CPU и IPC latency — 2026-09-08

Проведён воспроизводимый idle-sample на x64 Release-сборке
`SmartInput.ResidentHost.exe`. В этом замере не генерировались физические
клавиатурные события и не выполнялась live replacement. Для повторения есть
privacy-safe harness:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\tools\ResidentHostMetrics\ResidentHostMetrics.ps1 `
  -ProcessId <ResidentHost-PID> -DurationSeconds 20 `
  -SampleIntervalMs 500 -StatusRequests 20
```

Harness сохраняет только агрегаты: распределения памяти и времени, счётчики
успешных/неуспешных IPC-запросов и privacy-флаги. Содержимое status-пакета и
текстовые данные в результат не выводятся.

CONFIRMED:

- Release ResidentHost запущен с `IsMonitoring=true`; status pipe
  `SmartInput.ResidentRuntimeStatus.v1` подключается успешно;
- idle-sample: 40 samples за 20 431,408 мс;
- Working Set: min 243,871 MiB, average 244,205 MiB, p95 244,531 MiB,
  max 244,566 MiB;
- Private Memory: min 204,113 MiB, average 204,411 MiB, p95/max 204,742 MiB;
- CPU процесса: 0,841% в шкале одного логического ядра, 12 logical processors
  доступны системе;
- 20/20 status IPC-запросов успешны; round-trip: min 0,061 мс,
  average 0,337 мс, p95 0,161 мс, max 5,260 мс;
- текущий idle status не содержит hook events: `HookObserved=0`,
  `CharacterResolved=0`, `BoundaryIntercepted=0`, `DecisionEvaluated=0`,
  `ReplacementAttempted=0`, `ReplacementResult=0`, `BoundaryDelivered=0`;
- privacy harness подтвердил `rawTextLogged=false`,
  `rawStatusPayloadLogged=false`, `windowTitlesLogged=false`;
- целевой compact-core уровень 30–40 MiB не достигнут: текущий Working Set
  примерно в 6,1–8,1 раза выше диапазона, Private Memory — примерно в
  5,1–6,8 раза выше;
- Rust остаётся shadow/audit-only, C# live replacement и `SendInput` этим
  блоком не изменялись.

LIKELY:

- основная причина превышения RAM находится в загруженном managed runtime,
  словарях и провайдерах коррекции, а не в status IPC: IPC быстрый и не создаёт
  заметной нагрузки в idle;
- оптимизация словарного слоя через mmap/FST из блока 6 является обоснованной
  следующей мерой, но её эффект нельзя объявлять измеренным до отдельной
  A/B-сборки;
- низкий idle CPU не доказывает отсутствие задержек во время typing-load:
  callback, dispatch, boundary и replacement должны измеряться на реальных
  событиях отдельно.

UNKNOWN:

- latency физической цепочки `hook observed → character resolved → boundary
  intercepted → policy → decision → replacement → boundary delivered`;
- поведение Telegram и других native-приложений, включая задержку/потерю
  пробела и повторную печать символов;
- фактическая latency `Backspace + Unicode SendInput` и Double Shift в desktop;
- CPU/RAM под реальной нагрузкой клавиатуры: текущий status IPC публикует
  агрегированные stage counters, но не duration metrics;
- среда Computer Use не предоставила trusted RPC для native-окон Windows, поэтому
  физический Telegram-прогон в этом блоке не выдаётся за выполненный.

## Block 4 — Privacy-safe logs и диагностические артефакты — 2026-09-08

Проверен production logging-контур, runtime status IPC, diagnostic buffer и
локальные отчёты тестовых harness-ов. Проверка не ищет и не печатает значения
токенов: она контролирует только контракт полей, вызовы логгера и наличие
чувствительных field-маркеров.

Добавлен повторяемый статический аудит:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\tools\PrivacyAudit\PrivacyAudit.ps1
```

Исправления:

- `InputDiagnosticEvent` больше не может содержать имя процесса, заголовок или
  класс окна: эти свойства всегда возвращают пустое значение;
- diagnostic coordinator больше не получает метаданные активного окна для
  формирования диагностического события;
- `InputDiagnosticEntryViewModel` выводит только timestamp, тип и VK-код;
- `WordProbe` по умолчанию выводит только fingerprint и агрегированный summary;
- synthetic R1/recovery/residual отчёты записывают только case-id, enum-статусы,
  длины и числовые признаки; поля с токенами и кандидатами удалены из файлов и
  stdout.

CONFIRMED:

- статический аудит: 337 source-файлов и 3 tool-файла;
- проверено 90 production logger-вызовов, `unsafeLoggerCalls=0`;
- возможный raw-token console output в tools: `0`;
- `metadataContractRedacted=true`, `statusContractAggregateOnly=true`;
- итог: `privacySafe=true`, `rawTextLogged=false`,
  `rawReplacementLogged=false`, `windowTitlesLogged=false`;
- после запуска текущих writer-ов проверено 26 файлов в локальных diagnostic
  temp-каталогах: 0 чувствительных field-маркеров;
- Core privacy/diagnostic targeted tests: 15/15 passed;
- synthetic failure writer: 1/1 passed;
- residual table writer: 1/1 passed;
- App Release, ResidentHost Release и WordProbe Release собраны без ошибок и
  предупреждений;
- Rust остаётся shadow/audit-only; live replacement и `SendInput` не менялись.

LIKELY:

- production-логи текущего процесса не содержат пользовательский или
  исправленный текст: logger получает только enum-статусы, счётчики,
  длительности, VK-коды и типы действий;
- удаление metadata из diagnostic object исключает случайную утечку через UI или
  будущий сериализатор, даже если вызывающий код ошибочно передаст metadata;
- сообщения исключений остаются техническими событиями; отдельные persistence
  catch-блоки не сериализуют содержимое JSON и не печатают его.

KNOWN LIMITATION:

- два аналитических corpus-теста по-прежнему завершаются собственными
  quality-assert’ами: R1 сообщает `130` сохранённых примеров при счётчике `134`,
  recovery-loss сообщает `TotalAmbiguousApplied=134` при ожидаемом нуле. Это
  дефект/ограничение corpus acceptance, а не privacy-проверки; сами writer-ы
  сформировали безопасные файлы до assert-а.

UNKNOWN:

- внешние системные crash dumps, DebugView/Event Viewer и сторонние логгеры,
  если они подключены пользователем вне приложения;
- старые копии отчётов, созданные до этого патча и лежащие вне текущих
  каталогов writer-ов; текущие 26 файлов проверены и безопасны.
## Block 5 — Финальная Release-сборка и пакет передачи — 2026-09-08

### CONFIRMED

- Release x64 сборка `SmartInput.App`: 0 ошибок, 0 предупреждений.
- Release x64 сборка `SmartInput.ResidentHost`: 0 ошибок, 0 предупреждений.
- Release сборки `WordProbe` и `ScorerAudit`: 0 ошибок, 0 предупреждений.
- После последних изменений прошли `SmartInput.App.Tests` — 148/148, `SmartInput.ResidentHost.Tests` — 3/3 и диагностический Core-набор — 5/5.
- Создан отдельный пакет `artifacts/SmartInput.Core.Release.2026-09-08`.
- Пакет содержит 626 файлов, общий размер 92 950 734 bytes; для каждого файла создан `MANIFEST.sha256.json`.
- В пакет включены исходники `src/` и `tests/` без `bin/`/`obj/`, диагностические tools, ResidentHost Release output и privacy-safe документы.
- Политика границы сохранена: `liveReplacement=false`, Rust — `audit-shadow-only`.

### LIKELY

- Пакет воспроизводимо передаёт текущую проверенную версию ядра, runtime и диагностических средств.
- Отдельные release output можно запускать для локальной проверки без подключения Rust к live replacement.

### UNKNOWN / ОГРАНИЧЕНИЯ

- Физический ввод в Telegram и других native-приложениях этим блоком не подтверждался; ручной real-app прогон остаётся отдельным пунктом проверки.
- ResidentHost всё ещё выше целевого compact-core уровня 30–40 MiB: оптимизация словарей не входит в этот блок и перенесена в Block 6.
- Полный Core-прогон ранее имел 13 quality-gate падений из 1598; они не скрывались и не ослаблялись упаковкой.

### Повторяемая упаковка

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\ReleasePackaging\PackageRelease.ps1
```

Подробное описание содержится в `docs/SMART_INPUT_CORE_RELEASE_HANDOFF_RU.md` внутри пакета.

## Block 6 — mmap/lazy dictionary optimization — 2026-09-08

### CONFIRMED

- `.sidict` теперь загружается в ResidentHost через read-only `MemoryMappedFile`; при обычном exact lookup не создаются managed `byte[]` blob и `int[]` offset table для каждой локали.
- Hunspell `WordList` больше не создаётся при старте. Он остаётся runtime fallback для морфологических запросов, которым не хватило compact one-edit кандидатов.
- Resident DI больше не прогревает тяжёлый SymSpell delete-index. В штатном пути он использует compact word-form provider; SymSpell остаётся lazy fallback для редких distance-two запросов.
- Добавлен тест mmap round-trip и validation: dictionary/external regression наборы прошли 50/50, external provider — 17/17, live external regression — 59/59, representative/integration — 27/27.
- Release x64 ResidentHost после оптимизации собран с 0 ошибок и 0 предупреждений.
- Новый idle-замер на 20 431 ms и 40 samples: Working Set min/avg/p95/max = 83.477/85.050/86.977/87.031 MiB; Private min/avg/p95/max = 47.074/48.859/50.539/50.602 MiB. Status IPC: 20/20 успешных запросов.
- В сравнении с baseline Block 3 (244.205 MiB average Working Set, 204.411 MiB average Private) средний private memory уменьшился примерно на 76.1%, а working set — примерно на 65.2%.
- Privacy flags нового замера: raw text = false, raw status payload = false, window titles = false.

### LIKELY

- Основной startup-overhead для прежнего ResidentHost был связан с eager SymSpell index и eager morphology backend, а не с размером `.sidict` самих по себе; это следует из падения private memory после их defer.
- Для типовых одношаговых исправлений новый путь не требует загрузки тяжёлого backend и должен сохранять низкую задержку после startup.

### UNKNOWN / ОГРАНИЧЕНИЯ

- 30–40 MiB private memory всё ещё не достигнуты: новый средний baseline 48.859 MiB. Оставшийся расход включает .NET runtime, host, hook/tray, static core data и нативные библиотеки; дальнейшее снижение потребует отдельного профилирования, а не ослабления correction/safety правил.
- Полноценный минимальный FST не внедрялся: выбран совместимый mmap поверх уже проверенного sorted UTF-8 SIDICT формата.
- Измерение не является физическим Telegram-прогоном; native real-app ввод и Double Shift остаются отдельной ручной проверкой.
- Один предыдущий параллельный тестовый запуск получил `obj` file-lock; после последовательного повторения функциональные наборы прошли. Это ограничение orchestration/build concurrency, не runtime race.
