using System;
using System.Collections.Generic;
using System.Linq;

namespace MSCNodeIDE.Core
{
    internal static class MscLoaderNodes
    {
        public const string Cat = "MSCLoader";

        public static void Register(NodeLibrary lib)
        {
            // ---------- Настройки мода ----------

            var addToggle = Def.Make("msc.settings.toggle", Cat, "Setting: Toggle", "Чекбокс в настройках мода")
                .EnumIn("title", "Название", new[] { "Новая опция" }, "Новая опция")
                .AddIn("name", "Ключ", PortKind.String, literal: true, def_: "myOption")
                .AddIn("default", "По умолчанию", PortKind.Bool, literal: true, def_: false);
            lib.Register(addToggle);

            var addSlider = Def.Make("msc.settings.slider", Cat, "Setting: Slider", "Ползунок в настройках мода")
                .EnumIn("title", "Название", new[] { "Новая опция" }, "Новая опция")
                .AddIn("name", "Ключ", PortKind.String, literal: true, def_: "mySlider")
                .AddIn("min", "Мин", PortKind.Float, literal: true, def_: 0f)
                .AddIn("max", "Макс", PortKind.Float, literal: true, def_: 10f)
                .AddIn("default", "По умолчанию", PortKind.Float, literal: true, def_: 1f);
            lib.Register(addSlider);

            var addText = Def.Make("msc.settings.text", Cat, "Setting: Text", "Текстовая строка в настройках")
                .EnumIn("title", "Название", new[] { "Новая опция" }, "Новая опция")
                .AddIn("name", "Ключ", PortKind.String, literal: true, def_: "myText")
                .AddIn("default", "По умолчанию", PortKind.String, literal: true, def_: "");
            lib.Register(addText);

            var addHeader = Def.Make("msc.settings.header", Cat, "Setting: Header", "Заголовок в настройках")
                .EnumIn("title", "Заголовок", new[] { "Раздел" }, "Раздел");
            lib.Register(addHeader);

            var addButton = Def.Make("msc.settings.button", Cat, "Setting: Button", "Кнопка в настройках")
                .EnumIn("title", "Название", new[] { "Кнопка" }, "Кнопка")
                .AddIn("action", "Действие", PortKind.String, literal: true, def_: "onButton");
            lib.Register(addButton);

            // ---------- Хранилище значений ----------
            // В MSCLoader 1.4.x нет SaveLoad.SaveValue: данные хранятся в ES2-файле
            // мода (Mods.txt) и в настройках Settings. Поэтому узлы работают
            // через собственные файлы в папке мода — это стабильный и документированный путь.

            var saveGet = Def.Make("msc.save.get", Cat, "Load Value From File", "Прочитать float из файла мода")
                .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "key")
                .AddIn("default", "По умолчанию", PortKind.Float, literal: true, def_: 0f)
                .AddOut("out", "Значение", PortKind.Float);
            saveGet.Emit = (c, n) =>
            {
                c.Use("System.Globalization");
                var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "key"));
                var fb = c.ResolveInput(n, c.Port(n, "default"));
                var tmp = c.TempName("ld");
                return $"(c_LoadFloat({key}, {fb}))";
            };
            lib.Register(saveGet);

            var saveSet = Def.Make("msc.save.set", Cat, "Save Value To File", "Записать float в файл мода")
                .ExecIn()
                .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "key")
                .AddIn("value", "Значение", PortKind.Float, literal: true, def_: 0f)
                .Exec("out", "");
            saveSet.EmitStatements = (c, n) =>
            {
                c.Use("System.Globalization");
                var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "key"));
                var val = c.ResolveInput(n, c.Port(n, "value"));
                c.Line($"c_SaveFloat({key}, {val});");
                ChainOut(c, n, "out");
            };
            lib.Register(saveSet);

            var saveSetTyped = Def.Make("msc.save.settyped", Cat, "Save Typed Value", "Записать значение выбранного типа")
                .ExecIn()
                .EnumIn("type", "Тип", new[] { "Float", "Int", "Bool", "String" }, "Float")
                .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "key")
                .AddIn("value", "Значение", PortKind.Float, literal: true, def_: 0f)
                .Exec("out", "");
            saveSetTyped.EmitStatements = (c, n) =>
            {
                c.Use("System.Globalization");
                var t = Def.Key(n, "type", c.NodeDef(n), "Float");
                var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "key"));
                var val = c.ResolveInput(n, c.Port(n, "value"));
                var kind = t == "Int" ? PortKind.Int
                         : t == "Bool" ? PortKind.Bool
                         : t == "String" ? PortKind.String : PortKind.Float;
                var coerced = c.CoercePublic(val, kind);
                var toStr = kind == PortKind.String
                    ? coerced
                    : $"{coerced}.ToString(System.Globalization.CultureInfo.InvariantCulture)";
                c.Line($"c_SaveText({key}, {toStr});");
                ChainOut(c, n, "out");
            };
            lib.Register(saveSetTyped);

            var saveGetTyped = Def.Make("msc.save.gettyped", Cat, "Load Typed Value", "Прочитать значение выбранного типа")
                .EnumIn("type", "Тип", new[] { "Float", "Int", "Bool", "String" }, "Float")
                .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "key")
                .AddIn("default", "По умолчанию", PortKind.Float, literal: true, def_: 0f)
                .AddOut("out", "Значение", PortKind.Float);
            saveGetTyped.Emit = (c, n) =>
            {
                c.Use("System.Globalization");
                var t = Def.Key(n, "type", c.NodeDef(n), "Float");
                var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "key"));
                var fb = c.ResolveInput(n, c.Port(n, "default"));
                if (t == "String")
                    return $"c_LoadText({key}, {CodegenContext.Quote(fb.Trim('"'))})";
                var tmp = c.TempName("ld");
                var parsed = t == "Int" ? "int.TryParse" : t == "Bool" ? "bool.TryParse" : "float.TryParse";
                return $"(c_LoadText({key}, null) is string {tmp} && {parsed}({tmp}, out var __v) ? __v : {fb})";
            };
            lib.Register(saveGetTyped);

            var saveDelete = Def.Make("msc.save.delete", Cat, "Delete Value", "Удалить ключ из файла мода")
                .ExecIn()
                .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "key")
                .Exec("out", "");
            saveDelete.EmitStatements = (c, n) =>
            {
                var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "key"));
                c.Line($"c_SaveText({key}, null);");
                ChainOut(c, n, "out");
            };
            lib.Register(saveDelete);

            // ---------- Мод-инфо ----------

            var modName = Def.Make("msc.modname", Cat, "Get Mod Name", "Название мода")
                .AddOut("out", "Имя", PortKind.String);
            modName.Emit = (c, n) => "ModLoader.ModName";
            lib.Register(modName);

            var modVersion = Def.Make("msc.modversion", Cat, "Get Mod Version", "Версия мода")
                .AddOut("out", "Версия", PortKind.String);
            modVersion.Emit = (c, n) => "ModLoader.ModVersion";
            lib.Register(modVersion);

            var isModPresent = Def.Make("msc.ismodpresent", Cat, "Is Mod Present", "Установлен ли другой мод")
                .AddIn("id", "ID мода", PortKind.String, literal: true, def_: "modID")
                .AddOut("out", "Установлен", PortKind.Bool);
            isModPresent.Emit = (c, n) =>
            {
                c.Use("MSCLoader");
                return $"ModLoader.IsModPresent({CodegenContext.Quote(Def.Key(n, "id", c.NodeDef(n), ""))})";
            };
            lib.Register(isModPresent);

            // ---------- Ассеты ----------

            var loadBundle = Def.Make("msc.loadbundle", Cat, "Load AssetBundle", "Загрузить префаб из бандла")
                .ExecIn()
                .AddIn("bundle", "Путь к бандлу", PortKind.String, literal: true, def_: "mod.assets")
                .AddIn("asset", "Имя ассета", PortKind.String, literal: true, def_: "myPrefab")
                .AddOut("out", "Объект", PortKind.GameObject)
                .Exec("execOut", "Дальше");
            loadBundle.EmitStatements = (c, n) =>
            {
                c.Use("MSCLoader");
                c.Use("UnityEngine");
                var bundle = CodegenContext.Quote(Def.Key(n, "bundle", c.NodeDef(n), ""));
                var asset = CodegenContext.Quote(Def.Key(n, "asset", c.NodeDef(n), ""));
                var tmp = c.TempName("bundle");
                var tmpObj = c.TempName("asset");
                c.Line($"var {tmp} = ModLoader.LoadAssets.LoadBundle({bundle});");
                c.Line($"GameObject {tmpObj} = null;");
                c.Line($"if ({tmp} != null)");
                c.Line("{");
                c.PushIndent();
                c.Line($"var __a = {tmp}.LoadAsset<GameObject>({asset});");
                c.Line($"if (__a != null) {tmpObj} = UnityEngine.Object.Instantiate(__a);");
                c.PopIndent();
                c.Line("}");
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.SetExternalValue(next, "in.go", tmpObj);
                }
                ChainOut(c, n, "execOut");
            };
            lib.Register(loadBundle);

            var instantiate = Def.Make("msc.instantiate", Cat, "Instantiate", "Object.Instantiate")
                .ExecIn()
                .AddIn("source", "Оригинал", PortKind.GameObject, literal: false)
                .AddIn("position", "Позиция", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddIn("rotation", "Поворот", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddIn("name", "Имя", PortKind.String, literal: true, def_: "Clone")
                .AddOut("out", "Клон", PortKind.GameObject)
                .Exec("execOut", "Дальше");
            instantiate.EmitStatements = (c, n) =>
            {
                c.Use("UnityEngine");
                var src = c.ResolveInput(n, c.Port(n, "source"));
                var pos = c.ResolveInput(n, c.Port(n, "pos"));
                var rot = c.ResolveInput(n, c.Port(n, "rot"));
                var nm = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "Clone"));
                var tmp = c.TempName("inst");
                c.Line($"GameObject {tmp} = null;");
                c.Line($"if ({src} != null)");
                c.Line("{");
                c.PushIndent();
                c.Line($"{tmp} = UnityEngine.Object.Instantiate({src}, {pos}, Quaternion.Euler({rot}));");
                c.Line($"{tmp}.name = {nm};");
                c.PopIndent();
                c.Line("}");
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.SetExternalValue(next, "in.go", tmp);
                }
                ChainOut(c, n, "execOut");
            };
            lib.Register(instantiate);

            // ---------- Звук ----------

            var playSound = Def.Make("msc.playsound", Cat, "Play Sound", "Воспроизвести 3D-звук")
                .ExecIn()
                .AddIn("file", "Файл (wav/ogg/mp3)", PortKind.String, literal: true, def_: "sound.ogg")
                .AddIn("volume", "Громкость", PortKind.Float, literal: true, def_: 1f)
                .AddIn("pitch", "Высота", PortKind.Float, literal: true, def_: 1f)
                .AddIn("position", "Позиция", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddIn("spatialize", "3D-звук", PortKind.Bool, literal: true, def_: false)
                .Exec("out", "");
            playSound.EmitStatements = (c, n) =>
            {
                c.Use("MSCLoader");
                c.Use("UnityEngine");
                var file = CodegenContext.Quote(Def.Key(n, "file", c.NodeDef(n), "sound.ogg"));
                var vol = c.ResolveInput(n, c.Port(n, "volume"));
                var pitch = c.ResolveInput(n, c.Port(n, "pitch"));
                var pos = c.ResolveInput(n, c.Port(n, "position"));
                var spat = c.ResolveInput(n, c.Port(n, "spatialize"));
                c.Line($"c_PlaySound({file}, {pos}, {vol}, {pitch}, {spat});");
                ChainOut(c, n, "out");
            };
            lib.Register(playSound);

            var stopAll = Def.Make("msc.stopallaudio", Cat, "Stop All Audio", "Остановить всё")
                .ExecIn()
                .Exec("out", "");
            stopAll.EmitStatements = (c, n) =>
            {
                c.Use("MSCLoader");
                c.Line("AudioListener.pause = false;");
                ChainOut(c, n, "out");
            };
            lib.Register(stopAll);

            // ---------- Прочее ----------

            var showLoading = Def.Make("msc.showloading", Cat, "Show Loading Bar", "Прогресс загрузки")
                .ExecIn()
                .AddIn("progress", "Прогресс 0..1", PortKind.Float, literal: true, def_: 0f)
                .AddIn("text", "Текст", PortKind.String, literal: true, def_: "Загрузка")
                .Exec("out", "");
            showLoading.EmitStatements = (c, n) =>
            {
                c.Use("MSCLoader");
                var p = c.ResolveInput(n, c.Port(n, "progress"));
                var t = CodegenContext.Quote(Def.Key(n, "text", c.NodeDef(n), "Загрузка"));
                c.Line($"ModUI.UpdateProgressBar({p}, {t});");
                ChainOut(c, n, "out");
            };
            lib.Register(showLoading);

            var reloadMods = Def.Make("msc.reloadmods", Cat, "Reload Mods", "Перезагрузить моды (в меню)")
                .ExecIn()
                .Exec("out", "");
            reloadMods.EmitStatements = (c, n) =>
            {
                c.Line("ModLoader.ReloadMods();");
                ChainOut(c, n, "out");
            };
            lib.Register(reloadMods);
        }

        static void ChainOut(CodegenContext c, NodeInstance n, string port)
        {
            foreach (var conn in c.OutgoingAll(n, port))
            {
                var next = c.Graph.Find(conn.ToNode);
                if (next != null) c.RunStatements(next, c.NodeDef(next));
            }
        }
    }

/// <summary>
    /// Узлы для работы с PlayMaker FSM — главный механизм чтения состояния игры в MSC.
    /// Реальный API (проверен рефлексией по PlayMaker.dll и MSCLoader.dll 1.4.2):
    ///   PlayMakerFSM.Fsm            -> HutongGames.PlayMaker.Fsm
    ///   Fsm.GetFsmFloat("Money")    -> FsmFloat, значение в .Value
    ///   Fsm.SendEvent("EVENT")
    /// MSCLoader даёт удобные расширения: GetPlayMaker(name), FsmInject(state, hook).
    /// </summary>
    internal static class PlayMakerNodes
    {
        public const string Cat = "PlayMaker (FSM)";

        public static void Register(NodeLibrary lib)
        {
            // --- получение FSM ---

            var findFsm = Def.Make("pm.findfsm", Cat, "Get Fsm From Object",
                    "PlayMakerFSM.Fsm указанного объекта")
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddOut("out", "Fsm", PortKind.Object);
            findFsm.Emit = (c, n) =>
            {
                c.Use("HutongGames.PlayMaker");
                var go = c.ResolveInput(n, c.Port(n, "go"));
                var tmp = c.TempName("pm");
                return $"({go} != null ? ({go}.GetComponent<PlayMakerFSM>()?.Fsm) : null)";
            };
            lib.Register(findFsm);

            // Получить именованный FSM объекта (например "Gameplay" у PLAYER)
            var getNamed = Def.Make("pm.getnamedfsm", Cat, "Get Named Fsm (MSCLoader)",
                    "Расширение MSCLoader: объект.GetPlayMaker(\"Gameplay\").Fsm")
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddIn("name", "Имя FSM", PortKind.String, literal: true, def_: "Gameplay")
                .AddOut("out", "Fsm", PortKind.Object);
            getNamed.Emit = (c, n) =>
            {
                c.Use("MSCLoader");
                c.Use("HutongGames.PlayMaker");
                var go = c.ResolveInput(n, c.Port(n, "go"));
                var nm = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "Gameplay"));
                return $"({go} != null ? {go}.GetPlayMaker({nm})?.Fsm : null)";
            };
            lib.Register(getNamed);

            // Отправить событие в FSM
            var sendEvent = Def.Make("pm.sendevent", Cat, "Send Fsm Event",
                    "Fsm.SendEvent(\"ИМЯ\") — переключает состояние игры")
                .ExecIn()
                .AddIn("fsm", "Fsm", PortKind.Object, literal: false)
                .AddIn("event", "Событие", PortKind.String, literal: true, def_: "EVENT")
                .Exec("out", "");
            sendEvent.EmitStatements = (c, n) =>
            {
                var fsm = c.ResolveInput(n, c.Port(n, "fsm"));
                var ev = CodegenContext.Quote(Def.Key(n, "event", c.NodeDef(n), "EVENT"));
                c.Line($"if ({fsm} != null) {fsm}.SendEvent({ev});");
                ChainOut(c, n, "out");
            };
            lib.Register(sendEvent);

            // Глобальный переход (MSCLoader)
            var globalTransition = Def.Make("pm.addglobaltransition", Cat, "Add Global Transition",
                    "Добавить глобальный переход: событие -> состояние")
                .ExecIn()
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddIn("event", "Событие", PortKind.String, literal: true, def_: "EVENT")
                .AddIn("state", "Состояние", PortKind.String, literal: true, def_: "STATE")
                .Exec("out", "");
            globalTransition.EmitStatements = (c, n) =>
            {
                c.Use("MSCLoader");
                c.Use("HutongGames.PlayMaker");
                var go = c.ResolveInput(n, c.Port(n, "go"));
                var ev = CodegenContext.Quote(Def.Key(n, "event", c.NodeDef(n), "EVENT"));
                var st = CodegenContext.Quote(Def.Key(n, "state", c.NodeDef(n), "STATE"));
                var pm = c.TempName("pmfs");
                c.Line($"var {pm} = {go} != null ? {go}.GetComponent<PlayMakerFSM>() : null;");
                c.Line($"if ({pm} != null) {pm}.AddGlobalTransition({ev}, {st});");
                ChainOut(c, n, "out");
            };
            lib.Register(globalTransition);

            // Внедрение в состояние FSM (позволяет вмешиваться в логику игры)
            var fsmInject = Def.Make("pm.fsminject", Cat, "Inject Into Fsm State",
                    "Выполнить код при входе в состояние FSM")
                .ExecIn()
                .AddIn("go", "Объект", PortKind.GameObject, literal: false)
                .AddIn("fsmName", "Имя FSM", PortKind.String, literal: true, def_: "Gameplay")
                .AddIn("state", "Состояние", PortKind.String, literal: true, def_: "STATE")
                .AddIn("everyFrame", "Каждый кадр", PortKind.Bool, literal: true, def_: false)
                .Exec("out", "");
            fsmInject.EmitStatements = (c, n) =>
            {
                c.Use("MSCLoader");
                c.Use("HutongGames.PlayMaker");
                var go = c.ResolveInput(n, c.Port(n, "go"));
                var fsmName = CodegenContext.Quote(Def.Key(n, "fsmName", c.NodeDef(n), "Gameplay"));
                var state = CodegenContext.Quote(Def.Key(n, "state", c.NodeDef(n), "STATE"));
                var every = c.ResolveInput(n, c.Port(n, "everyFrame"));
                var handler = c.TempName("hook");
                var body = c.GetInjectedBody(n, "execOut");
                c.Line($"System.Action {handler} = () =>");
                c.Line("{");
                c.PushIndent();
                c.Raw(body);
                c.PopIndent();
                c.Line("};");
                c.Line($"{go}.FsmInject({fsmName}, {state}, {handler}, {every});");
                ChainOut(c, n, "execOut");
            };
            lib.Register(fsmInject);

            // --- переменные FSM, сгенерированные по типам ---

            AddFsmReader(lib, "float", "Float", PortKind.Float, "float");
            AddFsmReader(lib, "int", "Int", PortKind.Int, "int");
            AddFsmReader(lib, "bool", "Bool", PortKind.Bool, "bool");
            AddFsmReader(lib, "string", "String", PortKind.String, "string");
        }

        /// <summary>
        /// Пара узлов Get/Set для одного типа переменной FSM.
        /// Чтение: Fsm.GetFsmFloat("X").Value — но переменной может не быть,
        /// поэтому генерируем безопасную проверку через переменную Fsm* напрямую.
        /// </summary>
        private static void AddFsmReader(NodeLibrary lib, string lower, string title,
            PortKind kind, string clrType)
        {
            var id = "pm.getfsm" + lower;
            var get = Def.Make(id, Cat, "Fsm Get " + title,
                    $"Прочитать переменную FSM типа {title.ToUpperInvariant()}")
                .AddIn("fsm", "Fsm", PortKind.Object, literal: false)
                .AddIn("name", "Переменная", PortKind.String, literal: true, def_: "Variable")
                .AddOut("out", "Значение", PortKind.Object);
            get.Emit = (c, n) =>
            {
                var fsm = c.ResolveInput(n, c.Port(n, "fsm"));
                var nm = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "Variable"));
                var tmp = c.TempName("v");
                var fallback = kind == PortKind.Bool ? "false"
                             : kind == PortKind.String ? "\"\"" : "0";
                return $"({fsm} != null && {fsm}.GetFsm{title}(\"{nm}\") is {clrType} {tmp} ? {tmp}.Value : ({clrType}){fallback})";
            };
            lib.Register(get);

            var set = Def.Make("pm.setfsm" + lower, Cat, "Fsm Set " + title,
                    $"Записать переменную FSM типа {title.ToUpperInvariant()}")
                .ExecIn()
                .AddIn("fsm", "Fsm", PortKind.Object, literal: false)
                .AddIn("name", "Переменная", PortKind.String, literal: true, def_: "Variable")
                .AddIn("value", "Значение", PortKind.Object, literal: false)
                .Exec("out", "");
            set.EmitStatements = (c, n) =>
            {
                var fsm = c.ResolveInput(n, c.Port(n, "fsm"));
                var nm = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "Variable"));
                var val = c.ResolveInput(n, c.Port(n, "value"));
                var tmp = c.TempName("fsmv");
                c.Line($"if ({fsm} != null)");
                c.Line("{");
                c.PushIndent();
                c.Line($"var {tmp} = {fsm}.GetFsm{title}({nm});");
                c.Line($"if ({tmp} != null) {tmp}.Value = {val};");
                c.PopIndent();
                c.Line("}");
                ChainOut(c, n, "out");
            };
            lib.Register(set);
        }

        static void ChainOut(CodegenContext c, NodeInstance n, string port)
        {
            foreach (var conn in c.OutgoingAll(n, port))
            {
                var next = c.Graph.Find(conn.ToNode);
                if (next != null) c.RunStatements(next, c.NodeDef(next));
            }
        }
    }

}
