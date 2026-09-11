# Запрос к независимой нейросети: расшифровка таблиц правил Caramba

## Цель

Восстановить доказанную цепочку:

```text
input bytes → automaton transition → outputId → rule record → decision
```

Пока разрешён только `audit-only`: никаких keyboard hooks, `SendInput` и
автоматического изменения текста.

## Материалы

Пакет находится здесь:

```text
C:\Users\shuly\Desktop\CarambaSwitcher-Recovery-Package-2026-09-05\
```

Основные файлы:

```text
model\latest-rules-decoded.bin
integration\dawg\lexicon-a.dawg
integration\dawg\lexicon-b.dawg
integration\rules\group-2-field-3.jsonl
integration\rules\group-2-field-4.jsonl
integration\rules\group-5-field-2.jsonl
integration\rules\group-5-field-3.jsonl
integration\rules\group-3-field-3.jsonl
binaries\CarambaSwitcherPro.exe
```

## Уже доказано

Оба автомата используют записи `u32 little-endian` и переход:

```text
base = (records[state] >> 10) << (8 if records[state] & 0x200 else 0)
candidate = state XOR label_byte XOR base
valid = candidate в границах
        AND (records[candidate] & 0x800000FF) == label_byte
```

Для состояния с флагом `0x100` исходный код вычисляет output-link:

```text
output_index = current_state XOR base(record)
output_id = records[output_index] & 0x7FFFFFFF
```

Подтверждённые связи:

- Automaton A использует output-ID из диапазона примерно 67 значений;
- эти ID совпадают со значениями первого поля 67 записей `top.2.field.3`;
- Automaton B использует output-ID `0..72728`;
- они соответствуют индексам записей `top.5.field.2`;
- `top.5.field.3` содержит около 2426 записей другой внутренней формы.

`0x100` не является признаком конца слова. `outputId` не является словом,
заменой или confidence.

## Что нужно исследовать

### 1. Формат записей правил

Для каждой таблицы установи:

- wire/protobuf-структуру, если она есть;
- типы и порядок полей;
- signed/unsigned varint;
- length-delimited поля;
- повторяющиеся поля;
- ссылки на другие записи;
- допустимые диапазоны значений;
- различия между 67, 72 729 и 2 426 записями.

Нельзя называть поле «заменой», «языком», «весом» или «ошибкой», пока это
не подтверждено несколькими независимыми примерами.

### 2. Связь outputId с автоматом

Для обоих блоков:

- перечисли все состояния с output-link;
- вычисли outputId по подтверждённой формуле;
- проверь, что ID попадает в соответствующую таблицу;
- проверь дубликаты и недостижимые записи;
- проверь, что повреждённые индексы отклоняются без исключений и выхода за
  границы.

В отчёт записывай только агрегаты и хэшированные `caseId`, без полного
экспорта слов или пользовательского текста.

### 3. Сопоставление с поведением EXE

Статически найди в `CarambaSwitcherPro.exe` места, где после outputId:

- читается запись правила;
- выбирается направление RU/EN;
- вычисляется замена или только флаг;
- учитывается контекст приложения;
- вызывается автокоррекция, Yofikator или переключение раскладки.

Для каждого вывода укажи уровень доказательности:

```text
confirmed_static
confirmed_binary_correlation
strong_hypothesis
unknown
```

### 4. Контрольный набор

Используй только синтетические или заранее известные примеры:

```text
ghbdtn → привет
руддщ → hello
gtie → пишу
превет → привет
helo → hello
```

Проверь также отрицательные случаи:

```text
URL, email, путь, код, смешанный алфавит,
Safe Mode, secure input, неизвестное поле
```

Нельзя делать вывод о правиле только из одного совпадения.

## Ожидаемый ответ

1. Таблица форматов всех rule-record таблиц.
2. Диапазоны outputId и полное соответствие индексам.
3. Псевдокод безопасного `resolve_rule(outputId)`.
4. Какие поля доказанно влияют на решение.
5. Какие поля остаются opaque.
6. Минимальный API для SmartInput `audit-only`.
7. Набор unit/property тестов.
8. Прямой ответ: можно ли подключать правила к автозамене.

## Жёсткие ограничения

- не менять production-код SmartInput;
- не запускать непроверенные правила на пользовательском вводе;
- не подключать исследование к keyboard hook;
- не считать outputId словом или заменой;
- не ослаблять Safe Mode и защиту чувствительных полей;
- не писать в логи слова, токены, кандидатов и исходный текст;
- при отсутствии доказательств писать «не доказано».
