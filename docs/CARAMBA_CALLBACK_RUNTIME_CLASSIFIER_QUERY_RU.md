# Запрос: callback-ветки, runtime A/B и consumer после merge

Предыдущие исследования уже подтвердили:

- native forward/reverse substring matcher;
- формулу transition;
- `0x100 output-link`;
- точные связи A ID, B ID и `group-5.field-2.field3`;
- trim helper `0x68ED0`;
- выбор callback в `0x68100` по последнему code point.

Не повторяй эти результаты. Сейчас нужно исследовать только то, что находится
после `0x68100` и между runtime objects и classifier.

## 1. Callback A/B: что они реально возвращают

Для адресов callback:

```text
0x81D538
0x81D53E
```

Найди:

- полную функцию и её callers;
- аргументы и calling convention;
- создаёт ли callback новый byte buffer, view, структуру или пару slices;
- точный формат результата;
- изменяет ли он регистр, кодировку, порядок байтов, границы или маркеры;
- передаёт ли результат непосредственно в `0x6B0C0`/`0x6CD30`;
- отличаются ли ветки только режимом case-folding или делают разные
  преобразования.

Нужен псевдокод и пять synthetic traces:

```text
source bytes hash/length
→ callback address
→ result type/length
→ result bytes hash или hex для synthetic fixture
→ следующий caller
```

Если callback возвращает указатель, опиши lifetime, размер и проверку границ.
Нельзя делать вывод о replacement по содержимому callback.

## 2. Доказать A/B runtime binding

Нужно установить, что именно лежит по runtime pointers:

```text
global 0x140A79028
static/runtime object 0x140A33350
parameter RDX в 0x68200
```

Найди:

- функцию загрузки/дешифровки/распаковки model objects;
- места записи этих pointers;
- размер объекта и ссылку на record array;
- сравнение размера/SHA/первых структурных записей с
  `lexicon-a.dawg` и `lexicon-b.dawg`;
- какой pointer передаётся forward/reverse matcher.

Если доступен debug build, добавь instrumentation только для заранее
заданных synthetic labels. Разрешённый trace:

```text
synthetic case id
→ object pointer identity (не адрес пользовательских данных)
→ object byte length
→ object SHA-256
→ direction
→ output IDs
```

Запрещены keyboard hook, SendInput, чтение чужих окон, сеть и реальные
пользовательские строки.

Верни таблицу:

| Pointer/caller | Object length | Object SHA | A/B file | Direction | Language | Evidence | Status |
|---|---:|---|---|---|---|---|---|

Язык RU/EN указывай только при доказательстве, не по имени файла.

## 3. Consumer после merge/deduplicate

Продолжи от:

```text
0x6B0C0 reverse result records
0x68200 forward u32 output vector
```

Найди:

- следующий caller после возврата коллекции;
- все чтения полей 24/32-байтовых match records;
- операции merge, sort, deduplicate и фильтрации;
- чтение `group-2.field-3`, `group-5.field-2`, `group-5.field-3`;
- любые обращения к auxiliary `u32[2816]`;
- создание candidate/decision объекта;
- enum или integer, соответствующий `NoChange`, `Wait`, `Suggestion`,
  layout, spelling или replacement;
- ветку, которая в итоге вызывает `SendInput`, clipboard restoration или
  другой API применения.

Если consumer не найден, покажи последний instruction-level use output vector
и причину обрыва data-flow.

## 4. Policy и Undo

Отдельно проверь, соединены ли matcher results с:

- URL/email/path/code protection;
- secure/password fields;
- terminal/IDE/excluded applications;
- emergency pause и настройками;
- replacement transaction;
- Double Shift Undo.

Не приписывай эти функции matcher-у, если они находятся в другом pipeline.

## 5. Требуемый результат

Ответ должен содержать:

1. callback call graph и псевдокод;
2. доказанный runtime A/B binding либо точную причину, почему он недостижим;
3. consumer call graph после merge;
4. таблицу `outputId → table index → decoded fields`, только где decoding
   подтверждён;
5. минимум 8 synthetic traces от callback до последнего известного consumer;
6. список найденных decision/policy/undo веток;
7. таблицу неизвестного с конкретным следующим экспериментом.

Каждое утверждение помечай:

```text
CONFIRMED / LIKELY / UNKNOWN
```

Нельзя подключать recovered rules к live-autocorrect, пока не доказана полная
цепочка `callback → prepared bytes → matcher → outputId → rule → classifier →
policy → decision`. До этого допустим только audit-only и manual preview.
