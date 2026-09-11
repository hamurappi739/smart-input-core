# Smart Input — блок 4: privacy-safe telemetry и стресс событий

Дата: 2026-09-08  
Область: локальная диагностика и aggregate-only telemetry; Rust остаётся shadow-only, live replacement не менялся.

## CONFIRMED

### Privacy audit

Команда:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\PrivacyAudit\PrivacyAudit.ps1
```

Результат:

```yaml
source_files_scanned: 340
tool_files_scanned: 3
logger_calls_scanned: 91
unsafe_logger_calls: 0
raw_tool_outputs: 0
metadata_contract_redacted: true
status_contract_aggregate_only: true
privacy_safe: true
raw_text_logged: false
raw_replacement_logged: false
window_titles_logged: false
findings: []
```

В ходе аудита также удалена поклавишная Debug-запись из `InputDiagnosticCoordinator`: раньше она писала отдельный virtual-key код для каждого события. Теперь этот путь не создаёт per-key логов.

### Потокобезопасность telemetry

Добавлен конкурентный тест: 8 writer-задач по 2 000 итераций одновременно записывают duration, live-stage и queue depth, а отдельная задача параллельно читает snapshots.

Результат:

```yaml
writers: 8
iterations_per_writer: 2000
live_stage_records: 16000
final_hook_observed: 16000
rolling_duration_window: 256
queue_high_water_mark: 8
snapshot_assertions: passed
```

Counters не теряются, rolling window не выходит за лимит, snapshot не возвращает отрицательные или неограниченные значения. Счётчики live pipeline используют atomic increments; duration-агрегаты защищены lock.

### Стресс и регрессии

- KeyboardStress: **3/3**.
- LivePipeline: **15/15**.
- Privacy: **4/4**.
- PerformanceMetricsCollector + конкурентный тест: **11/11**.
- ResidentHost.Tests: **5/5**.
- App hotkey/Undo/manual-correction/input-diagnostic профиль: **39/39**.

Все перечисленные тесты прошли. При сборке App.Tests остаются только уже существующие предупреждения о неиспользуемых fake-событиях в тестовых классах; ошибок нет.

### Контракт status pipe

Resident status pipe передаёт только monitoring flag, enum/status и числовые aggregate-счётчики. Rust shadow pipe передаёт только provider state и aggregate audit status. Текст токена, кандидат, предыдущий контекст, имя процесса и заголовок окна в эти контракты не входят.

## LIKELY

- Счётчики и duration snapshots выдерживают конкурентную запись и чтение без обнаруженной гонки.
- Удаление per-key Debug-лога закрывает найденный риск поклавишной телеметрии.
- Текущая privacy-safe модель достаточна для локального audit/shadow режима: наружу уходят только агрегаты, enum-статусы и digest-кандидатов в review-only сравнении.

## UNKNOWN

- Поведение стороннего лог-провайдера, если пользователь вручную перенастроит host вне штатной конфигурации.
- Полная семантика UI diagnostic buffer: он хранит последние key-event объекты в памяти для отладочного экрана, хотя они не содержат текст, заголовок или имя процесса. Если потребуется строгий режим «только агрегаты даже в UI», этот экран нужно будет отдельно перевести на счётчики.
- Privacy-свойства внешних crash-dump/profiling инструментов ОС — они не принадлежат SmartInput и в этот аудит не включены.

## Изменения блока 4

- `src\SmartInput.App\Services\InputDiagnosticCoordinator.cs`: удалён per-key Debug log.
- `tests\SmartInput.Core.Tests\PerformanceMetricsCollectorTests.cs`: добавлен конкурентный стресс-тест telemetry.
- `docs\BLOCK_4_PRIVACY_TELEMETRY_AUDIT_RU.md`: этот отчёт.

## Итог блока 4

Privacy-проверка зелёная, конкурентный telemetry-профиль зелёный, потерь counters не обнаружено. Единственное отмеченное ограничение — диагностический UI-буфер в памяти содержит технические key-event записи, но не пишет их в логи и не содержит пользовательский текст. Следующий этап — финальная Release-проверка/упаковка; mmap/FST остаётся отдельной оптимизацией памяти.
