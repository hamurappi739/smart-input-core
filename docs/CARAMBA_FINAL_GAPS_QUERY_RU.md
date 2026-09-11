# Финальный запрос: закрыть оставшиеся пробелы Caramba pipeline

Предыдущие отчёты уже доказали и не требуют повторного анализа:

- forward matcher перезапускается на каждом байтовом смещении;
- reverse matcher перезапускается на каждом байтовом окончании;
- оба собирают все opaque `outputId`;
- transition formula и `0x100 output-link` подтверждены;
- A ID точно связаны с `group-2.field-3`;
- B ID точно связаны с `group-5.field-2`;
- `group-5.field-2.field3` точно адресует `group-5.field-3`;
- прямой UTF-8 synthetic harness на 20 случаях воспроизводим.

Не повторяй эти пункты. Нужно закрыть только три оставшихся неизвестных.

## 1. Реальный preprocessor

Найди upstream caller, который передаёт byte slice в native matcher по RVA:

```text
forward wrapper: 0x69C00 → 0x69FD0
reverse wrapper: 0x6CD30 → 0x6D140
consumers: 0x68200, 0x6B0C0 и их callers
```

Нужно установить:

- откуда берётся исходный буфер;
- где происходит UTF-8 encode;
- какие Unicode code points проходят через transform;
- ASCII case folding и его условия;
- есть ли `е/ё`, normalization, layout mapping и boundary markers;
- какой участок буфера передаётся matcher;
- когда создаются и очищаются буферы;
- как обрабатываются пробел, Tab, Enter, пунктуация, Backspace и смена окна.

Дай реальный псевдокод и минимум 5 доказанных synthetic traces:

```text
synthetic input → source buffer → prepared bytes (hex) → matcher call
```

Если источник текста или нормализация не найдены, укажи последний известный
caller и конкретную причину, почему data-flow обрывается.

## 2. Связь A/B и режимов

Нужно доказать, какой runtime object передаётся в каждый вызов:

```text
A-forward, A-reverse, B-forward, B-reverse
```

Используй адреса объектов, размеры, SHA/структурные признаки или трассировку
указателей. Нельзя определять язык A/B по имени файла или одному совпадению.

Верни таблицу:

| Consumer | Matcher direction | Runtime object | Decoded block | Language | Evidence | Status |
|---|---|---|---|---|---|---|

Если язык не доказан, оставь `UNKNOWN`. Если один consumer делает два прохода,
покажи условие второго прохода и его назначение.

## 3. Classifier после output collections

Продолжи статический call graph от:

```text
forward/reverse output collection
→ merge/deduplicate
→ rule table read
→ candidate/classifier
→ policy gates
→ suggestion/replacement caller
```

Найди и опиши:

- чтение `group-2.field-3`, `group-5.field-2`, `group-5.field-3`;
- точные поля, flags, weights и thresholds;
- объединение нескольких output ID;
- выбор `NoChange`, `Wait`, `Suggestion`, layout или spelling;
- определение языка и направления;
- проверки URL/email/path/code, secure field, terminal/IDE, excluded app,
  emergency pause и настроек;
- место создания replacement transaction;
- путь до `SendInput` или другого input API;
- состояние, необходимое для Double Shift Undo.

Дай минимум 8 синтетических end-to-end traces. Для каждой:

```text
prepared bytes hash
→ output IDs
→ table indices
→ decoded fields
→ classifier branch
→ final decision enum
→ policy reason
```

Не записывай реальные пользовательские слова в логи. Разрешены только заранее
заданные synthetic labels и hashes.

## Обязательный формат ответа

Для каждого утверждения используй одну метку:

```text
CONFIRMED — видно в коде или повторяется в эксперименте
LIKELY — сильная гипотеза, но data-flow неполный
UNKNOWN — доказательств недостаточно
```

Ответ должен завершаться таблицей:

| Unknown | Status | Exact blocker | Next experiment |
|---|---|---|---|

Не предлагай подключать recovered rules к live-автозамене, пока не доказана
полная цепочка `preprocessor → matcher → outputId → rule → classifier →
policy → decision`. До этого разрешён только audit-only и ручной preview.
