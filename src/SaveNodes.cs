using System;
using System.Collections.Generic;
using System.Linq;

namespace MSCNodeIDE.Core
{
    /// <summary>
    /// Сохранение данных мода через настоящий API MSCLoader.
    ///
    /// В 1.4.x есть обобщённые методы (проверено рефлексией по MSCLoader.dll):
    ///     T      ReadValue&lt;T&gt;(Mod mod, string valueID)
    ///     void   WriteValue&lt;T&gt;(Mod mod, string valueID, T value)
    ///     T[]    ReadValueAsArray&lt;T&gt;(...)      List&lt;T&gt; ReadValueAsList&lt;T&gt;(...)
    ///     Dictionary&lt;K,V&gt; ReadValueAsDictionary&lt;K,V&gt;(...)
    ///     bool   ValueExists(Mod mod, string valueID)
    ///     void   DeleteValue(Mod mod, string valueID)
    ///     void   SerializeClass&lt;T&gt;(Mod, T, string valueID, bool encrypt)
    ///     T      DeserializeClass&lt;T&gt;(Mod, string valueID, bool encrypted)
    ///     void   SerializeSaveFile&lt;T&gt;(Mod, T class, string fileName)
    ///     T      DeserializeSaveFile&lt;T&gt;(Mod, string fileName)
    ///
    /// Данные попадают в общий файл Mods.txt и переживают переустановку мода.
    /// </summary>
    internal static class SaveNodes
    {
        public const string Cat = "MSCLoader / Сохранение";

        public static void Register(NodeLibrary lib)
        {
            // ---------- ReadValue<T> ----------
            foreach (var (id, title, kind, fallback) in new[]
            {
                ("float",  "Float",  PortKind.Float,  "0f"),
                ("int",    "Int",    PortKind.Int,    "0"),
                ("bool",   "Bool",   PortKind.Bool,   "false"),
                ("string", "String", PortKind.String, "\"\""),
                ("color",  "Color",  PortKind.Color,  "Color.white"),
                ("v3",     "Vector3",PortKind.Vector3,"Vector3.zero"),
                ("v2",     "Vector2",PortKind.Vector2,"Vector2.zero"),
            })
            {
                var read = Def.Make("msc.save.read" + id, Cat, "Read Value " + title,
                        "Прочитать сохранённое значение из Mods.txt")
                    .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "myKey")
                    .AddIn("default", "По умолчанию", kind, literal: true, def_: null)
                    .AddOut("out", "Значение", kind);
                read.Description = "Работает только после загрузки сейва. " +
                                   "Для значений, которых ещё нет, вернётся default.";
                read.Emit = (c, n) =>
                {
                    c.Use("MSCLoader");
                    var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "myKey"));
                    var fb = c.ResolveInput(n, c.Port(n, "default"));
                    var clr = ClrName(id);
                    // Сигнатура ровно такая: ReadValue<T>(Mod, string) - без значения
                    // по умолчанию, поэтому проверяем наличие ключа отдельно.
                    var tmp = c.TempName("sv");
                    return $"(SaveLoad.ValueExists(this, {key}) ? SaveLoad.ReadValue<{clr}>(this, {key}) : ({clr})({fb}))";
                };
                lib.Register(read);
            }

            // ---------- WriteValue<T> ----------
            foreach (var (id, title, kind) in new[]
            {
                ("float",  "Float",   PortKind.Float),
                ("int",    "Int",     PortKind.Int),
                ("bool",   "Bool",    PortKind.Bool),
                ("string", "String",  PortKind.String),
                ("color",  "Color",   PortKind.Color),
                ("v3",     "Vector3", PortKind.Vector3),
                ("v2",     "Vector2", PortKind.Vector2),
            })
            {
                var write = Def.Make("msc.save.write" + id, Cat, "Write Value " + title,
                        "Записать значение в файл сохранения")
                    .ExecIn()
                    .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "myKey")
                    .AddIn("value", "Значение", kind, literal: true, def_: null)
                    .Exec("out", "");
                write.Description = "Обычно вызывается из хука On Save.";
                write.EmitStatements = (c, n) =>
                {
                    c.Use("MSCLoader");
                    var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "myKey"));
                    var val = c.ResolveInput(n, c.Port(n, "value"));
                    var clr = ClrName(id);
                    c.Line($"SaveLoad.WriteValue(this, {key}, ({clr})({val}));");
                    ChainOut(c, n, "out");
                };
                lib.Register(write);
            }

            // ---------- Значение есть? ----------
            var exists = Def.Make("msc.save.exists", Cat, "Value Exists",
                    "Проверить, есть ли ключ в сохранении")
                .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "myKey")
                .AddOut("out", "Есть", PortKind.Bool);
            exists.Emit = (c, n) =>
            {
                c.Use("MSCLoader");
                var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "myKey"));
                return $"SaveLoad.ValueExists(this, {key})";
            };
            lib.Register(exists);

            // ---------- Удалить ----------
            var del = Def.Make("msc.save.delete", Cat, "Delete Value", "Удалить ключ из сохранения")
                .ExecIn()
                .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "myKey")
                .Exec("out", "");
            del.EmitStatements = (c, n) =>
            {
                c.Use("MSCLoader");
                var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "myKey"));
                c.Line($"SaveLoad.DeleteValue(this, {key});");
                ChainOut(c, n, "out");
            };
            lib.Register(del);

            // ---------- Списки и словари ----------
            var listWrite = Def.Make("msc.save.writelist", Cat, "Write Value As List",
                    "Записать список чисел")
                .ExecIn()
                .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "myList")
                .AddIn("value", "Список", PortKind.Object, literal: false)
                .Exec("out", "");
            listWrite.EmitStatements = (c, n) =>
            {
                c.Use("MSCLoader");
                var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "myList"));
                var val = c.ResolveInput(n, c.Port(n, "value"));
                c.Line($"SaveLoad.WriteValue(this, {key}, {val});");
                ChainOut(c, n, "out");
            };
            lib.Register(listWrite);

            var listRead = Def.Make("msc.save.readlist", Cat, "Read Value As List",
                    "Прочитать список чисел")
                .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "myList")
                .AddOut("out", "Список", PortKind.Object);
            listRead.Emit = (c, n) =>
            {
                c.Use("MSCLoader");
                var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "myList"));
                return $"SaveLoad.ReadValueAsList<float>(this, {key})";
            };
            lib.Register(listRead);

            var dictWrite = Def.Make("msc.save.writedict", Cat, "Write Value As Dictionary",
                    "Записать словарь строк")
                .ExecIn()
                .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "myDict")
                .AddIn("value", "Словарь", PortKind.Object, literal: false)
                .Exec("out", "");
            dictWrite.EmitStatements = (c, n) =>
            {
                c.Use("MSCLoader");
                var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "myDict"));
                var val = c.ResolveInput(n, c.Port(n, "value"));
                c.Line($"SaveLoad.WriteValue(this, {key}, {val});");
                ChainOut(c, n, "out");
            };
            lib.Register(dictWrite);

            var dictRead = Def.Make("msc.save.readdict", Cat, "Read Value As Dictionary",
                    "Прочитать словарь строк")
                .AddIn("key", "Ключ", PortKind.String, literal: true, def_: "myDict")
                .AddOut("out", "Словарь", PortKind.Object);
            dictRead.Emit = (c, n) =>
            {
                c.Use("MSCLoader");
                var key = CodegenContext.Quote(Def.Key(n, "key", c.NodeDef(n), "myDict"));
                return $"SaveLoad.ReadValueAsDictionary<string, string>(this, {key})";
            };
            lib.Register(dictRead);

            // ---------- Значения настроек мода ----------
            // Settings.AddCheckBox возвращает SettingsCheckBox, значение читается GetValue().
            var getToggle = Def.Make("msc.setting.getbool", Cat, "Setting: Get Toggle Value",
                    "Прочитать значение флажка, объявленного узлом Setting: Toggle")
                .AddIn("id", "Ключ настройки (совпадает с узлом)", PortKind.String,
                       literal: true, def_: "myOption")
                .AddIn("default", "По умолчанию", PortKind.Bool, literal: true, def_: false)
                .AddOut("out", "Значение", PortKind.Bool);
            getToggle.Emit = (c, n) =>
            {
                c.Use("MSCLoader");
                var id = Def.Key(n, "id", c.NodeDef(n), "myOption");
                var fb = c.ResolveInput(n, c.Port(n, "default"));
                var full = c.SettingId(n);
                return $"(c_SettingToggle_{Sanitize(full)} != null ? c_SettingToggle_{Sanitize(full)}.GetValue() : {fb})";
            };
            lib.Register(getToggle);

            var getSlider = Def.Make("msc.setting.getfloat", Cat, "Setting: Get Slider Value",
                    "Прочитать значение ползунка, объявленного узлом Setting: Slider")
                .AddIn("id", "Ключ настройки (совпадает с узлом)", PortKind.String,
                       literal: true, def_: "mySlider")
                .AddIn("default", "По умолчанию", PortKind.Float, literal: true, def_: 0f)
                .AddOut("out", "Значение", PortKind.Float);
            getSlider.Emit = (c, n) =>
            {
                c.Use("MSCLoader");
                var fb = c.ResolveInput(n, c.Port(n, "default"));
                var full = c.SettingId(n);
                return $"(c_SettingSlider_{Sanitize(full)} != null ? c_SettingSlider_{Sanitize(full)}.GetValue() : {fb})";
            };
            lib.Register(getSlider);

            var getText = Def.Make("msc.setting.getstring", Cat, "Setting: Get Text Value",
                    "Прочитать значение текстового поля настроек")
                .AddIn("id", "Ключ настройки (совпадает с узлом)", PortKind.String,
                       literal: true, def_: "myText")
                .AddIn("default", "По умолчанию", PortKind.String, literal: true, def_: "")
                .AddOut("out", "Значение", PortKind.String);
            getText.Emit = (c, n) =>
            {
                c.Use("MSCLoader");
                var fb = c.ResolveInput(n, c.Port(n, "default"));
                var full = c.SettingId(n);
                return $"(c_SettingText_{Sanitize(full)} != null ? c_SettingText_{Sanitize(full)}.GetValue() : {fb})";
            };
            lib.Register(getText);

            // Кнопка сброса сохранений — часто нужна
            var resetBtn = Def.Make("msc.setting.saveresetbutton", Cat, "Setting: Save Reset Button",
                    "Кнопка сброса сохранений мода в меню настроек")
                .AddIn("title", "Название", PortKind.String, literal: true, def_: "Сбросить сохранения")
                .Exec("out", "");
            resetBtn.EmitStatements = (c, n) =>
            {
                c.Use("MSCLoader");
                c.Line($"Settings.AddSaveResetButton(this);");
                ChainOut(c, n, "out");
            };
            lib.Register(resetBtn);
        }

        private static string ClrName(string id) => id switch
        {
            "float" => "float",
            "int" => "int",
            "bool" => "bool",
            "string" => "string",
            "color" => "Color",
            "v3" => "Vector3",
            "v2" => "Vector2",
            _ => "float"
        };

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "v";
            var sb = new System.Text.StringBuilder();
            foreach (var ch in s)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            var r = sb.ToString();
            return r.Length == 0 ? "v" : r;
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