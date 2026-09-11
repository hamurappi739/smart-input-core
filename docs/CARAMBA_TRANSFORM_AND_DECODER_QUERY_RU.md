# Запрос: разобрать `0x39C20` и decoder кандидатов `0x7BE0F`

Предыдущие отчёты уже подтвердили и не требуют повторения:

- forward/reverse substring matcher;
- transition formula и `0x100 output-link`;
- A/B output ID ↔ таблицы;
- B `field3 → group-5.field-3[index]`;
- trim helper `0x68ED0`;
- `0x81D538`/`0x81D53E` — статические descriptors, а не callbacks;
- `0x68100` выбирает descriptor и вызывает generic transform `0x39C20`;
- classifier и путь до live replacement пока не доказаны.

Не повторяй эти пункты. Исследуй только transform и следующий decoder.

## 1. Generic transform `0x39C20`

Восстанови точную функцию `0x140039C20`:

- calling convention и все аргументы;
- назначение `RCX`, `RDX`, `R8` и дополнительных регистров/stack args;
- формат descriptor, на который указывают `0x14081D538` и `0x14081D53E`;
- чтение descriptor bytes и все ветки по его полям;
- формат 24-byte output object;
- назначение `out+0x00`, `out+0x08`, `out+0x10` и остальных полей;
- ownership/lifetime и освобождение выделенного byte buffer;
- какие именно входные bytes копируются или преобразуются;
- есть ли case-folding, code-point mapping, reversal, prefix/suffix markers,
  layout mapping, `е/ё` или только выбор режима;
- точный буфер и длина, передаваемые после transform в `0x6B0C0`.

Покажи полный псевдокод с проверками границ и все ранние выходы. Не называй
выход «нормализованным словом», пока не доказан его источник и семантика.

## 2. Статические descriptors

Сними и расшифруй raw bytes по адресам:

```text
0x14081D538
0x14081D53E
```

Определи размер, alignment, поля и отличия. Для каждого поля укажи:

| Descriptor | Offset | Raw value | Используется в `0x39C20` | Доказанная роль | Status |
|---|---:|---:|---|---|---|

Если это не полноценная структура, а указатель/таблица/срез, покажи, как это
доказано инструкциями.

## 3. Synthetic transform traces

Если доступен законный debug build или source-level instrumentation, добавь
только offline trace на заранее заданных synthetic inputs:

```text
case id
→ source bytes hash + length
→ descriptor RVA
→ output object size
→ output buffer SHA-256 + length
→ следующий caller
```

Минимум 8 cases: `ghbdtn`, `ghbdtn `, `руддщ`, `helo-`, `ё`, leading/trailing
whitespace, ASCII uppercase, non-ASCII code point.

Разрешены только hashes/длины/структурные offsets. Запрещены keyboard hook,
SendInput, clipboard, чтение чужих окон, RAM scan, сеть и реальные пользовательские
данные.

Если debug trace невозможен, укажи точную инструкцию, где data-flow обрывается.

## 4. Decoder около `0x7BE0F`

Проследи все инструкции после возврата reverse consumer:

```text
0x6B0C0 → caller 0x7B5C9 → участок 0x7BE0F и дальше
```

Нужно установить:

- источник 16-byte entries;
- поля этих entries и их размеры;
- откуда берутся UTF-8 fragments;
- связь entries с 24/32-byte match records;
- чтение `group-2.field-3`, `group-5.field-2`, `group-5.field-3`;
- чтение auxiliary `u32[2816]`;
- преобразование output ID или table index в candidate;
- merge/sort/deduplicate и discarded candidates;
- все score/weight/threshold/priority поля;
- enum/flag финального решения.

Верни псевдокод и таблицу:

| Instruction/RVA | Источник | Поле/offset | Операция | Следующий объект | Status |
|---|---|---|---|---|---|

## 5. Поиск classifier и применения

От decoder продолжи call graph до самого последнего известного узла. Ищи:

- decision enum (`NoChange`, `Wait`, `Suggestion`, `Apply` или integer flags);
- функции, меняющие текст;
- `SendInput`, clipboard restoration или иной input API;
- policy gates и контекст приложения;
- транзакцию и Double Shift Undo.

Если native classifier отсутствует в binary или вызывается через внешний
runtime, покажи доказательство этого, а не заполняй пробел предположением.

## 6. Обязательные границы ответа

Для каждого вывода используй одну из меток:

```text
CONFIRMED — доказано инструкциями или повторяемым synthetic trace
LIKELY — правдоподобно, но data-flow неполный
UNKNOWN — доказательств нет
```

Ответ должен завершаться:

1. точным псевдокодом `0x39C20` или объяснением, почему он недостижим;
2. descriptor table;
3. 8+ transform traces либо честным описанием блокера;
4. decoder call graph `0x7BE0F`;
5. перечнем найденных decision/apply/policy/undo узлов;
6. таблицей оставшихся неизвестных и следующим экспериментом.

До доказательства `transform → matcher → outputId → rule → decoder →
classifier → policy → decision` recovered rules нельзя подключать к
live-autocorrect. Разрешены только audit-only и manual preview.
