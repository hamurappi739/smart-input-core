# Smart Input Core — блок 5: Release-сборка и передаваемый пакет

Дата аудита: 2026-09-09  
Область: `SmartInput.Core`, `ResidentHost`, тестовые harness-инструменты и Rust shadow ABI.

Этот документ содержит только агрегаты и технические статусы. Исходные пользовательские строки, исправленные строки, заголовки окон и сырые контексты в отчёт не записываются.

## CONFIRMED

### Безопасность решения

- `BoundedCandidateApplyGuard` теперь оценивает конкурирующее spelling-решение до shortcut-пути layout correction.
- Универсальная layout-замена не проходит при неоднозначном или uniquely-recoverable spelling-кандидате.
- Для необходимых обратных layout-сценариев оставлен малый явный список положительных layout-якорей; его принятия отдельно отражаются в audit accounting как `AmbiguousLayoutAnchorAccepted`.
- В live replacement Rust по-прежнему не участвует: `liveReplacement=false`, режим `audit-shadow-only`.
- Порог recovery в corpus-тесте зафиксирован на `0.90`. Это согласуется с ранее принятой пользовательской нижней границей `89.74%` и сохраняет приоритет нулевых ложных применений.

### Regression и held-out

- Core regression без тяжёлого full-corpus теста: **1585/1585**.
- Joint correction pipeline: **90/90**.
- Fast regression: **37/37**.
- Live-pipeline ordering/boundary tests: **15/15**.
- Privacy tests: **4/4**.
- Performance budget test: **1/1**.
- App/profile tests: **152/152**.
- ResidentHost tests: **5/5**.
- Выбранный held-out seed `8675309`: **1/1**.

Детальный выбранный corpus-прогон: 250 000 мутаций и 438 450 токенов. В нём зафиксированы:

- `TotalAmbiguousApplied=0`;
- `WrongUniqueTarget=0`;
- `WrongDirectLayout=0`;
- `WrongCombined=0`;
- recovery **94.27%**, выше настроенного floor 90%;
- подробный latency-профиль: p95 около **1.25 ms**, максимум около **2.62 ms**;
- индекс в памяти: **4 553 164 bytes**;
- пиковый Working Set тестового процесса: около **201 MiB**.

Эти числа относятся к выбранному seed-прогону, а не являются гарантией для всех возможных корпусов.

### Дополнительные инструменты

- `ScorerAudit`: **95** privacy-safe синтетических/held-out cases; сырые строки не логируются; Rust provider остаётся shadow-only.
- `WordProbe`: **121** cases, aggregate p50 около **7.30 ms**, p95 около **16.25 ms**, максимум около **134.19 ms**.
- Privacy audit: **340** исходных файлов и **3** tool-файла; **91** logger call; unsafe logger calls **0**; raw tool outputs **0**; `privacySafe=true`.
- В privacy audit: `rawTextLogged=false`, `rawReplacementLogged=false`, `windowTitlesLogged=false`.

### Release и пакет

- Финальная сборка выполняется в конфигурации Release для solution, ResidentHost, ScorerAudit и WordProbe.
- Пакет содержит исходники Core/ResidentHost, тесты, audit-инструменты, отчёты блоков и Release-выходы.
- После упаковки создаётся `MANIFEST.sha256.json` с размером и SHA-256 для каждого файла.
- Упаковщик исключает `.dmp`, `.mdmp` и `.log` из исходников и тестовых каталогов: memory dumps не должны пересекать границу передачи.
- Пакет не включает разрешение на SendInput/live replacement и не меняет UI-приложение пользователя.

## LIKELY

- Текущий дефект с опасными повторными/двойными изменениями должен быть существенно ограничен: runtime теперь не принимает generic layout shortcut, если spelling-ветка уже дала конкурирующий кандидат.
- Явные layout-якоря сохраняют обязательные сценарии, но не превращают весь словарь в безусловный allow-list.
- Последний held-out pass подтверждает отсутствие выявленных safety-ошибок на выбранном seed после патча.

## UNKNOWN

- Не проведён полноценный ручной native-прогон в Telegram/другом реальном приложении из этого сеанса: CUA не имеет доступа к native-окнам Windows. Поэтому физическое поведение конкретного host, hook thread, boundary key и SendInput в Telegram требует ручной проверки.
- Целевой compact-core уровень 30–40 MiB пока не достигнут: baseline ResidentHost около 48 MiB Private Bytes и около 84 MiB Working Set. mmap/FST остаётся отдельным этапом оптимизации.
- Rust DLL проверена как ABI/shadow provider, но не является источником live-решения.
- В UI diagnostic buffer остаются технические key-event объекты в памяти; они не содержат текст, процесс или заголовок окна. Если потребуется строго aggregate-only хранение и там, это отдельная задача.
- Статистические показатели полного набора зависят от корпуса, seed и состава словарей; приведённые значения нельзя трактовать как универсальную точность модели.

## Состав передаваемого пакета

- Release-выход `ResidentHost`;
- Release-выход `WordProbe`;
- Release-выход `ScorerAudit`;
- исходники `src` и `tests` без `bin/obj`;
- privacy-safe отчёты блоков 2–5;
- held-out fixture и audit harness;
- `MANIFEST.sha256.json`.

Финальный статус: **готово к передаче для следующего этапа интеграции и ручной проверки; live replacement Rust не включён**.
