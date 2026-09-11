# Smart Input — блок 3: RAM / CPU / latency ResidentHost

Дата: 2026-09-08  
Область: Release x64, Rust shadow/live replacement не включены, пользовательский текст не сохранялся.

## CONFIRMED

### Release-сборка

Команда:

```powershell
dotnet build src\SmartInput.ResidentHost\SmartInput.ResidentHost.csproj -c Release -p:Platform=x64 --no-restore --nologo
```

Результат: **0 ошибок, 0 предупреждений**.

### ResidentHost после прогрева

Замер выполнен отдельным процессом ResidentHost. Перед выборкой был прогрев 5 секунд; затем 15 секунд выборки с интервалом 250 мс. Rust DLL принудительно отключена только для baseline-замера. Было 40 aggregate-only запросов status pipe.

```yaml
sample_count: 58
measurement_ms: 15229.538
working_set_mib: { min: 82.363, average: 83.815, p95: 86.270, max: 86.281 }
private_memory_mib: { min: 46.605, average: 48.350, p95: 50.508, max: 50.508 }
cpu_process_percent_one_core: 1.436
logical_processors: 12
status_pipe: { requests: 40, successful: 40, failures: 0 }
status_round_trip_ms: { min: 0.074, average: 0.836, p95: 0.315, max: 27.278 }
privacy: { raw_text_logged: false, raw_status_payload_logged: false, window_titles_logged: false }
```

Среднее status-pipe выше p95 из-за единичного выброса; percentile и max сохранены отдельно, чтобы выброс не скрывался усреднением.

Вывод: целевой compact-core уровень **30–40 MiB private memory** сейчас превышен. Peak private memory составил **50.508 MiB**. Working Set составил **86.281 MiB** и включает разделяемые runtime-модули, поэтому для сравнения с лимитом основным числом считается private memory.

### Core decision latency

`WordProbe` прогрет перед измерением, проверен набор из 121 синтетического single-token case. Выполнено пять независимых запусков. В выводе только агрегаты и fingerprint.

```yaml
run_1: { p50_ms: 7.330, p95_ms: 16.090, max_ms: 147.110, average_ms: 9.451 }
run_2: { p50_ms: 7.301, p95_ms: 15.924, max_ms: 143.326, average_ms: 9.377 }
run_3: { p50_ms: 7.209, p95_ms: 15.582, max_ms: 143.297, average_ms: 9.297 }
run_4: { p50_ms: 7.217, p95_ms: 16.245, max_ms: 144.650, average_ms: 9.479 }
run_5: { p50_ms: 7.286, p95_ms: 16.236, max_ms: 141.302, average_ms: 9.368 }
```

Медиана пяти запусков: `p50=7.286 ms`, `p95=16.090 ms`, `max=143.326 ms`, `average=9.377 ms`.

Это latency Core с включёнными offline SymSpell/Hunspell providers, а не доказательство задержки Windows hook. Хвост до 143 мс требует отдельного профилирования перед снижением порогов или расширением live-функций.

### Тесты

- PerformanceMetricsCollector + ExactWordCorpus профиль: **15/15**.
- ResidentHost Release x64: **успешно**.
- Aggregate-only status pipe: **40/40** успешных ответов.
- Новый launcher замера: `tools\ResidentHostMetrics\Run-ResidentHostMetrics.ps1`.

Воспроизводимая команда:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  tools\ResidentHostMetrics\Run-ResidentHostMetrics.ps1 `
  -WarmupSeconds 5 -DurationSeconds 15 -SampleIntervalMs 250 `
  -StatusRequests 40 -StatusConnectTimeoutMs 1500
```

## LIKELY

- ResidentHost уже стабилен после прогрева: Working Set и private memory не растут монотонно в течение выборки.
- CPU idle-профиль низкий относительно одного логического ядра, но это не нагрузочный прогон клавиатурного hook.
- Высокий latency tail Core может проявляться как редкое ощущение «подвисания», однако источник tail ещё не локализован до конкретного provider/операции.
- Для достижения 30–40 MiB нужно оптимизировать resident-ресурсы словарей/индексов; mmap/FST остаются отдельным следующим этапом и в этом блоке не внедрялись.

## UNKNOWN

- Реальная задержка от `WH_KEYBOARD_LL` до boundary delivery и replacement result при физическом вводе.
- RAM/CPU при реальных событиях Telegram, Unicode replacement, Undo и активной проверке пунктуации.
- Какая часть private memory приходится на Hunspell, SymSpell, managed heap и native/runtime allocations без профайлера памяти.
- Причина редкого максимума около 143 мс в Core latency.

## Изменения блока 3

- `tools\WordProbe\Program.cs` теперь выводит только aggregate latency `p50/p95/max/average`.
- Добавлен `tools\ResidentHostMetrics\Run-ResidentHostMetrics.ps1`: запускает актуальный x64 Release ResidentHost, прогревает его, вызывает существующий aggregate-only metrics harness и завершает только свой дочерний процесс.
- Пользовательский текст, payload status pipe и заголовки окон в измерения и отчёты не попадают.

## Итог блока 3

Resource baseline получен. ResidentHost работает, status pipe отвечает без потерь, idle CPU приемлем. Основной дефект блока — private memory выше цели и тяжёлый latency tail Core. До перехода к mmap/FST сначала нужно сохранить этот baseline и в следующем блоке проверить privacy-safe telemetry/stress under event pressure; live Telegram latency остаётся UNKNOWN до ручного прогона.
