# План снижения памяти SmartInput

## Измерение текущей сборки

Измерение выполнялось на Release `win-x64` после полного запуска координаторов:

| Режим | Working Set | Private bytes |
|---|---:|---:|
| Avalonia + .NET, без KBM | ~224 MB | ~201 MB |
| С полной KBM-моделью | ~376 MB | ~374 MB |
| Экспериментальный GC heap hard limit 40 MB | ~190 MB | ~161 MB |
| Tray-first без окна и без прогрева Hunspell | ~140–155 MB | ~114–128 MB |
| Обычный режим после compact UTF-8 словаря (после стабилизации) | ~197 MB | ~170 MB |
| Обычный режим с предсобранными `.sidict` пакетами (15 секунд) | ~181 MB | ~153 MB |
| Новый `SmartInput.ResidentHost`, 8 секунд простоя | **84.1 MB** | **49.3 MB** |
| ResidentHost после mmap SIDICT и lazy external spelling, 20 секунд | **85.05 MiB** | **48.859 MiB** |

Вывод: ограничение GC не ограничивает память всего процесса. Основной расход
создают Avalonia/Skia, загруженный UI, runtime .NET и native-графика. Поэтому
цель 30–40 MB для постоянно запущенного процесса невозможна одной упаковкой
или сжатием словаря.

## Что уже сделано

- KBM не загружается без `SMARTINPUT_KBM_MODEL_DIR`.
- Провайдер KBM читает модель один раз и проверяет размер/хэш/границы.
- Словари используют bounded exact-word индекс вместо полного Hunspell-графа.
- Постоянный словарный индекс хранит нормализованные RU/EN-слова одним UTF-8
  блоком и таблицей смещений, без `HashSet<string>` и объектов на каждое слово.
- При сборке доступны предсобранные пакеты `en_US.sidict` и `ru_RU.sidict`.
  Они загружаются напрямую; повреждённый или отсутствующий пакет безопасно
  вызывает fallback на исходный `.dic`.
- 2026-09-08: resident host читает `.sidict` через read-only memory mapping;
  offset table и UTF-8 blob больше не копируются в managed heap.
- 2026-09-08: Hunspell graph создаётся лениво только для запроса, которому
  действительно не хватило compact one-edit кандидатов; обычный lookup и
  обычные опечатки не прогревают его на старте.
- 2026-09-08: resident DI использует compact word-form path для первого
  spelling lookup; тяжёлый SymSpell delete-index оставлен fallback-ом для
  редких distance-two случаев и не загружается в idle.
- Включены Workstation GC и `System.GC.ConserveMemory=5`.
- Запрещено задавать опасный жёсткий heap limit в production: он может привести
  к OutOfMemoryException и не уменьшает native-память Avalonia.

## Реалистичная архитектура для цели 30–40 MB

Нужен tray-first режим:

```text
маленький resident helper (hook + tray + correction core)
                 │ IPC только при открытии настроек
                 ▼
отдельный Avalonia UI-процесс (запускается по требованию)
```

Постоянный процесс не должен загружать FluentTheme, окна, preview-страницы,
Inter fonts и Skia GPU surface. Окно настроек запускается отдельным процессом
и закрывается после работы. Для настоящих 30–40 MB resident helper, скорее
всего, потребуется отдельный native/Rust/C++ tray host либо очень маленький
framework-dependent .NET host; текущий Avalonia-процесс нельзя честно выдать
за 40 MB.

## Следующие этапы

1. Добавить измерительный режим: Working Set, Private Bytes, managed heap,
   native modules и peak без пользовательского текста.
2. Перенести загрузку KBM full preview в on-demand процесс/режим.
3. ~~Отделить tray/hook host от Avalonia UI.~~ Готово: `SmartInput.Runtime`
   содержит runtime без Avalonia, а `SmartInput.ResidentHost` держит hook,
   native tray и безопасные оверлеи; UI запускается только по запросу.
4. ~~Перевести resident host на компактное бинарное представление словаря:
   memory-mapped DAWG/минимальный FST или sorted UTF-8 table + offsets.~~
   Выполнено частично и безопасно: read-only mmap для существующего sorted
   UTF-8 table + offsets; FST не добавлялся, поскольку текущий формат уже
   проверен и сохраняет runtime-контракт.
5. Проверить cold start, 10 минут простоя, 1000 границ, открытие/закрытие UI
   и peak после словарного запроса.
6. Только после измерений выбрать framework-dependent, trimming или NativeAOT.
   Trimming/R2R уменьшают пакет или startup, но не гарантируют 30–40 MB.

### Как пересобрать словарные пакеты

Для изменения исходного `.dic` нужно пересобрать соответствующий `.sidict`:

```powershell
dotnet run --project tools\SmartInput.DictionaryCompiler\SmartInput.DictionaryCompiler.csproj -- `
  src\SmartInput.App\Dictionaries\Hunspell\ru_RU.dic `
  src\SmartInput.App\Dictionaries\Hunspell\ru_RU.sidict `
  начала началу началом начале начали запуска запуску запуском команду решения самого странно машина жизнь пишу
```

Английский пакет пересобирается той же командой без русских дополнительных форм.

## Acceptance для памяти

Отчёт должен содержать отдельные значения для:

- resident tray/hook idle;
- resident tray после 1000 boundary events;
- UI открыто;
- KBM preview открыто;
- peak Working Set;
- Private Bytes;
- managed heap;
- время запуска.

Цель 30–40 MB применяется только к resident tray/hook host. Для открытого
Avalonia UI задаётся отдельный честный предел, потому что UI физически тяжелее.

Подробная инструкция нового режима: [RESIDENT_HOST_RU.md](RESIDENT_HOST_RU.md).

## NativeAOT: отдельный экспериментальный профиль

Локальные JSON-хранилища переведены на source-generated
`JsonSerializerContext`: это убрало runtime-reflection и позволило AOT-хосту
стабильно пройти 10-секундный реальный запуск. Его замер: **67.1 MB Working
Set** и **51.8 MB Private Bytes**. Рабочий набор стал меньше, но private memory
на этой машине не улучшилась относительно framework-dependent host (49.3 MB).

Поэтому AOT остаётся **опциональным** профилем, а не запуском по умолчанию. Его
нужно проверять отдельно на tray, уведомлениях, prediction-оверлее, Double Shift,
настройках и реальном вводе. Сборка:

```powershell
dotnet publish src\SmartInput.ResidentHost\SmartInput.ResidentHost.csproj `
  -c Release -r win-x64 -p:PublishProfile=NativeAot
```

Цель 30–40 MB нельзя считать достигнутой одной компиляцией: измеряется именно
private memory запущенного процесса, а не размер EXE или только Working Set.

## Добавлен безопасный tray-first режим

В приложение добавлен опциональный режим `SMARTINPUT_TRAY_ONLY=1`:

- окно настроек не создаётся при старте;
- окно создаётся только при выборе «Открыть настройки» в трее;
- если системный трей недоступен, приложение автоматически создаёт окно, чтобы не оставить пользователя с невидимым процессом;
- тяжёлый прогрев Hunspell в этом режиме отложен до первого запроса коррекции;
- обычный запуск без переменной окружения не изменён.

Это уменьшает начальный resident-профиль, но не превращает Avalonia-процесс в
30–40 МБ: UI-стек, .NET runtime и Windows-графика всё равно остаются
загружены. Для строгого лимита нужен отдельный лёгкий tray/hook host без
Avalonia и UI-процесс, запускаемый только по требованию.

Проверка режима в PowerShell:

```powershell
$env:SMARTINPUT_TRAY_ONLY='1'
& 'C:\Users\shuly\Desktop\Автоматический Т9 на комп\src\SmartInput.App\bin\Release\net8.0-windows\win-x64\SmartInput.exe'
Remove-Item Env:SMARTINPUT_TRAY_ONLY
```
