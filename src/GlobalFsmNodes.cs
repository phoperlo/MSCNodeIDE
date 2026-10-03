using System;
using System.Collections.Generic;
using System.Linq;

namespace MSCNodeIDE.Core
{
    /// <summary>
    /// Глобальные переменные PlayMaker — главный способ читать и менять состояние игры.
    /// Реальные моды обращаются к ним так:
    ///     FsmVariables.GlobalVariables.FindVariable("PlayerMoney") as FsmFloat
    ///     FsmVariables.GlobalVariables.FindVariable("GlobalHour")   as FsmInt
    ///     FsmVariables.GlobalVariables.FindVariable("PlayerInMenu") as FsmBool
    /// </summary>
    internal static class GlobalFsmNodes
    {
        public const string Cat = "PlayMaker / Глобальные переменные";

        public const string CommonHint =
            "Частые переменные MSC: PlayerMoney, PlayerHunger, PlayerStress, PlayerUrine, " +
            "PlayerFatigue, PlayerThirst, PlayerSweat, PlayerAlcoholism, PlayerDirtiness, " +
            "PlayerTemp, PlayerFatigueRate, PlayerStressRate, GlobalHour, ClockMinutes, " +
            "PlayerInMenu, GlobalTime";

        public static void Register(NodeLibrary lib)
        {
            foreach (var (id, title, kind, fsmType, fallback) in new[]
            {
                ("float", "Float", PortKind.Float, "FsmFloat", "0f"),
                ("int",   "Int",   PortKind.Int,   "FsmInt",   "0"),
                ("bool",  "Bool",  PortKind.Bool,  "FsmBool",  "false"),
                ("string","String",PortKind.String,"FsmString","\"\""),
            })
            {
                var get = Def.Make("pm.globalget" + id, Cat, "Global Get " + title,
                        "Прочитать глобальную переменную " + title.ToUpperInvariant())
                    .AddIn("name", "Переменная", PortKind.String, literal: true,
                           def_: id == "float" ? "PlayerMoney" : id == "int" ? "GlobalHour" :
                                 id == "bool" ? "PlayerInMenu" : "GlobalTime")
                    .AddOut("out", "Значение", kind);
                get.Description = CommonHint;
                get.Emit = (c, n) =>
                {
                    var nm = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "Variable"));
                    return $"(FsmVariables.GlobalVariables.FindVariable({nm}) as {fsmType} != null " +
                           $"? (FsmVariables.GlobalVariables.FindVariable({nm}) as {fsmType}).Value " +
                           $": ({fallback}))";
                };
                lib.Register(get);

                var set = Def.Make("pm.globalset" + id, Cat, "Global Set " + title,
                        "Записать глобальную переменную " + title.ToUpperInvariant())
                    .ExecIn()
                    .AddIn("name", "Переменная", PortKind.String, literal: true,
                           def_: id == "float" ? "PlayerMoney" : id == "int" ? "GlobalHour" :
                                 id == "bool" ? "PlayerInMenu" : "GlobalTime")
                    .AddIn("value", "Значение", kind, literal: true, def_: null)
                    .Exec("out", "");
                set.Description = CommonHint;
                set.EmitStatements = (c, n) =>
                {
                    var nm = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "Variable"));
                    var val = c.ResolveInput(n, c.Port(n, "value"));
                    c.Use("HutongGames.PlayMaker");
                    c.Line($"c_GlobalSet({nm}, {val});");
                    ChainOut(c, n, "out");
                };
                lib.Register(set);
            }

            // Игровой объект по глобальной переменной (например машина игрока)
            var getGo = Def.Make("pm.globalgetgameobject", Cat, "Global Get GameObject",
                    "Глобальная переменная типа FsmGameObject — например, текущая машина")
                .AddIn("name", "Переменная", PortKind.String, literal: true, def_: "PlayerCar")
                .AddOut("out", "Объект", PortKind.GameObject);
            getGo.Description = CommonHint;
            getGo.Emit = (c, n) =>
            {
                c.Use("HutongGames.PlayMaker");
                c.NeedsGlobalObjectReader = true;
                var nm = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "Variable"));
                var tmp = c.TempName("gvar");
                return $"(c_GlobalGetObject({nm}, out var {tmp}) ? {tmp} : null)";
            };
            lib.Register(getGo);

            // Существует ли переменная
            var exists = Def.Make("pm.globalexists", Cat, "Global Variable Exists",
                    "Проверить, объявлена ли глобальная переменная")
                .AddIn("name", "Переменная", PortKind.String, literal: true, def_: "PlayerMoney")
                .AddOut("out", "Существует", PortKind.Bool);
            exists.Emit = (c, n) =>
            {
                c.Use("HutongGames.PlayMaker");
                var nm = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "Variable"));
                return $"(FsmVariables.GlobalVariables.FindVariable({nm}) != null)";
            };
            lib.Register(exists);

            // Существующие глобальные переменные (список для отладки)
            var listAll = Def.Make("pm.globalslist", Cat, "Global Variables List",
                    "Все имена глобальных переменных (для отладки). Печатает в консоль.")
                .ExecIn()
                .Exec("out", "");
            listAll.EmitStatements = (c, n) =>
            {
                c.Use("HutongGames.PlayMaker");
                c.Line("foreach (var __v in FsmVariables.GlobalVariables.Variables)");
                c.Line("{");
                c.PushIndent();
                c.Line("    ModConsole.Log(__v.Name);");
                c.PopIndent();
                c.Line("}");
                ChainOut(c, n, "out");
            };
            lib.Register(listAll);
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