# Финальные вопросы по Caramba Engine Handoff

## Контекст

Передан полный пакет `SmartInput-Engine-Handoff-2026-09-06`. Не нужно заново
исследовать уже подтверждённые нижние слои. Требуется закрыть оставшиеся
доказательные пробелы и подготовить безопасную схему подключения к SmartInput.

Права на собственный проект и предоставленные recovery-артефакты имеются.
Recovered Caramba/KBM-правила пока нельзя подключать к live-автозамене без
отдельного audit/regression gate.

## Уже подтверждено — не выдавай это за открытый вопрос

1. KBM DAWG работает по маршруту:

   `prepared UTF-8 bytes → terminal outputId → indexed text pool → candidate`.

2. Для текущего KBM-артефакта доказана биекция `70 007 outputId ↔ 70 007`
   записей text pool.

3. Подтверждены marker routes `raw`, `prefix_02`, `suffix_03`,
   `wrapped_02_03`; marker является частью ключа и не должен выбираться
   перебором в live-режиме.

4. Подтверждено нижнее выполнение:

   `Action → type dispatch → INPUT[0x28] → SendInput → count check`.

5. Подтверждены имена нескольких Action: `fix_mistyped_layout_action`,
   `change_last_word_layout`, `change_selection_layout`,
   `switch_layout_action`, `paste_text_action`, `restore_hook_action`.

6. В SmartInput уже есть переносимый фасад:

   `IPortableCorrectionEngine.Evaluate(PortableCorrectionRequest)`
   возвращает только `apply`, `wait` или `no_change`.

7. Live-применение SmartInput должно идти через общий safety policy,
   `SafeTextReplacementService`, `CorrectionUndoService` и Double Shift Undo.

## Главный вопрос

Восстанови недостающий верхний producer и докажи цепочку:

```text
keyboard event / prepared token
  → normalization
  → active language pair
  → marker route / boundary mode
  → KBM/layout candidate
  → feature + user + app + model context
  → score / classifier / policy
  → no Action | text Action | other Action
  → existing replacement transaction
```

Нужны не общие рассуждения, а конкретные функции, структуры, offsets,
call-sites, условия ветвления и уровень доказательности каждого утверждения.

## Вопросы A — preprocessor и выбор route

1. Где и как из Windows keyboard events формируется `prepared` buffer?
2. Какая точная нормализация выполняется до DAWG: регистр, Unicode NFC/NFD,
   `ё/е`, пробелы, punctuation, boundary markers, dead keys, surrogate pairs?
3. Как выбирается `raw`, `prefix_02`, `suffix_03` или `wrapped_02_03`?
4. Что означают режимы A/B и как они связаны с forward/reverse matcher?
5. Как определяется активная language pair (`ru-en`, `ru-de`, и т. п.)?
6. Какие события являются границей слова и какие из них подавляются во время
   replacement transaction?

Для каждого ответа дай synthetic trace минимум на:

```text
ghbdtn → привет       (layout route, не KBM spelling route)
руддщ → hello        (layout обратного направления)
commersant → kommersant
infact → in fact
Акcенов → Аксёнов
```

## Вопросы B — classifier и decision policy

1. Найди producer, который получает candidate и решает: создать Action или
   ничего не делать.
2. Укажи точные места, где читаются:
   `auto.enabled`, `ai.enabled`, `AutocorrectEnabled`, `YofikatorEnabled`,
   `SingleshiftEnabled`, `SplitshiftEnabled`.
3. Найди consumer для `app_exceptions`, `user_rules`, `special_behaviors`,
   `switch_to_en_on_focus` и объясни семантику поля `enabled`.
4. Установи порядок gates:
   feature off, invalid pair, protected token, known word, case-sensitive,
   double-caps, ё/ёfikator, app exception, user rule, ambiguity, score.
5. Найди реальное значение score/threshold, либо чётко докажи, что его нельзя
   получить из имеющихся артефактов.
6. Покажи, чем различаются `no_action`, `wait`, text Action и non-text Action.
7. Объясни, как выбирается конкретный Action для layout, autocorrect,
   yofikator и manual/selection correction.

Если точный classifier не восстанавливается статически, предложи audit-only
telemetry для debug build: какие enum/reason/feature IDs записывать, где ставить
точки наблюдения и как не записывать исходный текст, candidate или replacement.

## Вопросы C — структуры Action, replacement и Undo

1. Восстанови поля всех text-related Action: source length, replacement bytes,
   target HKL, selection/last-word mode, retry/delay flags.
2. Свяжи producer каждого Action с callback `void(const Action*)` и его веткой
   type-dispatch.
3. Найди, где создаётся Undo state и как он инвалидируется после настоящего
   пользовательского ввода.
4. Установи, есть ли в Caramba отдельный rollback при частичном `SendInput`.
5. Сравни это с SmartInput:
   `SafeTextReplacementService`, `CorrectionUndoService`,
   `DoubleShiftUndoCoordinator`.
6. Составь безопасный адаптер без прямого вызова `SendInput` из decoder-а или
   нейросети.

## Вопросы D — подключение к SmartInput

Подготовь конкретную архитектуру интеграции:

```text
ICarambaPreprocessor
  → ICarambaCandidateProvider
  → ICarambaDecisionAdapter
  → IPortableCorrectionEngine
  → SafetyPolicyEvaluator
  → SafeTextReplacementService
  → CorrectionUndoService
```

Нужно указать:

1. какие интерфейсы добавить в Core/Infrastructure;
2. какие классы зарегистрировать в DI;
3. где хранить и валидировать SHA-256/версию KBM;
4. как разделить Windows layout provider и KBM spelling provider;
5. как включать режимы `audit-only`, `preview`, `allow-list`, `live`;
6. как выполнить мгновенный rollback к текущему SmartInput engine;
7. как не допустить двойного применения (старый engine + recovered provider);
8. как сохранить Safe Mode, secure fields, URL/path/code guards,
   excluded apps, Protection OFF и Double Shift Undo.

Не предлагай вторую независимую safety policy и не подключай raw recovered
rules напрямую к live replacement.

## Вопросы E — acceptance и тесты

Составь воспроизводимую матрицу тестов с ожидаемым решением и reason code:

### Положительные случаи

```text
ghbdtn → привет
руддщ → hello
превет → привет
helo → hello
teh → the
adn → and
commersant → kommersant
infact → in fact
```

### Обязательное сохранение

```text
начала, начало, дать, даст, дают, дает, даёт,
меня, нас, дело, видел, жизнь, машина,
URL, email, path, identifier, code, mixed-script
```

### Безопасность

```text
Safe Mode
terminal / IDE / excluded app
secure/password field
Protection OFF
Emergency Pause
focus change during replacement
partial SendInput failure
Double Shift undo with trailing space/punctuation
```

Тесты должны проверять не только target text, но и:

- выбранный route;
- outputId bounds и model hash;
- decision (`apply/wait/no_change`);
- reason code;
- отсутствие raw текста в диагностике;
- ровно одну replacement transaction;
- корректный Undo.

## Формат требуемого ответа

Верни отдельный отчёт со следующими разделами:

1. `CONFIRMED` — доказано конкретными call-sites/байтами/тестами;
2. `LIKELY` — сильная гипотеза с объяснением;
3. `UNKNOWN` — чего нет в артефактах;
4. полный call graph producer → Action → execution;
5. таблица runtime-структур и полей;
6. точный контракт preprocessor/decoder/classifier;
7. DI-карту для SmartInput;
8. staged migration plan с rollback;
9. acceptance matrix и команды тестирования;
10. список файлов/артефактов, которые ещё нужны.

Критически важно:

- не называть эвристический score SmartInput оригинальным score Caramba;
- не считать outputId доказательством права на замену;
- не применять `wait`;
- не превращать `no_change` в автоматический fallback;
- не логировать исходные слова, candidate или replacement;
- явно отметить каждый вывод, который нельзя доказать статически.

Итогом должно быть не «ещё один общий анализ», а решение: что уже можно
подключить в audit/preview, что можно включить через allow-list, а что требует
matching source/PDB или собственной debug telemetry.
