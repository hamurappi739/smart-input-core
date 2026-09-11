# SmartInput Maximum Corpus, Application, Pipeline, and Stress Audit

## Mission

Perform the largest practical offline correctness audit of SmartInput's automatic layout correction and spelling correction. Reproduce every failure, discover additional failures automatically, fix their systemic causes, and rerun the complete audit until the acceptance criteria are satisfied.

This is not a request to add a few hard-coded examples. Build a reusable corpus-driven test harness that checks tens of thousands of real words, hundreds of thousands of deterministic mutations, known-to-known collisions, full live pipeline behavior, application safety policy, keyboard boundary pairing, undo, performance, and privacy.

Work in the current shared project directory. Preserve all existing user and Codex changes. Do not use `git reset`, `git checkout`, or any destructive cleanup. Do not create a commit.

Do not copy or decompile proprietary Caramba Switcher resources. Use only SmartInput's existing local resources and appropriately licensed or already bundled data. Do not add network calls.

## Non-negotiable acceptance criteria

The work is complete only when all of the following are true:

1. Every exact known Russian and English word in the bundled lexicons is audited.
2. Exact known words produce zero automatic known-to-known substitutions.
3. Russian words and valid Russian word forms produce zero false conversions to Latin layout.
4. English words produce zero false conversions to Cyrillic layout, except the explicitly approved short service-word whitelist.
5. At least 250,000 deterministic typo/layout/combined mutation cases are evaluated.
6. Every failure is classified, and systemic failures are fixed rather than merely added to a denylist.
7. The real boundary pipeline is tested, not only isolated `Evaluate()` methods.
8. At least 30 application identities and relevant window classes are tested through the safety policy.
9. Boundary KeyDown/KeyUp pairing, one-time reinjection, concurrent typing, timeouts, and Double Shift undo are stress-tested.
10. Production logs and diagnostic DTOs contain no typed tokens, candidate words, context strings, or replacement strings.
11. The full existing test suite remains green.
12. A Release build succeeds and a fresh Release executable is launched after stopping only the old SmartInput process that locks its DLL files.

## Newly observed real failures

Reproduce these failures before changing the implementation.

### Russian typo incorrectly converted to Latin

The user typed:

```text
миняй
```

SmartInput produced:

```text
vbyzq
```

Expected:

```text
миняй → меняй
```

The Latin layout candidate `vbyzq` is not a plausible English word and must never defeat the strong Russian spelling candidate `меняй`.

Also support the reverse combined case:

```text
vbyzq → миняй → меняй
```

Only one external replacement is permitted:

```text
vbyzq → меняй
```

### Correct Russian form incorrectly replaced

These replacements are incorrect:

```text
неизвестное → неизвестно
начала → начало
дает → даст
дать → жать
дал → лал
видел → видик
дела → дело
нас → нам
меня → сеня
```

Each source is either a valid word, a valid inflected form, or a normal `е` spelling of a word commonly written with `ё`. Without a grammar model with strong sentence-level evidence, SmartInput must preserve the exact source form.

### Obvious Russian typos missed

These intentionally misspelled inputs must be investigated:

```text
деелай → делай
машиина → машина
говноо → говно
меняяй → меняй
дериись → дерись
пениис → пенис
будуут → будут
напесал → написал
исслидование → исследование
посянить → пояснить
спецеально → специально
испровляет → исправляет
обстаят → обстоят
допалнение → дополнение
дууш → душ
```

### Combined layout and spelling

The English-layout sequence:

```text
gtie
```

physically converts to:

```text
пешу
```

and should then resolve to:

```text
пишу
```

The application must perform one final replacement:

```text
gtie → пишу
```

## Phase 1: Establish an evidence-producing audit harness

Create a reusable audit layer in the test project or a dedicated local test utility. It must produce aggregate reports without changing production logging.

The audit report must include:

- total tokens evaluated;
- totals by language;
- exact known words preserved;
- exact known words changed;
- direct layout candidates accepted/rejected;
- combined candidates accepted/rejected;
- known-to-known collision count;
- correct mutation recoveries;
- ambiguous mutations safely ignored;
- wrong confident corrections;
- maximum generated candidate count;
- average, p95, p99, and maximum evaluation duration;
- deterministic random seed;
- test corpus fingerprint or resource version;
- failures before fixes;
- failures after fixes.

Never silently skip malformed entries. Count and explain skipped corpus lines.

Do not print the entire word corpus to ordinary test logs. A failure may display the specific failing token because it is controlled test data, but production application logs must remain text-free.

Use separate categories where useful:

```text
FastRegression
FullCorpusAudit
MutationAudit
LivePipeline
ApplicationPolicy
Stress
Privacy
```

The full audit may be opt-in for normal development if it is expensive, but it must be executed during this task and its exact commands and results must be reported.

## Phase 2: Exact-word preservation audit

Enumerate every entry from:

- the complete bundled Russian lexicon;
- the complete bundled English lexicon;
- all enumerable Russian word-form resources;
- the explicit high-frequency starter words;
- the colloquial/profanity protection list;
- a deterministic test user dictionary;
- the `NeverAutocorrect` test dictionary.

For every exact word, test:

1. spelling evaluation in its own language;
2. direct layout evaluation;
3. combined layout plus spelling evaluation;
4. joint correction decision;
5. decision with no context;
6. decision with same-language context;
7. lower case;
8. initial upper case;
9. all caps where meaningful;
10. boundary variants: space, comma, period, exclamation mark, question mark.

Expected invariant:

```text
Exact strong known word → NoChange
```

Frequency must not allow one exact known word to be replaced by another exact known word.

Separate dictionary membership from ranking confidence:

- membership protects an exact source word;
- frequency ranks candidates only when the source is not known;
- user dictionary and `NeverAutocorrect` are absolute;
- a rare corpus entry may be weaker evidence for cross-layout ambiguity, but it still must not be replaced by another same-language word.

## Phase 3: Exhaustive known-to-known collision audit

Build indexed collision sets for every pair of known words related by:

- one character substitution;
- adjacent keyboard substitution;
- one character insertion;
- one character deletion;
- adjacent transposition;
- repeated-character insertion/removal;
- `е/ё` variation;
- physical RU/EN keyboard conversion.

Do not compare every word with every other word using an unbounded O(n²) loop. Build deletion signatures, substitution patterns, transposition signatures, normalized forms, and layout-conversion indexes.

For every collision where the source itself is a known word, assert `NoChange`.

Mandatory collision families:

```text
дать / жать
дал / лал
дела / дело
нас / нам
меня / сеня
видел / видик
начала / начало
неизвестное / неизвестно
стала / стало
была / было
слова / слово
места / место
том / дом
код / кот
пить / жить
бить / быть
стать / знать
```

Before adding an expected known-to-known assertion, verify actual dictionary membership. Report how many collision pairs were found and how many produced an incorrect automatic substitution before and after the fix.

## Phase 4: Large Russian morphology regression corpus

Add table-driven and corpus-driven tests for correct Russian forms. All listed forms must remain unchanged.

### `начало` and `начать`

```text
начало
начала
началу
началом
начале
начал
начала
начали
начать
начинаю
начинаешь
начинает
начинаем
начинаете
начинают
начинал
начинала
начинали
начиная
начав
```

### `дать` and `давать`

```text
дать
дам
дашь
даст
дадим
дадите
дадут
дал
дала
дало
дали
давать
даю
даешь
даёшь
дает
даёт
даем
даём
даете
даёте
дают
давал
давала
давали
давай
давайте
```

### `писать`

```text
писать
пишу
пишешь
пишет
пишем
пишете
пишут
писал
писала
писало
писали
написать
написал
написала
написали
написано
написанный
```

### `видеть`

```text
видеть
вижу
видишь
видит
видим
видите
видят
видел
видела
видело
видели
увидеть
увидел
увидела
увидели
```

### `менять`

```text
менять
меняю
меняешь
меняет
меняем
меняете
меняют
менял
меняла
меняли
меняй
меняйте
поменять
поменял
изменять
изменил
```

### `дело`

```text
дело
дела
делу
делом
деле
дел
делами
делах
```

### Common nouns with potentially colliding forms

```text
слово
слова
слову
словом
слове
слов
словами
словах
место
места
месту
местом
месте
мест
местами
местах
окно
окна
окну
окном
окне
окон
время
времени
временем
времена
имя
имени
именем
имена
путь
пути
путём
путем
путями
```

### Pronouns

```text
я
меня
мне
мной
мною
ты
тебя
тебе
тобой
он
его
ему
ним
она
её
ее
ей
нею
мы
нас
нам
нами
вы
вас
вам
вами
они
их
им
ими
себя
себе
собой
кто
кого
кому
кем
что
чего
чему
чем
```

### Function words and short words

```text
в
с
к
у
о
а
и
но
не
ни
на
по
за
из
от
до
для
при
про
без
под
над
как
так
там
тут
где
когда
если
или
либо
тоже
также
уже
ещё
еще
```

### Adjective gender, number, and case families

```text
новый
новая
новое
новые
нового
новой
новому
новым
новыми
хороший
хорошая
хорошее
хорошие
хорошего
хорошей
хорошему
хорошим
русский
русская
русское
русские
русского
русской
русскому
русским
неизвестный
неизвестная
неизвестное
неизвестные
неизвестного
неизвестной
неизвестному
неизвестным
неизвестно
```

### Frequent verbs

```text
быть
есть
был
была
было
были
буду
будешь
будет
будем
будете
будут
работать
работаю
работает
работают
делать
делаю
делаешь
делает
делают
сказать
скажу
скажешь
скажет
скажут
говорить
говорю
говоришь
говорит
говорят
знать
знаю
знаешь
знает
знают
ждать
жду
ждешь
ждёшь
ждет
ждёт
ждут
идти
иду
идешь
идёшь
идет
идёт
идут
```

Programmatically extend these examples from all morphological data already available in the repository. The explicit lists are regression anchors, not the full corpus.

## Phase 5: `е/ё` equivalence audit

Russian users commonly omit `ё`. For dictionary membership and source-word protection, treat `е` and `ё` variants as evidence of validity. Do not automatically rewrite the user's chosen character unless a separate explicit feature is introduced.

Both forms must be preserved:

```text
дает / даёт
идет / идёт
ждет / ждёт
берет / берёт
пойдет / пойдёт
придет / придёт
найдет / найдёт
живет / живёт
живем / живём
поет / поёт
еще / ещё
ее / её
все / всё
серьезный / серьёзный
```

Special warning:

```text
все
всё
```

have different meanings. Never automatically replace one with the other.

Generate additional `е/ё` variants from all dictionary words containing either character and assert that a valid source is not changed to an unrelated word.

## Phase 6: Russian typo mutation audit

Generate deterministic mutations from known Russian words. Use a fixed seed and include every eligible word, or a documented stratified sample if a full generated set is too large.

Mutation classes:

1. one repeated character;
2. one deleted character;
3. one inserted keyboard-neighbor character;
4. one adjacent-key substitution;
5. one arbitrary vowel substitution;
6. adjacent transposition;
7. `е/и`, `а/о`, `е/ё` confusion;
8. one repeated character plus one safe substitution;
9. physical layout conversion;
10. physical layout conversion plus one spelling error.

At least 125,000 Russian mutation cases must be evaluated.

The expected policy is:

- if there is exactly one strong known target, correct it;
- if multiple known targets are plausible, return `Wait` or `NoChange`;
- never confidently choose the wrong known target;
- never prefer meaningless Latin output over a strong Russian spelling correction.

Mandatory positive typo anchors:

```text
деелай → делай
машиина → машина
говноо → говно
меняяй → меняй
дериись → дерись
пениис → пенис
будуут → будут
напесал → написал
исслидование → исследование
посянить → пояснить
спецеально → специально
испровляет → исправляет
обстаят → обстоят
допалнение → дополнение
дууш → душ
миняй → меняй
превет → привет
```

Mandatory legitimate-double-letter anchors that must remain unchanged:

```text
касса
ванна
группа
класс
суббота
Россия
Алла
Анна
тонна
сумма
комиссия
профессия
территория
искусство
рассказ
программа
```

## Phase 7: English exact-word and mutation audit

Perform the same exhaustive checks for the complete English lexicon.

At least 125,000 English mutation cases must be evaluated.

Regression anchors:

```text
helo → hello
teh → the
adn → and
recieve → receive
becuase → because
thier → their
```

Valid words with double letters must remain unchanged:

```text
hello
letter
coffee
class
address
success
necessary
parallel
application
correct
```

Protect exact valid English words from same-language known-to-known substitution.

## Phase 8: Full layout conversion corpus

Use the actual `KeyboardLayoutConverter` to generate physical keyboard-layout counterparts for the complete RU and EN corpora. Do not manually maintain thousands of layout strings.

For each generated pair, record:

- source membership and source frequency;
- converted membership and frequency;
- source plausibility;
- converted plausibility;
- context language;
- selected action;
- confidence and decision margin.

Mandatory layout anchors:

```text
ghbdtn → привет
руддщ → hello
nen → тут
vtyzq → меняй
vbyzq → меняй
gtie → пишу
gbie → пишу
мущ → veo
пзг → gpu
```

Mandatory non-conversions:

```text
миняй must not become vbyzq
гавно must not become ufdyj
говно must not become ujdyj
дууш must not become leei
душ must not become lei
меня must not become vtyz
```

## Phase 9: Strict unknown-name evaluation

Audit `UnknownLatinNameLayoutEvaluator` independently and inside the joint decision.

An unknown Latin candidate must require multiple independent signals:

- at least one normal vowel from `a/e/i/o/u`;
- `y` alone is not a sufficient vowel;
- no impossible consonant cluster;
- bounded length;
- strong improvement in target-language plausibility;
- no exact source-language word;
- no strong source-language spelling candidate;
- sufficient score margin;
- no user rejection for the pair.

Negative unknown-name examples:

```text
vbyzq
ufdyj
leei
vtyz
plhfdcndeqnt
```

Positive short technical-name anchors that must remain supported if they still satisfy the conservative rules:

```text
мущ → veo
пзг → gpu
```

Run a generated unknown-token audit and measure the false-positive rate. Do not lower safety merely to keep two hand-picked examples working.

## Phase 10: Context-sensitive language evidence

If the current implementation has or adds a bounded context buffer, it must be local and ephemeral:

- last 3–8 completed tokens;
- no more than 256 characters;
- memory only;
- clear on focus/app change;
- clear on blocked policy;
- clear on emergency pause;
- clear when Protection is disabled;
- never log or persist context text;
- do not collect context in secure, unknown, excluded, IDE, terminal, or game contexts.

Test sentence-level language evidence with at least the following phrases:

```text
для начала
это только начало
она начала работать
работа уже началась
он дает ответ
он даёт ответ
он даст ответ
они дают ответ
как обстоят дела
у нас всё хорошо
у нас все хорошо
он меня видит
теперь меняй слово
я сейчас nen
он уже nen
мы были nen
теперь я тут
я написал текст
он писал письмо
это неизвестное слово
мне это неизвестно
```

Expected output:

```text
для начала
это только начало
она начала работать
работа уже началась
он дает ответ
он даёт ответ
он даст ответ
они дают ответ
как обстоят дела
у нас всё хорошо
у нас все хорошо
он меня видит
теперь меняй слово
я сейчас тут
он уже тут
мы были тут
теперь я тут
я написал текст
он писал письмо
это неизвестное слово
мне это неизвестно
```

Do not implement aggressive grammar rewriting. Context should help language/layout selection and preserve valid forms. It must not guess between two valid grammatical forms unless a future grammar feature is explicitly designed and enabled.

## Phase 11: Capitalization and punctuation matrix

Run every key regression in:

- lowercase;
- Title case;
- ALL CAPS;
- after a period;
- after a comma;
- inside parentheses;
- inside quotes;
- before `!` and `?`;
- followed by Space, Enter, Tab, comma, period, semicolon, colon, exclamation, and question mark where supported.

Examples:

```text
превет → привет
Превет → Привет
ПРЕВЕТ → ПРИВЕТ
миняй → меняй
Миняй → Меняй
МИНЯЙ → МЕНЯЙ
```

Punctuation and whitespace must be preserved exactly. A boundary must be delivered exactly once.

## Phase 12: Colloquial and profanity preservation

Treat colloquial and profane words as normal user text. Do not censor them and do not convert them to Latin merely because an academic corpus omits them.

Audit at least these anchors and their existing listed inflections:

```text
гавно
говно
говнюк
бля
блядь
блять
млять
сука
сучка
хуй
хуя
хуйню
хуйней
хуёвый
хуевый
пизда
пиздец
пиздато
ебать
ёбать
ебаный
ёбаный
еблан
долбоеб
долбоёб
мудак
мудила
мудацкий
нахуй
нахуя
похуй
заебал
заебало
заебись
уебок
уёбок
срать
насрать
обосраться
жопа
жопу
жопой
чмо
мразь
тварь
шлюха
дебил
идиот
придурок
урод
пенис
```

Exact forms remain unchanged. Obvious single accidental repetition may be corrected only to a unique protected word:

```text
говноо → говно
пениис → пенис
```

## Phase 13: User dictionary and learning matrix

Test every decision class with:

- no user entry;
- ordinary user dictionary entry;
- `NeverAutocorrect` entry;
- one rejection;
- two rejections;
- `AllowAgain` after rejection;
- rejection learned through Double Shift;
- rejection learned through Backspace/retyping;
- app restart and persistence reload.

Assertions:

- user entries override bundled frequency;
- `NeverAutocorrect` is absolute;
- two rejections block only the exact correction pair and kind;
- manual correction remains available;
- no surrounding sentence or typing history is persisted.

## Phase 14: Full live input pipeline

Create integration tests that cover the real sequence:

```text
physical KeyDown
→ Windows character resolution or faithful platform fake
→ preflight buffer
→ live token/context buffers
→ boundary gate
→ boundary KeyDown suppression
→ physical KeyUp pairing
→ joint decision
→ safe replacement session
→ one external replacement
→ marked injected events filtered
→ boundary reinjected once
→ undo transaction recorded
```

Do not claim live correctness based only on `AutocorrectionService.Evaluate()`.

For all mandatory regression anchors, test at least Space. For a representative subset, test punctuation, Tab, and Enter according to supported product behavior.

Assertions:

- one correction at most per boundary;
- one external replacement at most;
- one boundary delivery exactly;
- matching physical KeyUp suppression exactly once;
- unrelated KeyUp passes through;
- injected KeyDown/KeyUp does not trigger correction;
- genuine concurrent KeyDown aborts safely;
- timeout restores the boundary;
- failed policy recheck restores the boundary;
- focus change clears pending state;
- no stuck Space, Tab, Enter, or punctuation key;
- no duplicated spaces;
- no missing spaces.

## Phase 15: Double Shift undo stress

Double Shift must undo all correction types:

- spelling;
- repeated-character correction;
- direct layout;
- combined layout plus spelling;
- service-word whitelist;
- unknown-name correction;
- snippet expansion if currently designed as undoable.

Mandatory cases:

```text
миняй → меняй → Double Shift → миняй
vbyzq → меняй → Double Shift → vbyzq
деелай → делай → Double Shift → деелай
будуут → будут → Double Shift → будуут
напесал → написал → Double Shift → напесал
дууш → душ → Double Shift → дууш
gtie → пишу → Double Shift → gtie
ghbdtn → привет → Double Shift → ghbdtn
мущ → veo → Double Shift → мущ
```

Preserve the trailing boundary. Restore the previous input language when automatic correction changed it.

Stress scenarios:

- left Shift twice;
- right Shift twice;
- alternating left/right Shift if supported;
- key repeat while holding Shift;
- slow second press outside the timeout;
- intervening character;
- intervening mouse/focus change;
- no available undo;
- repeated Double Shift after transaction consumption;
- concurrent correction and undo;
- emergency pause between correction and undo.

## Phase 16: Application compatibility and policy matrix

Add automated policy/integration tests using mocked process names, executable names, window classes, security state, elevation state, and caret availability.

### Normal editing applications: automation expected when supported

Test identities for at least:

```text
notepad.exe
winword.exe
excel.exe
powerpnt.exe
outlook.exe
olk.exe
soffice.bin
wordpad.exe
chrome.exe
msedge.exe
firefox.exe
opera.exe
browser.exe
telegram.exe
Discord.exe
WhatsApp.exe
slack.exe
ms-teams.exe
Teams.exe
thunderbird.exe
obsidian.exe
notion.exe
zoom.exe
```

For browser processes, include modeled scenarios for:

- ordinary text input;
- textarea;
- contenteditable;
- Google Docs-like editor;
- webmail compose field;
- address bar;
- password field;
- developer tools.

Address bars, password fields, developer tools, and unknown caret contexts must fail safely.

### Safe Mode applications: automatic modification forbidden

Test identities for:

```text
Cursor.exe
Code.exe
devenv.exe
rider64.exe
idea64.exe
pycharm64.exe
webstorm64.exe
clion64.exe
goland64.exe
phpstorm64.exe
rubymine64.exe
fleet.exe
WindowsTerminal.exe
wt.exe
powershell.exe
pwsh.exe
cmd.exe
conhost.exe
mintty.exe
putty.exe
WindowsSandbox.exe
mstsc.exe
```

### Games and custom input windows: automatic modification forbidden or fail-safe

Test window classes and representative processes:

```text
UnityWndClass
UnrealWindow
SDL_app
Valve001
CEFCLIENT
Minecraft
javaw.exe
cs2.exe
valorant.exe
FortniteClient-Win64-Shipping.exe
```

Do not require these applications to be installed for automated tests. Test the policy using deterministic fakes. If any are installed, optionally perform manual validation without modifying external application data beyond typed test text.

### Security states

For every application category, cross-test:

- Allowed;
- SafeMode;
- BlockedApplication;
- SecureInput;
- UnknownContext;
- EmergencyPause;
- Protection disabled;
- elevated target inaccessible;
- no foreground window;
- no caret;
- mismatched caret window handle.

Build a parameterized matrix of at least 30 application identities × relevant policy states × enabled feature combinations.

## Phase 17: Feature interaction matrix

Cross-test these feature flags:

```text
Protection
Automatic Layout
Autocorrect
Snippets
Prediction
Emergency Pause
```

Important combinations:

- all off;
- Protection only;
- layout only;
- autocorrect only;
- layout + autocorrect;
- snippets + layout + autocorrect;
- prediction with corrections;
- emergency pause with every combination;
- Protection toggled during a pending token;
- feature toggled during a suppressed boundary;
- feature toggled during replacement.

Verify priority and one-action behavior:

```text
explicit snippet
→ source spelling/layout joint decision
→ prediction metadata
→ boundary delivery
```

No injected replacement may recursively trigger a snippet, correction, or prediction cycle.

## Phase 18: Keyboard event stress and fuzz

Generate deterministic keyboard event sequences covering:

- rapid typing;
- very slow typing;
- auto-repeat;
- alternating KeyDown/KeyUp;
- missing simulated KeyUp;
- unmatched KeyUp;
- held Space;
- held Shift;
- Backspace bursts;
- navigation keys;
- Ctrl/Alt/Win combinations;
- IME/unresolved events;
- dead keys;
- punctuation;
- focus changes between characters;
- focus changes while boundary is suppressed;
- application shutdown during replacement;
- replacement timeout;
- concurrent genuine typing during replacement;
- injected input during replacement;
- repeated enable/disable cycles.

Minimum generated workload:

- 100,000 character events;
- 25,000 word boundaries;
- 10,000 focus/policy transitions;
- 10,000 Double Shift timing sequences;
- multiple fixed seeds, including `42`, `1337`, and `20260903`.

Assertions:

- no deadlock;
- no unbounded queue growth;
- no stuck boundary key;
- no duplicate boundary;
- no double replacement;
- no stale undo transaction;
- no correction in blocked contexts;
- deterministic final state for the same seed.

## Phase 19: Performance audit

Measure Core decision performance separately from Windows injection timing.

Report:

- token evaluation average;
- p50, p95, p99, maximum;
- candidate generation counts;
- joint-decision counts;
- context lookup cost;
- full corpus audit duration;
- mutation audit duration;
- event stress duration;
- peak queue depth;
- allocation observations if practical.

Do not perform a full dictionary scan on every typed word. Use precomputed indexes, signatures, bounded generation, early exits, and strict candidate limits.

Performance tests must avoid flaky unrealistic one-millisecond assertions. Use generous regression budgets and report measured values.

## Phase 20: Privacy audit

Reflect over or explicitly inspect all production status models, logs, diagnostics, notification messages, and persisted JSON models.

Assert that they do not expose:

- original token;
- corrected token;
- sentence context;
- prediction text;
- selected text;
- window title when prohibited by the current privacy policy;
- typing history.

Allowed diagnostic data:

- counts;
- durations;
- enum outcomes;
- queue sizes;
- process metadata already explicitly permitted by the existing diagnostic feature.

Test that user dictionaries, snippets, and rejection learning persist only their explicitly designed data. Do not introduce new text persistence for context or audit runtime data.

## Phase 21: Review every visible feature before packaging

Do not create an installer yet.

Audit the status of every visible UI feature:

```text
Protection
Emergency Pause
Automatic Layout
Autocorrect
Capitalization
Punctuation
Prediction
Prediction Tab acceptance
Prediction Esc dismissal
Snippets
Manual selected-text correction
Global hotkeys
Double Shift undo
My Dictionary
Rejection learning
Excluded applications
Safe Mode
Start with Windows
System tray
Close to tray
Advanced diagnostics
```

Produce a table:

```text
Feature | Fully functional | Partially functional | Placeholder | Automated coverage | Manual coverage | Problems
```

Do not silently leave an enabled-looking UI control as a placeholder. If capitalization, punctuation, or Start with Windows is still not implemented, clearly identify it and recommend the next implementation phase.

## Required regression suites

### Must correct

```text
миняй → меняй
vbyzq → меняй
деелай → делай
машиина → машина
говноо → говно
меняяй → меняй
дериись → дерись
пениис → пенис
будуут → будут
напесал → написал
исслидование → исследование
посянить → пояснить
спецеально → специально
испровляет → исправляет
обстаят → обстоят
допалнение → дополнение
дууш → душ
превет → привет
helo → hello
teh → the
adn → and
gtie → пишу
ghbdtn → привет
руддщ → hello
nen → тут
мущ → veo
пзг → gpu
```

### Must never change automatically

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
меняй
делай
машина
дерись
пенис
будут
написал
исследование
пояснить
специально
исправляет
обстоят
дополнение
все
всё
еще
ещё
```

## Execution order

1. Run the current complete test suite and save the baseline counts.
2. Add focused regression tests that reproduce every newly observed failure.
3. Build the reusable corpus/collision/mutation audit harness.
4. Run the audits before fixes and record aggregate failure counts.
5. Inspect the actual failure clusters and identify systemic causes.
6. Fix the decision model, dictionary semantics, normalization, indexes, or pipeline as appropriate.
7. Do not solve broad clusters only through individual denylist entries.
8. Rerun focused regression tests.
9. Rerun exact-word audits.
10. Rerun collision audits.
11. Rerun RU and EN mutation audits.
12. Rerun layout and combined audits.
13. Run live pipeline integration tests.
14. Run application/policy matrix tests.
15. Run keyboard stress tests with all required seeds.
16. Run privacy tests.
17. Run the full existing Core and App suite.
18. Run the complete corpus/fuzz suite at least three times.
19. Stop only the running SmartInput process that locks the Release output.
20. Build the entire solution in Release.
21. Launch the fresh Release executable.
22. Prepare a concise manual Notepad and application compatibility checklist.

## Final report requirements

The final report must include exact numbers, not phrases such as “many tests passed.”

Report:

1. Root cause of `миняй → vbyzq`.
2. Root cause of `начала → начало`.
3. Root cause of `дает → даст`.
4. Root cause of `дать → жать`.
5. Root cause of `неизвестное → неизвестно`.
6. Changes made to exact-word protection.
7. Changes made to `е/ё` handling.
8. Changes made to unknown-name scoring.
9. Changes made to source spelling versus layout priority.
10. Total RU exact words audited.
11. Total EN exact words audited.
12. Total word-form cases audited.
13. Total known-to-known collision pairs found.
14. Total Russian mutations evaluated.
15. Total English mutations evaluated.
16. Total layout pairs evaluated.
17. Total combined layout-plus-spelling cases evaluated.
18. Total event sequences evaluated.
19. Total application/policy combinations evaluated.
20. False corrections before and after.
21. Wrong confident mutation recoveries before and after.
22. Ambiguous cases safely left unchanged.
23. Average, p95, p99, and maximum evaluation times.
24. Full Core/App test totals.
25. Release build result.
26. Fresh executable launch result.
27. Full visible-feature status table.
28. Remaining limitations and the recommended next functional phase.

Do not declare success if only the hand-written regression examples pass. The primary success condition is broad corpus evidence, zero known-word substitutions, zero wrong confident mutation recoveries, complete live-pipeline safety, and deterministic stress results.
