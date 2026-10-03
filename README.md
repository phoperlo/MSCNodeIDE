# MSCNodeIDE

Визуальный конструктор C#-модов для **My Summer Car** (загрузчик **MSCLoader 1.4.2**).

Собираешь логику из нод — программа генерирует **настоящий, читаемый C#**, создаёт `.csproj`
и собирает рабочую `.dll`, которую MSCLoader сразу подхватывает.

```
граф нод  →  чистый C#  →  .csproj  →  Mods\MyMod.dll
```

---

## Требования

| Компонент | Версия |
|---|---|
| .NET SDK | 8.0 или новее (проверено на 10.0) |
| ОС | Windows x64 |
| Игра | My Summer Car + установленный MSCLoader 1.4.x |
| NuGet | нужен один раз для `Microsoft.NETFramework.ReferenceAssemblies` |

Visual Studio **не требуется** — сборка идёт через `dotnet build`.

---

## Сборка программы

```bash
dotnet build src\MSCNodeIDE.App\MSCNodeIDE.App.csproj
```

Готовый исполняемый файл:
`src\MSCNodeIDE.App\bin\Debug\net10.0-windows\MSCNodeIDE.exe`

---

## Как пользоваться

1. **Проект → Новый проект** — выбери папку и имя мода.
   Папка игры определяется автоматически по Steam-библиотекам
   (`libraryfolders.vdf`), при необходимости поправь путь в
   **Проект → Настройки проекта**.

2. **Строй логику** на холсте:
   - двойной клик по узлу в палитре слева — добавить узел;
   - правый клик по пустому месту холста — контекстное меню со всеми узлами;
   - тяни от кружка к кружку, чтобы связать;
   - колесо мыши — масштаб, **Shift+ЛКМ** или СКМ — панорама, **F** — вписать всё;
   - выдели узел → справа в инспекторе задай значения (текст, числа, флажки, выпадающие списки, цвет).

3. **Собрать** — кнопка «Собрать + в Mods». Программа:
   - сгенерирует `<Имя>.cs` в папку `Generated\`;
   - создаст `<Имя>.csproj` и `Properties\AssemblyInfo.cs`;
   - запустит `dotnet build`;
   - скопирует DLL в `My Summer Car\Mods\`;
   - покажет ошибки компиляции с номерами строк в логе внизу окна.

4. Запусти My Summer Car, открой меню модов (Ctrl+M) — мод появится в списке.

---

## Узлы

| Категория | Что внутри |
|---|---|
| Поток выполнения | On Load, Update, On New Game, On Menu Load, On Save, If/Else, Sequence, Loop, While, Wait, Return |
| Значения и переменные | константы, Get/Set Variable (с выбором типа) |
| Математика | + − × ÷, Pow, Clamp, Lerp, MoveTowards, Abs, Sin/Cos, Random, работа с Vector3 |
| Логика | сравнения, AND / OR / NOT, Toggle |
| Строки | Concat, Contains, Substring, Replace, Split, To Float |
| Unity / Объекты | Find, GetComponent, Transform (позиция/поворот/масштаб), SetActive, Rigidbody, AddForce, Create/Destroy, ввод, время, Debug Log, Show Message |
| MSCLoader | настройки мода (Toggle/Slider/Text/Header), сохранение значений, LoadBundle, Instantiate, звук |
| PlayMaker (FSM) | `FindFsm`, Get/Set Float/Int/Bool/String, SendEvent, AddGlobalTransition, **FsmInject** |
| API игры | все статические методы из `MSCLoader.dll`, `UnityEngine.dll`, `Assembly-CSharp.dll`, `PlayMaker.dll` — по кнопке **Справка → Сканировать API игры** |

---

## Как читается сгенерированный код

Повторяющиеся и сложные подвыражения автоматически выносятся в локальные
переменные с именами от узлов — код остаётся читаемым и правится руками, если нужно:

```csharp
private void Update()
{
    float _Add__A___B_1 = (c_timer + Time.deltaTime);
    c_timer = _Add__A___B_1;

    for (int __ic1 = 0; __ic1 < 3; __ic1++)
    {
        ModConsole.Log("iteration");
    }

    bool _Get_Key_Down2 = UnityEngine.Input.GetKeyDown(KeyCode.Space);
    if (_Get_Key_Down2)
    {
        ModConsole.Log("Space pressed");
    }
}
```

---

## Особенности MSCLoader, учтённые в генераторе

Эти вещи проверены рефлексией по реальным DLL игры — код под них генерируется корректно:

* хуки в 1.4.x объявлены `internal`, поэтому `override` невозможен —
  используется `ModSetup()` + `SetupFunction(Setup.OnLoad, метод)`;
* обязательные свойства мода: `ID`, `Version`, `Author` (абстрактные);
* `Settings.AddCheckBox(settingID, name, value)` - идентификатор идёт **первым** аргументом,
  а возвращает объект настройки, значение которого читается через `GetValue()`;
* сохранение: `SaveLoad.WriteValue<T>(this, key, value)` и `SaveLoad.ReadValue<T>(this, key)`.
  Второй метод принимает **всего два аргумента**, поэтому значение по умолчанию
  подставляется через проверку `ValueExists`. Есть также списки, словари,
  сериализация классов и файлов.
* `ModLoader.GetModConfigFolder` помечен устаревшим (ошибка компиляции) —
  используется `GetModSettingsFolder`;
* PlayMaker: `PlayMakerFSM.Fsm.GetFsmFloat("X").Value`;
* корутины запускаются через собственный runner, так как `Mod`
  не наследует `MonoBehaviour`.

---

## Структура решения

```
MSCNodeIDE/
├─ src/
│  ├─ MSCNodeIDE.Core/     логика без UI
│  │  ├─ NodeGraph.cs        модель графа, порты, типы
│  │  ├─ CodegenContext.cs   генерация кода, hoisting, литералы
│  │  ├─ CodeGenerator.cs    сборка класса мода целиком
│  │  ├─ NodeLibrary.cs      реестр типов узлов
│  │  ├─ *Nodes*.cs          встроенные узлы
│  │  ├─ ApiCatalog.cs       чтение API игры (MetadataLoadContext)
│  │  ├─ ApiNodes.cs         узлы из просканированного API
│  │  ├─ ProjectManager.cs   поиск игры, генерация csproj, сборка
│  │  └─ ProjectSerializer.cs  сохранение проекта в JSON
│  └─ MSCNodeIDE.App/      интерфейс (WinForms)
│     ├─ MainForm.cs         окно, меню, сборка
│     ├─ GraphCanvas.cs      холст нод
│     ├─ NodePalette.cs      палитра с поиском
│     └─ NodeInspector.cs    инспектор свойств узла
└─ tools/
   ├─ CodegenSmokeTest/   сквозной тест: граф → код → сборка DLL
   ├─ ApiDump/            дамп реального API игры
   └─ DebugGraph/         отладка обхода графа
```

---

## Проверка

```bash
dotnet run --project tools\CodegenSmokeTest
```

Тест строит нетривиальный граф, генерирует код, создаёт csproj и собирает DLL
против DLL настоящей игры. Успешный вывод: `RESULT: SUCCESS`.

---

## Ограничения

* поддерживается **MSCLoader 1.4.x** (в нём хуки стали `internal`);
* настройки мода поддерживают базовые типы; сложные опции вроде групп
  чекбоксов и выпадающих списков пока не вынесены в палитру;
* узлы из просканированного API создаются по статическим методам —
  методы экземпляров нужно оборачивать в свои узлы;
* файлы проекта (`project.json`, `Generated\`) перезаписываются при сборке —
  правьте ноды, а не сгенерированный `.cs`.
