# Запрос на архитектурное решение: Caramba, собственный движок или гибрид

## Задача

Изучи папку `SmartInput-Engine-Handoff-2026-09-06` и текущий проект SmartInput.
Нужно принять практическое решение, как продолжать разработку без бесконечной
расшифровки неизвестного classifier-а Caramba.

Главный вопрос:

> Что лучше для готового продукта: продолжать восстанавливать Caramba 1:1,
> заменить часть движка собственными проверенными компонентами или сделать
> гибрид?

## Текущее состояние SmartInput

Уже работает и покрыто тестами:

- Windows RU↔EN layout conversion;
- spelling/autocorrection pipeline;
- Hunspell/SymSpell adapters;
- punctuation spacing и локальный punctuation preview;
- Safe Mode, secure fields, URL/email/path/code guards;
- app exclusions и Protection OFF;
- Double Shift Undo;
- `SafeTextReplacementService` и `CorrectionUndoService`;
- `IPortableCorrectionEngine` с решениями `apply/wait/no_change`;
- privacy-safe diagnostics без raw token/candidate logs.

В текущем handoff уже добавлен exact audit-provider:

```text
prepared bytes → KBM DAWG → outputId → indexed text pool → candidate
```

Проверены все 70 007 canonical witness-ключей. Provider пока не участвует в
live replacement и по умолчанию заменён в DI на `NullKbmCandidateProvider`.

## Что остаётся неизвестным у Caramba

Не доказаны по исходникам/PDB/debug telemetry:

1. точная нормализация input до matcher;
2. выбор marker route из boundary/preprocessor;
3. raw поля `AutocorrectRule`;
4. score/threshold и порядок classifier gates;
5. семантика `app_exceptions` и `user_rules`;
6. payload text-related `Action`;
7. точный Caramba Undo и rollback;
8. полное соответствие KBM candidate → Action/no Action.

Нижний dispatch и `SendInput` уже доказаны, но этого недостаточно для
безопасного 1:1 live-подключения.

## Сравни три варианта

### Вариант A — продолжать Caramba 1:1

Оставить recovered KBM/DAWG центром движка и искать matching source/PDB,
producer и classifier telemetry.

Оцени:

- вероятность реально достичь 1:1;
- необходимые артефакты и время;
- юридические/лицензионные риски;
- риск зависания проекта на reverse engineering;
- влияние на память и запуск;
- возможность безопасного rollback.

### Вариант B — полностью собственный engine

Использовать прозрачную собственную архитектуру:

```text
Windows HKL layout provider
Hunspell/SymSpell spelling provider
local language/context model
explicit safety policy
SafeTextReplacementService
CorrectionUndoService
```

Оцени:

- точность RU/EN layout;
- точность spelling, повторных букв, пропусков, перестановок и `ё`;
- размер словарей и память;
- скорость boundary decision;
- объяснимость и тестируемость;
- риск regressions и ложных замен.

### Вариант C — гибрид

Предполагаемая схема:

```text
Windows layout provider → live layout correction
Hunspell/SymSpell/local rules → live spelling correction
KBM provider → audit/preview и ограниченный allow-list
Caramba recovered rules → только после доказанной policy
```

Оцени:

- как избежать двойного применения;
- как выбрать единственного apply owner;
- как синхронизировать score/ambiguity;
- как не раздувать память;
- как организовать staged migration и rollback;
- какие случаи оставить за текущим SmartInput.

## Обязательная рекомендация

Выбери один вариант как основной и объясни решение по 10-балльной шкале:

| Критерий | Вес |
| --- | ---: |
| точность исправлений | 25 |
| отсутствие ложных замен | 20 |
| безопасность и privacy | 15 |
| память и скорость | 10 |
| простота поддержки | 10 |
| независимость от Caramba | 10 |
| возможность Undo/rollback | 5 |
| расширяемость пунктуации | 5 |

Не выбирай Caramba только потому, что она «готовая». Учитывай, что её
classifier и policy пока не восстановлены доказательно.

## Если рекомендуешь гибрид или собственный engine

Дай конкретный план:

1. какие текущие классы SmartInput оставить;
2. какие классы считать устаревшими;
3. какие словари подключить и в каком формате;
4. как уменьшить текущие memory peaks;
5. как исправить остаточные ambiguous/layout corpus failures;
6. какие интерфейсы и DI-регистрации нужны;
7. как провести shadow/audit comparison с KBM;
8. как включать feature flags без перезапуска или с безопасным rollback;
9. какие тесты считать acceptance gate;
10. какие функции можно выпускать пользователю уже сейчас.

## Обязательные тестовые группы

Проверь план на таких группах:

```text
ghbdtn → привет
руддщ → hello
мущ → veo
превет → привет
стрвнно → странно
здрвствуйте → здравствуйте
мирр → мир
helo → hello
teh → the
adn → and
давать/дать/даст/дают/дает/даёт
начала/начало
URL, email, path, identifier, code, secure field
Safe Mode, excluded app, terminal, IDE
Double Shift Undo
```

Проверяй не только target, но и отсутствие двойного replacement, сохранение
пробела/пунктуации, переключение раскладки, memory budget и отсутствие raw
текста в диагностике.

## Формат ответа

Верни:

1. итоговый выбор `A`, `B` или `C`;
2. таблицу оценки по критериям;
3. обоснование на основе файлов handoff, а не предположений;
4. целевую архитектуру и call graph;
5. migration plan по этапам;
6. план тестирования и acceptance gates;
7. список того, что прекратить исследовать;
8. список того, что обязательно исследовать дальше;
9. список конкретных файлов, которые надо изменить;
10. команду запуска и проверки.

Если данных недостаточно, явно напиши `UNKNOWN` и укажи минимальный следующий
эксперимент. Не предлагай подключать KBM к live replacement только на основании
того, что DAWG возвращает корректный `outputId` и target text.
