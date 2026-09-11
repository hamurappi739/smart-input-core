# Smart Input Core — Release handoff

Дата пакета: 2026-09-08

## Назначение

Этот пакет — передача внутренней реализации Smart Input для аудита и дальнейшей работы над compact-core. UI и live replacement не являются частью этого handoff-пакета как изменяемые подсистемы.

Rust остаётся опциональным audit/shadow scorer. Он не подключён к живой автозамене, `SendInput` или Undo.

## Что проверено в этом Release-блоке

- `SmartInput.App` — Release x64: 0 ошибок, 0 предупреждений.
- `SmartInput.ResidentHost` — Release x64: 0 ошибок, 0 предупреждений.
- `WordProbe` — Release: 0 ошибок, 0 предупреждений.
- `ScorerAudit` — Release: 0 ошибок, 0 предупреждений.
- `SmartInput.App.Tests`: 148/148.
- `SmartInput.ResidentHost.Tests`: 3/3.
- диагностический Core-набор: 5/5.
- PrivacyAudit: 337 исходных C#-файлов, 3 tool-файла, 90 logger-call; unsafe logger calls = 0, raw tool outputs = 0.

## Состав

- `src/` — исходный код ядра и runtime.
- `tests/` — тестовые проекты.
- `tools/` — WordProbe, ScorerAudit, PrivacyAudit и ResidentHostMetrics.
- `bin/ResidentHost/` — x64 Release output ResidentHost.
- `bin/WordProbe/` и `bin/ScorerAudit/` — Release output диагностических инструментов.
- `docs/` — end-to-end аудит, privacy-аудит и handoff-документы.
- `MANIFEST.sha256.json` — размер и SHA-256 каждого переданного файла.

## Безопасная граница

В диагностические логи и отчёты не должны попадать исходный пользовательский текст, исправленный текст, заголовки окон, имена процессов и контекстные слова. Разрешены агрегаты, enum-статусы, длины, счётчики и хэши synthetic case-id.

Изменение этой границы или включение Rust в live replacement требует отдельного решения и новых интеграционных тестов.

## Ограничения на дату пакета

- Физический ввод в Telegram и других native-приложениях в этой среде не подтверждён; это остаётся `UNKNOWN` до ручного прогона на машине пользователя.
- Полный Core-прогон ранее выявил 13 существующих quality-gate падений из 1598 тестов. Privacy-патч их не ослабляет и не маскирует.
- ResidentHost превышает целевой уровень 30–40 MiB; измеренный baseline и план mmap/FST находятся в end-to-end отчёте. Оптимизация памяти — следующий отдельный блок.

После блока 6 `.sidict` читается через read-only mmap, Hunspell и SymSpell
загружаются лениво. Новый idle baseline ResidentHost: 85.050 MiB Working Set
average и 48.859 MiB Private average за 20 секунд. Цель 30–40 MiB ещё не
достигнута; минимальный FST пока не внедрялся.

## Повторяемые команды

Из корня исходного проекта:

```powershell
dotnet build src\SmartInput.App\SmartInput.App.csproj -c Release -p:Platform=x64 --no-restore --nologo
dotnet build src\SmartInput.ResidentHost\SmartInput.ResidentHost.csproj -c Release -p:Platform=x64 --no-restore --nologo
dotnet build tools\WordProbe\WordProbe.csproj -c Release --no-restore --nologo
dotnet build tools\ScorerAudit\ScorerAudit.csproj -c Release --no-restore --nologo
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\PrivacyAudit\PrivacyAudit.ps1
```

`ScorerAudit` использует Rust DLL только если она явно задана через `SMARTINPUT_RUST_ENGINE_DLL`; отсутствие DLL должно приводить к безопасному `wait`, а не к live-замене.
