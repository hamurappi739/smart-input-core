# KBM candidate-provider в SmartInput

## Статус

В SmartInput добавлен точный audit/preview-only provider для переданного
KBM-артефакта. Он не подключён к live-автозамене и не вызывает Windows input
API.

Проверенный маршрут:

```text
явно выбранный prepared route
  → exact DAWG lookup
  → outputId
  → indexed text pool
  → opaque candidate observation
```

Провайдер никогда не перебирает marker routes, не выполняет fuzzy lookup, не
оценивает score и не применяет текст.

## Компоненты

- `SmartInput.Core/Integration/KbmCandidateContracts.cs`
  - `KbmMarkerRoute`;
  - `KbmPreparedInput`;
  - `KbmCandidate`;
  - `IKbmCandidateProvider`;
  - безопасный `NullKbmCandidateProvider`.
- `SmartInput.Infrastructure/RecoveredRules/KbmCandidateProvider.cs`
  - проверка хэша DAWG;
  - bounds-check размера файлов и pool;
  - строгий JSONL-парсер `id/text/metadata_hex`;
  - exact DAWG transition и output-link lookup.
- `SmartInput.Core/Integration/KbmAuditComparisonService.cs`
  - shadow-сравнение с текущим `IPortableCorrectionEngine`;
  - только хэши кандидатов, без текста в результате сравнения.
- `SmartInput.Core/Integration/KbmAllowListCorrectionService.cs`
  - первый безопасный live-мост только для трёх подтверждённых relations:
    `commersant → kommersant`, `infact → in fact`, `Аксенов → Аксёнов`;
  - требует точного source, marker route и candidate text из проверенной модели.
- `SmartInput.Core/Integration/CorrectionOwnershipGate.cs`
  - режимы `Disabled/AuditOnly/Preview/AllowList/Live`;
  - один владелец применения и проверка поколения.

По умолчанию DI регистрирует `NullKbmCandidateProvider`, поэтому без переменной
`SMARTINPUT_KBM_MODEL_DIR` allow-list не активирует ни одной KBM-замены.
При подключённой модели allow-list-кандидат проходит тот же rejection policy,
`SafeTextReplacementService` и `CorrectionUndoService`, что и собственные
исправления. Все остальные KBM-слова остаются audit/preview-only.

В интерфейсе добавлена страница **«Сравнение KBM»**. Она показывает рядом
решение собственного движка и точный результат recovered-модели, но не имеет
кнопки внешнего применения. Это позволяет безопасно проверять реальные
расхождения до того, как появятся доказанные правила classifier-а.

## Подключение к модели

Каталог должен содержать:

```text
kbm-lexicon.dawg
kbm-indexed-text-pool.jsonl
```

Пример загрузки только в audit/preview-коде:

```csharp
var provider = KbmCandidateProvider.Load(
    modelDirectory,
    expectedDawgSha256:
        "1ac698262db2c425db37a4d078faf72753c55a7070bbbb367356f1c4d256702a");

var prepared = KbmPreparedInput.FromToken(
    token,
    KbmMarkerRoute.Prefix02);

if (provider.TryResolve(prepared, out var candidate))
{
    // показать в preview или записать обезличенное audit-событие;
    // SafeTextReplacementService здесь не вызывается.
}
```

Для реального KBM-пакета загрузчик проверяет DAWG SHA-256 и полный pool
`0..70006`. Синтетические тесты могут отключить требование полного pool.

## Проверка

Новые тесты:

```powershell
dotnet test tests\SmartInput.Core.Tests\SmartInput.Core.Tests.csproj `
  -c Release --no-restore `
  --filter FullyQualifiedName~KbmCandidateProviderTests
```

Для полного handoff-пакета:

```powershell
$env:SMARTINPUT_KBM_MODEL_DIR = `
  'C:\Users\shuly\Desktop\SmartInput-Engine-Handoff-2026-09-06\model'
dotnet test tests\SmartInput.Core.Tests\SmartInput.Core.Tests.csproj `
  -c Release --no-restore `
  --filter FullyQualifiedName~KbmCandidateProviderTests
```

Последний запуск проверил все 70 007 canonical witness-ключей.

Чтобы увидеть реальные KBM-кандидаты в окне приложения, запускайте его из
PowerShell с переменной каталога модели (переменная действует только для этого
запуска):

```powershell
$env:SMARTINPUT_KBM_MODEL_DIR = `
  'C:\Users\shuly\Desktop\SmartInput-Engine-Handoff-2026-09-06\model'
& 'C:\Users\shuly\Desktop\Автоматический Т9 на комп\src\SmartInput.App\bin\Release\net8.0-windows\win-x64\SmartInput.exe'
```

Без этой переменной страница честно покажет «Кандидат не найден», а обычная
автозамена продолжит работать как раньше.

## Что пока запрещено

Нельзя считать `outputId` разрешением на замену. Перед live-применением ещё
нужны отдельные доказанные или явно заданные правила:

1. выбор route из boundary/preprocessor;
2. language pair и feature gates;
3. защита URL/email/path/code/secure fields и исключённых приложений;
4. ambiguity/score policy;
5. одна replacement transaction через `SafeTextReplacementService`;
6. запись Undo через `CorrectionUndoService` и Double Shift.

Recovered provider должен иметь единственного владельца применения. Пока он
остаётся `audit-only/preview`, старый SmartInput engine продолжает работать
единственным live-владельцем.
