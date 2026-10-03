using System;
using System.Collections.Generic;
using System.Linq;

namespace MSCNodeIDE.Core
{
    /// <summary>Служебные хелперы для объявления узлов (используются и редактором).</summary>
    public static class Def
    {
        public static NodeDefinition Make(string id, string cat, string title, string desc = null)
        {
            return new NodeDefinition
            {
                Id = id,
                Category = cat,
                Title = title,
                Description = desc
            };
        }

        public static NodeDefinition AddIn(this NodeDefinition d, string id, string name,
            PortKind kind, bool literal = false, object def_ = null, string tip = null)
        {
            d.Ports.Add(new Port
            {
                Id = id,
                Name = name,
                Kind = kind,
                IsInput = true,
                IsLiteral = literal,
                LiteralValue = def_
            });
            return d;
        }

        public static NodeDefinition AddOut(this NodeDefinition d, string id, string name, PortKind kind)
        {
            d.Ports.Add(new Port
            {
                Id = id,
                Name = name,
                Kind = kind,
                IsInput = false
            });
            return d;
        }

        public static NodeDefinition Exec(this NodeDefinition d, string id = "exec", string name = "")
            => d.AddOut(id, name, PortKind.Execution);

        public static NodeDefinition ExecIn(this NodeDefinition d, string id = "exec", string name = "")
            => d.AddIn(id, name, PortKind.Execution);

        public static NodeDefinition Sel(this NodeDefinition d, string id = "sel", string name = "")
            => d.AddOut(id, name, PortKind.Execution);

        /// <summary>Добавляет enum-список значений, хранится как строка в Properties.</summary>
        public static NodeDefinition EnumIn(this NodeDefinition d, string id, string name,
            string[] options, string first = null)
        {
            d.Ports.Add(new Port
            {
                Id = id,
                Name = name,
                Kind = PortKind.String,
                IsInput = true,
                IsLiteral = true,
                LiteralValue = first ?? options.FirstOrDefault()
            });
            d.SetOptions(id, options);
            return d;
        }

        /// <summary>Опции enum хранятся в записи DefinitionId+":opt:"+portId.</summary>
        public static void SetOptions(this NodeDefinition d, string portId, string[] options)
            => OptionRegistry.Set(d.Id, portId, options);

        public static string[] Options(this NodeDefinition d, string portId)
            => OptionRegistry.Get(d.Id, portId);

        /// <summary>Публичный доступ к списку enum-опций порта (для инспектора).</summary>
        public static string[] OptionsFor(string definitionId, string portId)
            => OptionRegistry.Get(definitionId, portId);

        /// <summary>Ключ-в-выбор: читаем сохранённое значение либо дефолт.</summary>
        public static string Key(NodeInstance n, string portId, NodeDefinition def, string fallback = null)
            => n.Get("in." + portId, fallback ?? (def.Options(portId)?.FirstOrDefault() ?? ""));

        public static int KeyInt(NodeInstance n, string portId, NodeDefinition def, int fallback = 0)
            => int.TryParse(n.Get("in." + portId), out var v) ? v : fallback;

        public static float KeyFloat(NodeInstance n, string portId, NodeDefinition def, float fallback = 0f)
            => float.TryParse(n.Get("in." + portId), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public static bool KeyBool(NodeInstance n, string portId, NodeDefinition def, bool fallback = false)
            => n.Get("in." + portId) is string s
                ? (s == "True" || s == "true" || s == "1")
                : fallback;
    }

    internal static class OptionRegistry
    {
        private static readonly Dictionary<string, string[]> _map = new Dictionary<string, string[]>();

        public static void Set(string defId, string portId, string[] options)
            => _map[defId + "::" + portId] = options;

        public static string[] Get(string defId, string portId)
            => _map.TryGetValue(defId + "::" + portId, out var v) ? v : null;

        public static bool IsOption(string defId, string portId)
            => _map.ContainsKey(defId + "::" + portId);
    }

    // ============ FLOW ============

    internal static class FlowNodes
    {
        public const string Cat = "Поток выполнения";

        public static void Register(NodeLibrary lib)
        {
            // Точки входа — корневые узлы, запускают цепочку выполнения.
            // Хуки internal в MSCLoader 1.4.x, поэтому регистрируются через SetupFunction.
            void Entry(string id, string title, string desc)
            {
                var n = Def.Make(id, Cat, title, desc).Exec("out");
                n.EmitStatements = (c, node) => { };
                n.Ports.First().Name = "";
                lib.Register(n);
            }

            Entry("flow.onload", "On Load", "Выполняется после загрузки игры");
            Entry("flow.update", "Update", "Выполняется каждый кадр");
            Entry("flow.onnewgame", "On New Game", "Новая игра / загрузка сейва");
            Entry("flow.onmenuload", "On Menu Load", "Возврат в главное меню");
            Entry("flow.onsave", "On Save", "При сохранении игры");
            Entry("flow.ongui", "On GUI", "Рисование GUI каждый кадр — используется для меню");
            Entry("flow.fixedupdate", "Fixed Update", "Шаг физики (50 раз в секунду)");
            Entry("flow.postload", "Post Load", "После загрузки всех модов");

            // Последовательность: выполняет все подключённые ветки по порядку
            var seq = Def.Make("flow.sequence", Cat, "Sequence", "Выполнить всё по порядку")
                .ExecIn().Exec("out");
            seq.Ports.Find(p => p.Id == "out" && !p.IsInput).Name = "";
            seq.EmitStatements = (c, n) =>
            {
                var me = c.CurrentNode(n);
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
            };
            lib.Register(seq);

            // Ветвление
            var branch = Def.Make("flow.branch", Cat, "If / Else", "Выполнить ветку по условию")
                .ExecIn()
                .AddIn("cond", "Условие", PortKind.Bool, literal: true, def_: false)
                .Sel("true", "Если да")
                .Sel("false", "Если нет")
                .Exec("out", "");
            branch.EmitStatements = (c, n) =>
            {
                var cond = c.ResolveInput(n, c.Port(n, "cond"));
                using (c.Scope($"if ({cond})"))
                {
                    foreach (var conn in c.OutgoingAll(n, "true"))
                    {
                        var next = c.Graph.Find(conn.ToNode);
                        if (next != null) c.RunStatements(next, c.NodeDef(next));
                    }
                }
                if (c.OutgoingAll(n, "false").Any())
                {
                    using (c.Scope("else"))
                    {
                        foreach (var conn in c.OutgoingAll(n, "false"))
                        {
                            var next = c.Graph.Find(conn.ToNode);
                            if (next != null) c.RunStatements(next, c.NodeDef(next));
                        }
                    }
                }
            };
            lib.Register(branch);

            // Цикл
            var loop = Def.Make("flow.loop", Cat, "Loop", "Повторить N раз")
                .ExecIn()
                .AddIn("count", "Повторов", PortKind.Int, literal: true, def_: 1)
                .AddIn("i", "Индекс (i)", PortKind.Int)
                .Exec("body", "Тело")
                .Exec("out", "");
            loop.EmitStatements = (c, n) =>
            {
                var count = c.ResolveInput(n, c.Port(n, "count"));
                var idx = "__i" + n.Id;
                c.Line($"for (int {idx} = 0; {idx} < {count}; {idx}++)");
                c.Line("{");
                c.PushIndent();
                c.SetLoopIndex(n, idx);
                foreach (var conn in c.OutgoingAll(n, "body"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
                c.PopIndent();
                c.Line("}");
                c.ClearLoopIndex(n);
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
            };
            lib.Register(loop);

            // While
            var whileNode = Def.Make("flow.while", Cat, "While", "Пока условие истинно")
                .ExecIn()
                .AddIn("cond", "Условие", PortKind.Bool, literal: true, def_: false)
                .Exec("body", "Тело")
                .Exec("out", "");
            whileNode.EmitStatements = (c, n) =>
            {
                var cond = c.ResolveInput(n, c.Port(n, "cond"));
                c.Line($"while ({cond})");
                c.Line("{");
                c.PushIndent();
                foreach (var conn in c.OutgoingAll(n, "body"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
                c.PopIndent();
                c.Line("}");
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
            };
            lib.Register(whileNode);

            // Задержка
            var wait = Def.Make("flow.wait", Cat, "Wait Seconds", "Подождать N секунд")
                .ExecIn()
                .AddIn("seconds", "Секунды", PortKind.Float, literal: true, def_: 1f)
                .Exec("out", "");
            wait.EmitStatements = (c, n) =>
            {
                var sec = c.ResolveInput(n, c.Port(n, "seconds"));
                // Mod не наследует MonoBehaviour, поэтому корутину запускаем
                // через собственный runner-объект.
                c.Line($"c_StartCoroutine(c_SecondWait({sec}));");
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
            };
            lib.Register(wait);

            // Возврат из метода
            var ret = Def.Make("flow.return", Cat, "Return", "Выйти из текущего метода")
                .ExecIn();
            ret.EmitStatements = (c, n) => c.Line("return;");
            lib.Register(ret);
        }
    }
}