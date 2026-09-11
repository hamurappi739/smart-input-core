# Follow-up: закрыть четыре неизвестных места Caramba pipeline

Предыдущий отчёт уже подтвердил native matcher:

- forward scan запускается с каждого байтового смещения;
- reverse scan запускается с каждого байтового окончания;
- оба собирают все opaque `outputId`;
- переходы и `0x100 output-link` воспроизводимы;
- raw UTF-8 synthetic harness на 20 случаях повторяет эти циклы.

Не нужно повторно исследовать matcher. Нужно закрыть только четыре пробела:

```text
input text/keys → exact preparation → A/B matcher mode
→ outputId → exact rule table record → classifier decision
```

Все выводы помечай `CONFIRMED`, `LIKELY` или `UNKNOWN`. Нельзя заменять
отсутствие доказательств догадкой.

## 1. Точная нормализация и подготовка байтов

Найди upstream-код, который формирует `byte slice` для вызова forward/reverse
matcher. Нужны:

- конкретный caller и адрес/RVA;
- формат исходного буфера: keyboard events, UTF-16, UTF-8 или внутренний массив;
- точная таблица преобразования символов в байты;
- ASCII case folding и его границы;
- Unicode normalization;
- правила `е/ё`, регистра, повторов букв и диакритики;
- RU↔EN layout conversion, если она выполняется до matcher;
- маркеры начала/конца/границы слова;
- поведение пробела, Tab, Enter, знаков препинания, цифр и backspace;
- размер окна и очистка буфера при смене приложения/фокуса;
- отдельные режимы для текста, URL, email, пути, кода и secure field.

Покажи реальный псевдокод и таблицу минимум на 20 заранее заданных synthetic
строках:

```text
synthetic input
→ prepared bytes (hex)
→ длина
→ выбранный matcher
→ результаты outputId
```

Обязательно включи: `ghbdtn`, `руддщ`, `мущ`, `превет`, `helo`, верхний регистр,
`е/ё`, повтор буквы, пробел, пунктуацию, URL, email, путь, identifier,
mixed-script и secure-like input.

## 2. Связь forward/reverse с A/B

Нужно доказать, какой автомат используется в каждом режиме:

- A-forward;
- A-reverse;
- B-forward;
- B-reverse.

Если связь зависит от caller, контекста или типа правила — перечисли все
режимы. Дай минимум три независимых эксперимента на каждый подтверждённый
вариант и объясни, почему совпадение не случайно.

Результат должен содержать таблицу:

| Caller/RVA | Направление | Автомат | Подготовка входа | Назначение | Статус |
|---|---|---|---|---|---|

Если A/B нельзя привязать к языку RU/EN, так и напиши. Нельзя называть A
русским, а B английским по имени файла или одному совпадению.

## 3. Расшифровка rule tables

Нужно восстановить byte-level schema и связи:

1. `rules/group-2-field-3.jsonl` — подтвердить все protobuf-like fields;
2. `rules/group-2-field-4.jsonl` — определить связь с A;
3. `rules/group-3-field-3.jsonl` — определить связь с auxiliary;
4. `rules/group-5-field-2.jsonl` — полностью расшифровать поля и индексацию;
5. `rules/group-5-field-3.jsonl` — объяснить 2 426 записей и sentinel;
6. `auxiliary/group-3-field-2.bin` — структура, блоки, размеры и ссылки.

Особенно нужна доказанная цепочка:

```text
B outputId 0..72728
→ record group-5-field-2
→ field 3 value 1..2425
→ record group-5-field-3
→ следующий объект/правило
```

Для A нужна аналогичная цепочка от output ID до `group-2-field-3` и связанных
таблиц. Укажи `0-based` или `1-based`, sentinel/null, duplicate и out-of-range.

Для каждого поля верни:

| Артефакт | Номер поля/offset | Wire type | Тип | Доказанная семантика | Доказательство | Статус |
|---|---:|---|---|---|---|---|

Сырые записи с `parse_status=opaque` нельзя выбрасывать. Если поле пока не
расшифровано, сохрани его и укажи, какой эксперимент нужен дальше.

## 4. Classifier и финальное решение

Найди код после получения output collections и до применения текста. Нужно
установить:

- как объединяются результаты forward/reverse и A/B;
- как читаются rule records;
- какие flags/weights/thresholds участвуют;
- как выбирается один кандидат при нескольких outputId;
- какие решения существуют: `NoChange`, `Wait`, `Suggestion`, layout,
  spelling, replacement или другие;
- как определяется язык и направление RU↔EN;
- как защищаются правильные слова;
- как учитываются приложение, фокус, secure input, Safe Mode, URL/email/path,
  code и emergency pause;
- где создаётся транзакция и как работает Double Shift Undo;
- какая функция последней вызывает замену и какие аргументы ей передаются.

Нужен call graph:

```text
matcher caller
→ output collection consumer
→ rule decoder
→ classifier
→ policy gate
→ replacement/suggestion caller
```

Покажи минимум 12 end-to-end synthetic traces:

- две корректные RU→EN/EN→RU смены раскладки;
- две spelling-опечатки;
- два случая `Wait` из-за неоднозначности;
- правильное слово без изменения;
- URL/email/path/code;
- secure field/Safe Mode;
- emergency pause;
- отмена Double Shift.

Для каждого случая нужны только заранее заданные synthetic values, hashes и
структурные ID. Не включай пользовательский текст в постоянные логи.

## 5. Что нужно вернуть в ответе

Ответ должен содержать:

1. краткий verdict по каждому из четырёх пробелов;
2. call graph с RVA/символами;
3. псевдокод preprocessor;
4. таблицу A/B и forward/reverse binding;
5. полную таблицу полей rule records;
6. псевдокод classifier и policy gates;
7. 20+ fixtures `input → bytes → outputId → rule → decision`;
8. минимальный чистый API для SmartInput;
9. список неизвестного, которое всё ещё нельзя безопасно реализовать;
10. точные acceptance tests для следующего этапа.

Критически важно:

- не утверждать, что `outputId` — слово, replacement, язык или confidence,
  пока это не доказано;
- не подключать recovered rules к live-автозамене до полной трассы;
- не добавлять hook/SendInput в исследовательский код;
- не отправлять synthetic или пользовательский текст в сеть;
- не ломать существующий Double Shift Undo тройным Shift.
