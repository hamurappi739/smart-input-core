# Rust shadow mode

## Назначение

Rust Smart Input Core подключён к SmartInput только как **shadow provider**.
Он получает завершённый токен после того, как текущий C# pipeline уже прошёл
Protection, Safe Mode, secure-input и application policy, и возвращает
candidate в память для сравнения.

Rust в этом режиме не имеет права:

- вызывать `SendInput`, clipboard или Windows hook suppression;
- менять решение C# движка;
- применять текст, переключать раскладку, писать настройки или learning;
- участвовать в Undo/Double Shift;
- записывать токены, кандидаты или контекст в log/diagnostics.

## Включение

По умолчанию Rust DLL не загружается. Это важно для обычного запуска и памяти.
Для developer shadow run задайте явный абсолютный путь:

```powershell
$env:SMARTINPUT_UI_EXE = "C:\Users\shuly\Desktop\Автоматический Т9 на комп\src\SmartInput.App\bin\Release\net8.0-windows\win-x64\SmartInput.exe"
$env:SMARTINPUT_RUST_ENGINE_DLL = "C:\Users\shuly\Documents\Codex\2026-09-06\referenced-chatgpt-conversation-this-is-an\outputs\smart-input\target\release\smart_input.dll"
& "C:\Users\shuly\Desktop\Автоматический Т9 на комп\src\SmartInput.ResidentHost\bin\x64\Release\net8.0-windows\win-x64\SmartInput.ResidentHost.exe"
```

Если переменная отсутствует, файл недоступен, ABI не равен 1 или DLL не
экспортирует обязательные функции, провайдер fail-closed переходит в
`Disabled`, `LibraryNotFound`, `LibraryLoadFailed`, `AbiMismatch` или
`InitializationFailed`. SmartInput продолжает работу на собственном C# engine.

## Контракт

Контракты находятся в:

- `src/SmartInput.Core/Integration/RustShadowIntegration.cs`;
- `src/SmartInput.Infrastructure/Rust/RustNativeShadowCandidateProvider.cs`.

ABI проверяет version и layout capability до создания native engine. Каждый
`SmartInputResult` освобождается `smart_input_result_free`, включая `Keep` и
ошибочные результаты. Входы и native output ограничены, invalid status или
UTF-8 приводят только к `NativeFailure`, без остановки keyboard pipeline.

## Privacy-safe counters

`IRustShadowAuditService` хранит только aggregate counts:

- provider unavailable / native failures;
- both keep;
- Rust keeps while C# applies;
- Rust applies while C# keeps/waits;
- matching and differing applies.

Дополнительно считаются тип решения C# и Rust, причины `Keep`, суммарная и
максимальная задержка вызова Rust, а также число **кандидатов на ручную
проверку**. Последняя категория требует exact match C#↔Rust, совпадения типа
решения, confidence не ниже `0.98` и margin не ниже `0.20`. Она не является
allow-list, не сохраняет образцы и ничего не применяет.

Исходные токены и replacement не входят в `RustShadowAuditStatus`. Для
единичного тестового сравнения используются только SHA-256 digests.

Окно «Дополнительно» показывает эти же агрегированные числа через локальный
named pipe `SmartInput.RustShadowAudit.v1`. У канала нет запроса, команд и
текстового payload: фоновый процесс отправляет один снимок с состоянием DLL и
счётчиками, после чего соединение закрывается. Поэтому отдельное окно настроек
не создаёт ложный второй набор показателей и не передаёт фоновому процессу
набираемый текст.

## Проверка

```powershell
$env:SMARTINPUT_RUST_ENGINE_DLL = "C:\Users\shuly\Documents\Codex\2026-09-06\referenced-chatgpt-conversation-this-is-an\outputs\smart-input\target\release\smart_input.dll"
dotnet test tests\SmartInput.Core.Tests\SmartInput.Core.Tests.csproj `
  -c Release --no-restore `
  --filter "FullyQualifiedName~RustNativeShadowCandidateProviderTests|FullyQualifiedName~RustShadowAuditServiceTests"
```

Для маленькой синтетической C#↔Rust матрицы с выводом **только чисел**:

```powershell
$env:SMARTINPUT_RUST_ENGINE_DLL = "C:\Users\shuly\Documents\Codex\2026-09-06\referenced-chatgpt-conversation-this-is-an\outputs\smart-input\target\release\smart_input.dll"
dotnet test tests\SmartInput.Core.Tests\SmartInput.Core.Tests.csproj `
  -c Release --no-restore `
  --filter "FullyQualifiedName~RustShadowParityMatrixTests" `
  --logger "console;verbosity=detailed"
```

Матрица не является production-корпусом и не определяет allow-list. Она
проверяет FFI и даёт базовые aggregate-счётчики для повторяемого сравнения.

Перед переходом к allow-list обязательны real-app matrix, held-out corpus,
memory measurement and UI display of only the aggregate counters. Режим
`Controlled Live` в этом этапе не реализован.

## Первый замер

На этой машине свежий замер показал примерно **51 MB private memory** и
**84 MB working set** у resident host без Rust DLL; с явно подключённой Rust
Release DLL — примерно **74 MB private memory** и **151 MB working set**.
Это больше цели 30–40 MB private, поэтому shadow mode остаётся developer-only
и не включается обычным запуском. Перед поставкой нужны раздельные замеры DLL,
SID/MOR1 и managed host, а также переход словарей на memory-mapped compact
ресурсы.

Отдельно проверен NativeAOT-профиль resident host: без Rust он дал около
**54 MB private** и **70 MB working set**; с Rust — около **76 MB private** и
**135 MB working set**. Он совместим с агрегированным IPC, но пока не заменяет
обычный запуск: требуется реальная матрица hook/Undo/Safe Mode на целевых
Windows-машинах. Это эксперимент оптимизации, а не выпускной режим.
