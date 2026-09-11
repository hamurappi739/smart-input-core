# SmartInput: независимый технический разбор проблем автоматического исправления

Этот документ предназначен для независимых моделей/разработчиков, которым нужно разобраться в текущих проблемах SmartInput. Его можно передавать другой нейросети целиком как исходный контекст для аудита кода, построения тестов и предложения исправлений.

Важно: это не просьба добавить несколько специальных слов в denylist. Требуется найти системную причину неправильных решений, доказать её тестами и сохранить защитные гарантии.

---

## 1. Кратко о задаче

SmartInput — Windows-приложение на .NET 8/Avalonia, которое работает как локальный помощник ввода. Основные автоматические функции:

1. исправление неверной клавиатурной раскладки RU ↔ EN;
2. орфографическая автокоррекция;
3. расширение пользовательских сниппетов;
4. локальное предсказание следующего слова;
5. ручное исправление выделенного текста;
6. отмена последнего автоматического исправления;
7. системный трей и глобальные горячие клавиши.

Автоматические изменения происходят только на границе слова. Для Windows используется низкоуровневый keyboard hook, bounded in-memory buffers и `SendInput` для замены текста. Ввод пользователя не должен сохраняться как история и не должен попадать в логи.

Текущая проблема: алгоритм иногда исправляет слово в неправильную сторону, иногда пропускает очевидную опечатку, а иногда выбирает правильную словоформу случайно по частотности словаря. На больших корпусных проверках часть неправильных решений всё ещё остаётся, хотя обязательные ручные регрессии уже проходят.

Цель независимого аудита — понять, почему production-решение не совпадает с независимым oracle, и разработать устойчивую схему принятия решения без разрушения уже работающих функций.

---

## 2. Текущий статус проекта

Последний известный статус из работы Cursor:

- Release build пока не считается финально принятым;
- обязательный каталог регрессий: **104/104 passed**;
- unique recovery на seed 42: около **96.74%**;
- `ExactWordsChanged = 0`;
- `WrongCombined = 0`;
- `TotalAmbiguousApplied = 8,661` — неприемлемо, должно быть 0;
- `WrongUniqueTarget = 299` — неприемлемо, должно быть 0;
- `WrongDirectLayout = 1,501` — неприемлемо, должно быть 0;
- brute-force sampled cases: 1,412;
- brute-force vs independent oracle disagreement: 420;
- `ProductionSignatureIndexMiss`: около 44,841;
- accounting/reconciliation: 0 ошибок, 0 неучтённых случаев.

Эти числа означают, что тестовая бухгалтерия уже в основном исправлена, но production safety classifier и независимая классификация мутаций пока используют разные представления допустимых конкурентов.

Нельзя объявлять задачу завершённой при ненулевых `AmbiguousApplied`, `WrongUniqueTarget` или `WrongDirectLayout`, даже если 104/104 ручных теста и весь обычный unit suite зелёные.

---

## 3. История наблюдаемых пользовательских ошибок

Ниже реальные или специально воспроизведённые симптомы. Они важнее красивых локальных unit-тестов.

### 3.1. Ложное исправление правильного русского слова

Ввод пользователя и нежелательный результат:

```text
меня     -> сеня
дать     -> жать
гавно    -> ufdyj
душ      -> lei / lie
начала   -> начало
дает     -> даст
дела     -> дело
видел    -> видик
нас      -> нам
миняй    -> vbyzq
```

Эти слова нельзя автоматически менять только потому, что другая словарная форма имеет большую частотность или похожее расстояние редактирования.

### 3.2. Ложное исправление английского/латинского токена в кириллицу

Пример:

```text
vbyzq -> миняй
```

Здесь `vbyzq` — физическое EN-представление русского `миняй`, поэтому в контексте опечатки допустима combined-коррекция. Но нормальные английские слова и технические имена нельзя переводить в кириллицу автоматически.

### 3.3. Пропуск очевидной опечатки

Ранее пропускались или нестабильно обрабатывались:

```text
превет       -> привет
мирр         -> мир
helo         -> hello
teh          -> the
adn          -> and
будуут       -> будут
напесал      -> написал
исслидование -> исследование
```

Некоторые из этих случаев должны быть исправлены, но не за счёт разрешения агрессивного исправления всех похожих слов.

### 3.4. Неверное разрешение формы слова

Алгоритм не должен решать морфологию только по частотности без контекста:

```text
начала / начало
дает / даёт / даст / дают / дать
писал / писалa? / пишу
нас / нам
все / всё
еще / ещё
```

Если обе формы являются точными нормальными словами, безопасное поведение по умолчанию — оставить исходный текст без изменений, если нет сильного контекстного доказательства.

### 3.5. Проблемы с короткими служебными словами

В английской раскладке русские служебные слова имеют очень короткие физические представления:

```text
z  -> я
f  -> а
d  -> в
b  -> и
c  -> с
r  -> к
j  -> о
e  -> у
ns -> ты
vs -> мы
jy -> он
jyf -> она
yt -> не
yf -> на
gj -> по
pf -> за
jn -> от
lj -> до
bp -> из
```

Одиночные буквы нельзя исправлять общим скоринговым алгоритмом. Для них разрешён только явный небольшой whitelist на границе слова. Срабатывание посреди слова запрещено.

---

## 4. Обязательные положительные примеры

Эти случаи должны быть проверены в unit-тестах, интеграционных тестах полного pipeline и при ручной проверке в Notepad.

### 4.1. Неверная раскладка и combined correction

```text
ghbdtn       -> привет
руддщ        -> hello
мущ          -> veo
мущ 3        -> veo 3
пзг          -> gpu
миняй        -> меняй
vbyzq        -> меняй
gtie         -> пишу
```

### 4.2. Русские орфографические опечатки

```text
превет       -> привет
деелай       -> делай
машиина      -> машина
говноо       -> говно
будуут       -> будут
напесал      -> написал
исслидование -> исследование
посянить     -> пояснить
спецеально   -> специально
испровляет   -> исправляет
обстаят      -> обстоят
допалнение   -> дополнение
дууш         -> душ
меняй        -> меняй
```

### 4.3. Английские орфографические опечатки

```text
helo  -> hello
teh   -> the
adn   -> and
```

### 4.4. Короткие служебные слова

Проверять именно на границе слова и с сохранением пробела:

```text
z   -> я
f   -> а
d   -> в
b   -> и
c   -> с
r   -> к
j   -> о
e   -> у
ns  -> ты
vs  -> мы
jy  -> он
jyf -> она
yt  -> не
yf  -> на
gj  -> по
pf  -> за
jn  -> от
lj  -> до
bp  -> из
```

После успешной EN → RU замены активная раскладка должна переключаться на русскую, если это предусмотрено текущей архитектурой. Двойной Shift должен отменить замену и вернуть исходную форму вместе с пробелом.

---

## 5. Обязательные отрицательные примеры

Эти слова и конструкции должны оставаться без изменений при автоматической обработке.

### 5.1. Нормальные русские словоформы

```text
начала
начало
дает
даёт
даст
дают
дать
жать
дал
дела
дело
видел
видик
нас
нам
меня
сеня
неизвестное
неизвестно
гавно
говно
душ
пишу
все
всё
еще
ещё
работает
красивыми
собакой
```

### 5.2. Нормальные английские слова

```text
hello
world
the
and
help
test
receive
because
their
```

### 5.3. Защищённые токены

Без изменений:

```text
https://example.com
http://localhost:8080
user@example.com
C:\\Users\\Test\\file.txt
/usr/local/bin/tool
snake_case
kebab-case
camelCase
PascalCase
GitHub
VSCode
Cursor
SmartInput
npm
dotnet
gpu
veo
```

Важно: `мущ` и `пзг` являются специальным консервативным путём неизвестных коротких названий и могут быть исправлены в `veo`/`gpu`, но уже точные `veo` и `gpu` должны оставаться без изменений.

### 5.4. Смешанные и числовые токены

```text
helloмир
мирhello
abc123
123
v2
model_3
```

Исключение: цифры после пробела не должны запрещать исправление предыдущего отдельного слова:

```text
мущ 3 -> veo 3
```

---

## 6. Архитектура pipeline

### 6.1. Наблюдение ввода

`WindowsInputMonitor` получает `WH_KEYBOARD_LL` события на dedicated STA thread. KeyDown и KeyUp обрабатываются раздельно. События с `LLKHF_INJECTED` или с маркером SmartInput не должны запускать новый цикл исправления.

На genuine KeyDown:

1. проверяется policy и текущий foreground application;
2. обновляются bounded buffers;
3. на boundary запускается correction pipeline;
4. при необходимости boundary KeyDown подавляется;
5. после завершения операции boundary доставляется ровно один раз как injected KeyDown/KeyUp pair.

### 6.2. Буферы

Есть несколько логических буферов:

- letter/token buffer для layout и spelling;
- snippet trigger buffer для printable trigger characters (`/`, `@`, `_`, `-`, буквы, цифры и т.п.);
- prediction context buffer.

Буферы bounded и хранятся только в памяти. Смена приложения, focus change, policy block, emergency pause, uncertain/reset и отключение функций очищают состояние.

### 6.3. Решение на границе слова

Текущий общий pipeline задуман так:

```text
boundary reached
    |
    +-- Protection/policy/feature gates
    |
    +-- snippet exact match (если включён)
    |
    +-- layout evaluation
    |
    +-- spelling evaluation
    |
    +-- combined in-memory candidate (layout -> bounded spelling)
    |
    +-- выбрать не более одной внешней замены
    |
    +-- finally deliver boundary exactly once
```

Сейчас используется `JointCorrectionDecisionService`, который должен сравнивать как минимум следующие варианты:

1. `NoChange` — оставить исходный токен;
2. spelling correction в текущем языке;
3. direct layout conversion;
4. combined layout + одна spelling correction.

Ключевое требование: решения должны сравниваться **до** внешней замены. Нельзя сначала заменить раскладку, а потом повторно запускать spelling на уже изменённом тексте, если это приводит к цепочке ошибочных замен.

### 6.4. Suppress-and-reinject

Для детерминированного порядка boundary:

1. synchronous gate в hook решает, можно ли подавить boundary;
2. при положительном решении genuine boundary KeyDown возвращает `1` и не передаётся приложению;
3. matching physical KeyUp также подавляется через pairing tracker;
4. async pipeline завершает correction или timeout;
5. `WindowsBoundaryKeyDeliveryService` доставляет boundary один раз через `SendInput` с маркером SmartInput.

При unsafe/failure/abort/timeout boundary всё равно должен быть доставлен один раз. Пользовательский ввод во время replacement должен отменить replacement, но не потерять свою клавишу.

---

## 7. Safety policy, которую нельзя ослаблять

Состояния policy:

| Состояние | Автоматические операции | Ручные внешние операции |
|---|---:|---:|
| Allowed | разрешены | разрешены |
| SafeMode | запрещены | разрешены |
| BlockedApplication | запрещены | запрещены |
| SecureInput | запрещены | запрещены |
| UnknownContext | запрещены | запрещены |
| EmergencyPause | запрещены | запрещены |

Приоритет:

1. Emergency pause;
2. Protection disabled;
3. Secure input Active;
4. Secure input Unknown или неизвестное foreground context;
5. excluded application;
6. Safe Mode process/window class;
7. Allowed.

Safe Mode разрешает ручные Fix Layout/Fix Spelling/Fix Text, но не разрешает live automatic correction. Secure/Unknown/Excluded/Emergency всегда блокируют внешнюю замену.

Автоматическое исправление не должно работать в:

- Cursor;
- VS Code;
- Visual Studio;
- JetBrains IDE;
- Windows Terminal, PowerShell, cmd, conhost;
- terminals, RDP, games и нестандартных controls;
- secure/password fields;
- user-configured excluded applications;
- неизвестных foreground contexts.

Нельзя решать corpus-проблемы удалением этих защит.

---

## 8. Словари и данные

В проекте есть:

- starter RU/EN lexicon;
- `ru_50k` и bloom-like membership для русских словоформ;
- `CompositeAutocorrectDictionary`;
- пользовательский словарь с частотой и флагом `NeverAutocorrect`;
- локальное rejection learning после успешной отмены;
- небольшой starter prediction model.

Важное различие:

```text
Contains(word) != высокая частотность(word)
```

Точное наличие слова в словаре должно защищать его от known→known автоматической подмены. Частотность может участвовать в выборе кандидата для неизвестной опечатки, но не должна сама по себе разрешать замену правильного слова на другую словарную форму.

Отдельно учитывается эквивалентность `е/ё` только для membership-проверок. Она не должна автоматически превращать `дает` в `даёт` или `все` в `всё` без явного сильного контекста.

Пользовательский словарь усиливает уверенность, но неизвестное короткое название не должно требовать ручного добавления, если оно проходит консервативный physical-layout path.

---

## 9. Текущая проблема corpus oracle vs production

### 9.1. Независимый oracle

Corpus verifier классифицирует сгенерированную мутацию исходного правильного слова. Для каждой мутации известны:

- исходное слово;
- правильный target;
- тип мутации;
- edit provenance;
- однозначность или неоднозначность восстановления;
- защищённость/точное наличие;
- ожидаемое решение: Apply или Wait.

Типы мутаций включают:

- repeated accidental character;
- adjacent key substitution;
- adjacent transposition;
- missing character;
- extra character;
- vowel substitution;
- general substitution;
- layout conversion;
- combined layout + spelling.

### 9.2. Production classifier

Production использует `ProductionCandidateSafetyClassifier`, `CandidateAmbiguityIndex`, operation-specific gates и signature indexes. Он должен отвечать на вопрос: «можно ли безопасно применять конкретного кандидата к реальному token?»

Сейчас production и независимый verifier часто смотрят на разные множества конкурентов. Отсюда возникает:

```text
Oracle: Ambiguous / MustWait
Production: Candidate / Apply
```

В отчёте это классифицируется как `OracleProductionDisagreement`.

### 9.3. Главные текущие кластеры

По последним отчётам:

```text
AdjacentKeySubstitution      много CrossOp competitor misses
ExtraCharacter               много CrossOp competitor misses
AdjacentTransposition        CrossOp и отдельные layout/combined ошибки
RepeatedAccidentalCharacter  около десятков ложных Apply
GeneralSubstitution           несовпадение brute-force и oracle
```

`ProductionSignatureIndexMiss` около 44k означает, что production index не находит часть минимально допустимых конкурентов, которые видит verifier. Это не мелкая статистическая погрешность, а вероятная системная причина разрешённых ambiguous applies.

### 9.4. Требуемый порядок исправления

Не следует сначала подкручивать глобальный confidence threshold. Нужно:

1. сделать brute-force source set и independent oracle идентичными;
2. устранить все BF↔oracle disagreements;
3. унифицировать edit provenance и operation classification;
4. обеспечить полное покрытие production signature index для всех operation classes;
5. закрыть CrossOperationCompetitorMissing;
6. запретить Apply на oracle-equivalent ambiguous cases;
7. отдельно обработать direct layout и unknown-name path;
8. повторить seed 42, held-out seeds и обязательные регрессии.

Нельзя делать production classifier зависимым от harness oracle: oracle должен оставаться независимым проверяющим механизмом.

---

## 10. Acceptance gates

Работа считается завершённой только после выполнения всех условий:

1. `ExactWordsChanged = 0` по полному exact RU/EN corpus;
2. `WrongCombined = 0`;
3. `WrongUniqueTarget = 0`;
4. `WrongDirectLayout = 0`;
5. `TotalAmbiguousApplied = 0`;
6. BF vs independent oracle disagreement = 0;
7. `ProductionSignatureIndexMiss = 0` или каждый residual miss доказан как недостижимый и отдельно учтён;
8. unique recovery не ниже 95%, желательно не ниже 99% после устранения индекса;
9. все 104 mandatory assertions проходят индивидуально;
10. full Core + App suite проходит;
11. минимум 250,000 deterministic mutations на seed 42 и held-out seeds;
12. stress: не менее 100,000 KeyDown/KeyUp, 25,000 boundaries, focus/policy/emergency transitions;
13. минимум 30 application identities через safety policy;
14. boundary KeyDown/KeyUp suppression и reinjection не теряют и не дублируют клавиши;
15. Double Shift и Backspace rejection learning остаются рабочими;
16. logs/diagnostic DTOs не содержат token text, candidate, context или replacement text;
17. Release build и fresh executable запускаются после остановки только старого SmartInput с тем же путём.

Обязательное reconciliation:

```text
OracleUniquelyRecoverable
  = CorrectRecovery + WrongUniqueTarget + WaitedUnique

OracleAmbiguous
  = AmbiguousSafelyWaited
  + AmbiguousSpellingApplied
  + AmbiguousLayoutApplied
  + AmbiguousCombinedApplied
  + other explicitly classified terminals

TotalMutations
  = Unique + Ambiguous + ExactKnown + Invalid/Protected
```

Все равенства должны быть точными. Нельзя скрывать случаи в агрегате `other`, если для них нет явной причины.

---

## 11. Mandatory regression catalog

В каталоге должно быть 104 отдельных assertion, а не только 104 входа в один параметризованный тест.

Группы:

- A01–A32: positive applies;
- C01–C03: capitalization;
- E01–E32: exact-word preserves;
- D01–D26: double-letter preserves;
- L01–L06: layout leak rejects;
- U01–U05: unknown-name cases.

Минимально проверить, что каталог явно включает:

```text
ghbdtn -> привет
руддщ -> hello
превет -> привет
helo -> hello
teh -> the
adn -> and
мущ -> veo
пзг -> gpu
миняй -> меняй
vbyzq -> меняй
gtie -> пишу
деелай -> делай
машиина -> машина
говноо -> говно
будуут -> будут
```

И preservation cases:

```text
начала, начало, дает, даёт, даст, дают, дать, жать,
дал, дела, дело, видел, видик, нас, нам, меня, сеня,
неизвестное, неизвестно, гавно, говно, душ, пишу,
все, всё, еще, ещё
```

---

## 12. Что именно нужно проверить в коде

### 12.1. Порядок и границы

- Где формируется token?
- Boundary обрабатывается до или после вставки пробела?
- Не запускается ли spelling над уже layout-converted token?
- Может ли injected boundary запустить новый cycle?
- Не очищается ли buffer раньше, чем async pipeline его прочитает?
- Не возникает ли гонка при focus change?

### 12.2. Семантика словаря

- Используется ли `Contains` как защита exact word?
- Не превращается ли низкая частотность правильного слова в «неизвестное»?
- Не смешаны ли lexical membership, plausibility и frequency?
- Не считается ли `е/ё` разрешением на замену?
- Не переопределяет ли starter lexicon пользовательское слово с `NeverAutocorrect`?

### 12.3. Происхождение кандидата

- Один ли и тот же edit provenance используется генератором, oracle и production classifier?
- Полностью ли генерируются general substitutions?
- Учитывается ли кандидат, найденный другим operation class?
- Не отбрасывается ли конкурент из-за лимита или порядка генерации?
- Стабильны ли tie-break правила?

### 12.4. Ambiguity

- Что именно означает `Ambiguous`?
- Проверяется ли ambiguity до Apply, а не только при диагностике?
- Есть ли разница между одинаковыми score и одинаковыми target?
- Не разрешается ли candidate из-за словаря, когда другой plausible candidate должен дать Wait?
- Есть ли явная политика: ambiguous = no external replacement?

### 12.5. Layout

- Проверяется ли exact target-language membership?
- Защищаются ли нормальные русские слова от перевода в латиницу?
- Отдельно ли обрабатывается unknown-name path (`мущ→veo`, `пзг→gpu`)?
- Не позволяет ли `y` считаться достаточной английской гласной в длинном случайном токене?
- Не превращается ли нормальная латиница в кириллицу?

### 12.6. Live pipeline

- Проходит ли решение через реальный hook → buffer → boundary → replacement путь?
- Все ли unit tests вызывают только isolated `Evaluate`?
- Имеется ли тест с реальными KeyDown/KeyUp sequence?
- Сохраняется ли пробел после замены?
- Работает ли Double Shift для layout, spelling и combined?
- Отменяет ли genuine concurrent KeyDown replacement, не теряя пользовательский ввод?

---

## 13. Корпусный и fuzz-аудит

Нужны несколько независимых наборов:

### 13.1. Exact corpus

Взять все доступные точные RU/EN entries и проверить:

- исходное слово не меняется;
- capitalization сохраняется;
- `е/ё` остаётся как введено;
- known→known substitutions отсутствуют.

### 13.2. Deterministic mutations

Для каждого слова генерировать bounded mutations всех типов. Каждая mutation должна хранить provenance и expected target. Использовать seed 42, 1337 и 20260903.

### 13.3. Negative mutations

Генерировать случаи, где:

- два исправления одинаково правдоподобны;
- исходник уже является другим нормальным словом;
- физическая раскладка случайна, но похожа на техническое имя;
- одна правка приводит к известному слову, а другая — к equally plausible слову;
- токен слишком короткий или содержит цифры/символы.

Ожидаемое решение для этих случаев обычно `Wait` или `NoChange`, а не Apply.

### 13.4. Independent implementation

Verifier должен быть независим от production service. Допустимо использовать общие неизменяемые данные (алфавиты/physical map), но нельзя вызывать production classifier для построения ground truth.

---

## 14. Тесты приложений и безопасности

Проверить минимум такие identity:

```text
Notepad
WordPad
Microsoft Word
Chrome
Edge
Firefox
Discord
Telegram
Slack
Teams
Cursor
Code
devenv
idea64
rider64
webstorm64
WindowsTerminal
wt
powershell
pwsh
cmd
conhost
mintty
putty
mstsc
UnityWndClass
UnrealWindow
SDL_app
CEFCLIENT
```

Матрица должна включать:

- Protection on/off;
- Automatic Layout on/off;
- Autocorrect on/off;
- Snippets on/off;
- Prediction on/off;
- Emergency Pause;
- SecureInput Active/Inactive/Unknown;
- foreground known/unknown;
- SafeMode;
- excluded app;
- active replacement session;
- focus change between token and boundary.

Для каждого случая проверить, что:

- автоматические операции запускаются только в `Allowed`;
- SafeMode запрещает live automation, но разрешает ручные внешние Fix actions;
- secure/unknown/excluded/emergency всегда блокируют замену;
- неизвестное приложение не получает автоматическое исправление;
- prediction overlay скрывается в blocked context;
- Tab/Esc не крадутся, если условия gate не выполнены.

---

## 15. Privacy audit

Запрещено писать в логи, метрики, notification text или diagnostic DTO:

- исходный token;
- исправленный token;
- список кандидатов;
- prediction context;
- replacement text;
- surrounding sentence;
- clipboard contents;
- историю набора.

Разрешены только:

- counts;
- durations;
- enum action/status;
- queue depth;
- process/window metadata там, где это уже предусмотрено диагностикой и не содержит текста ввода.

Нужно отдельно проверить исключения, format strings и debug logging. Тест должен искать реальные значения тестовых слов в captured logs/status objects.

---

## 16. Что не делать

1. Не добавлять тысячи специальных слов в denylist вместо исправления алгоритма.
2. Не отключать все исправления ради нулевого WrongConfident.
3. Не ослаблять Safe Mode, secure-input или excluded-app guards.
4. Не подключать internet dictionary/downloads.
5. Не копировать и не декомпилировать Caramba Switcher.
6. Не использовать production oracle как independent ground truth.
7. Не менять `e/ё` автоматически только из-за membership.
8. Не считать зелёный unit suite доказательством корректности live hook pipeline.
9. Не принимать отчёт с ненулевыми ambiguous applies как complete.
10. Не запускать одновременно несколько Release экземпляров: lock на `SmartInput.exe` и DLL создаёт ложные build failures.
11. Не создавать git commit без отдельной просьбы пользователя.

---

## 17. Рекомендуемый план независимого расследования

### Шаг 1. Воспроизвести минимальные ошибки

Запустить только Core decision tests для:

```text
меня, дать, гавно, душ, начала, начало, дает, даёт,
миняй, vbyzq, мущ, пзг, gtie, превет, helo, teh, adn
```

Для каждого сохранить не текст лога, а структурированный trace:

- source classification;
- candidate list size;
- candidate operation;
- source dictionary membership;
- target membership;
- plausibility;
- ambiguity;
- final action.

### Шаг 2. Сравнить oracle и production на одном токене

Для каждого disagreement вывести только безопасный идентификатор case id и enum-поля. Сопоставить:

- набор кандидатов;
- provenance;
- min edit band;
- operation class;
- reason for Apply/Wait.

### Шаг 3. Исправить генерацию и индексы

Сначала сделать полными source sets для verifier и production index. Проверить, что лимит 512 кандидатов не отбрасывает нужных конкурентов до проверки ambiguity. Если bounded limit необходим, доказать, что он order-independent.

### Шаг 4. Ввести жёсткую final apply gate

Перед любой внешней заменой должна быть единая проверка:

```text
if exact source word -> NoChange
if protected/mixed/URL/path/identifier -> NoChange
if policy != Allowed -> NoChange/Wait
if independent-equivalent ambiguity -> Wait
if target is not uniquely supported -> Wait
otherwise Apply
```

Эта gate не должна вызывать UI, логировать текст или менять safety policy.

### Шаг 5. Запустить полный аудит

Последовательность:

1. BF↔oracle = 0;
2. production signature misses = 0;
3. seed 42 canonical audit;
4. seeds 1337 и 20260903;
5. operation cluster tests;
6. mandatory 104;
7. stress and application matrix;
8. Core/App tests;
9. Release build;
10. fresh launch and manual Notepad verification.

---

## 18. Какой ответ требуется от независимой нейросети

Нужно вернуть не общие советы, а техническое заключение в таком формате:

1. **Root cause candidates** — максимум 3–5 конкретных причин с указанием файлов/классов/методов.
2. **Evidence** — какие тесты/метрики подтверждают каждую причину.
3. **Minimal safe fix** — какое изменение в архитектуре нужно сделать.
4. **Why it will not regress** — какие инварианты сохраняются.
5. **New tests** — точные тестовые случаи, включая русские слова из этого документа.
6. **Oracle alignment plan** — как убрать BF↔oracle disagreement без зависимости oracle от production.
7. **Acceptance report** — таблица всех gates до/после.
8. **Uncertainty** — что осталось недоказанным.

Если предлагается изменение порога, обязательно показать trade-off:

- сколько mandatory positives теряется;
- сколько exact words начинает меняться;
- сколько ambiguous cases остаётся;
- что происходит с unknown-name cases `мущ→veo` и `пзг→gpu`;
- что происходит с `z→я`, `ns→ты`, `jyf→она`.

---

## 19. Минимальная ручная проверка после исправлений

1. Закрыть все старые SmartInput-процессы, затем запустить свежий Release exe.
2. Включить Protection.
3. Включить Automatic Layout и Autocorrect.
4. В Notepad проверить по одному слову с пробелом:

```text
ghbdtn  -> привет
руддщ   -> hello
мущ     -> veo
миняй   -> меняй
vbyzq   -> меняй
gtie    -> пишу
превет  -> привет
helo    -> hello
teh     -> the
adn     -> and
```

5. Проверить, что без изменений остаются:

```text
начала, начало, дает, даёт, даст, дают, дать, жать,
дал, дела, дело, видел, видик, нас, нам, меня, сеня,
неизвестное, неизвестно, гавно, говно, душ, пишу,
все, всё, еще, ещё
```

6. После успешной коррекции дважды нажать Shift: исходное слово и пробел должны вернуться.
7. Повторить отмену одной пары два раза и проверить rejection learning.
8. Проверить Safe Mode/terminal/Cursor/password field: автоматической замены нет.
9. Проверить URL, email, path, identifier и mixed-script: изменений нет.
10. Проверить, что обычный пробел срабатывает с первого раза и не дублируется.

---

## 20. Итоговая формулировка проблемы

SmartInput уже имеет большую архитектуру, safety layer, live boundary pipeline, undo, dictionaries, layout conversion, autocorrection, snippets, prediction, tray и hotkeys. Главная нерешённая проблема не в отсутствии ещё одного словаря. Она в том, что production-классификатор кандидатов и независимый corpus oracle используют несовпадающие множества кандидатов и разные правила edit provenance/ambiguity.

Из-за этого приложение одновременно:

- иногда меняет правильные русские слова (`дать→жать`, `меня→сеня`, `гавно→ufdyj`);
- иногда пропускает явные опечатки (`превет`, `будуут`, `напесал`);
- иногда выбирает одну из нескольких нормальных словоформ (`начала/начало`, `дает/даёт/даст`);
- иногда корректно восстанавливает неизвестные физические названия (`мущ→veo`), но этот путь нельзя превратить в агрессивный layout переводчик;
- показывает тысячи ambiguous applies на большом корпусе, несмотря на зелёные обязательные регрессии.

Правильное решение должно быть corpus-driven, operation-aware, deterministic и conservative: точные слова защищены, неоднозначные случаи ждут, уникальные доказанные опечатки исправляются, а внешняя замена выполняется максимум один раз и только в разрешённом контексте.

Финальный критерий успеха — не количество тестов и не красивый отчёт, а доказанное выполнение acceptance gates на независимом oracle, полном production pipeline и ручной проверке в реальных Windows-приложениях.

