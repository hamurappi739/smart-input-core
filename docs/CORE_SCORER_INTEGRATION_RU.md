# Безопасное подключение дополнительного scorer’а

## Текущий статус

`PortableCorrectionEngine` уже является стабильным фасадом SmartInput Core.
Он возвращает `ReplacementToken`, `ConfidenceScore`, `Decision` (`apply`,
`wait`, `no_change`) и `Kind`. Фасад не вызывает `SendInput`, буфер обмена,
Windows-hook или live replacement.

## Что добавлено

`IAdditionalCorrectionScorer` — расширение для будущей локальной модели,
Rust/ONNX provider’а или другого scorer’а. Он получает тот же
`PortableCorrectionRequest`, что и Core, и возвращает
`AdditionalCorrectionScore`.

`AdditionalCorrectionScoringAuditService` запускает Core и scorer рядом и
возвращает только агрегируемое сравнение. Тексты замен представлены SHA-256
digest’ами; исходный токен и кандидат не сохраняются в результате сравнения.

Для уже существующего optional Rust ABI добавлен адаптер
`RustAdditionalCorrectionScorer`. Он переиспользует `IRustShadowCandidateProvider`
и передаёт native-движку только агрегированный контекст вида
`lang/ru/en/tokens/chars`. Если DLL не задана, ABI несовместим или native-вызов
не удался, результат закрывается как `wait`/`provider_unavailable`.

## Жёсткие границы

- scorer не выбирает финальное решение;
- generic scorer и audit остаются наблюдательными;
- отдельный `RustHybridCorrectionService` подключён к DI live-пайплайна, но
  `apply` из Rust сам по себе не является разрешением на замену;
- гибрид рассматривает Rust-кандидат только при Core `wait`/`no_change`, после
  чего вызывает тот же `ConfidentCorrectionApplyGate`;
- Rust не имеет ссылки на `SendInput`, Undo или replacement service;
- финальные safety-gates SmartInput Core остаются владельцем решения;
- Safe Mode, Secure Input, Double Shift и Undo не затронуты;
- для audit-only не нужны сетевые вызовы.

В live shadow-пути контекст теперь тоже преобразуется в агрегат
`lang/ru/en/tokens/chars`; snapshot предыдущего текста не передаётся в Rust.

## Гибридный live-путь

При заданном `SMARTINPUT_RUST_ENGINE_DLL` resident host создаёт
`RustHybridCorrectionService`. Для каждого токена он передаёт DLL только
агрегированный языковой контекст. Кандидат принимается лишь при доступном ABI,
решении `Replace`, confidence не ниже `0.98`, margin не ниже `0.20`, разрешённой
функции (layout/autocorrect) и успешном Core safety-gate. Если Core уже выбрал
`Apply`, Rust его не переопределяет. После одобрения используется обычный
`AutomaticLayoutCorrectionEngine`, поэтому политика, Safe Mode, Secure Input,
Double Shift и Undo остаются общими.

## Следующий шаг

Подключить конкретную модель только как реализацию `IAdditionalCorrectionScorer`,
затем прогнать её в audit-only на фиксированном наборе. Разрешать allow-list
можно только после отдельного анализа расхождений и ручного подтверждения.

Для Rust достаточно задать абсолютный путь к DLL через
`SMARTINPUT_RUST_ENGINE_DLL`; это включает shadow/audit и консервативный
гибридный путь. Без переменной окружения provider остаётся disabled, а Core
работает как раньше.

## Локальный прогон

В репозитории есть `tools/ScorerAudit`. Он запускает synthetic-fixtures с
ожидаемым решением через Core и scorer и печатает только агрегаты, case-id и
digest’ы:

```powershell
dotnet run --project tools\ScorerAudit\ScorerAudit.csproj -c Debug
```

С DLL команда та же, с предварительным заданием
`$env:SMARTINPUT_RUST_ENGINE_DLL`. Отсутствующая DLL считается безопасным
`wait`, а не ошибкой запуска приложения.

Отчёт отдельно считает `false_apply`, `false_keep` и `wrong_target` для Core
и scorer. Этот набор является smoke/contract audit; для allow-list всё ещё
нужны held-out corpus и shadow-прогон в реальных приложениях.
