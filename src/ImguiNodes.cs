using System;
using System.Collections.Generic;
using System.Linq;

namespace MSCNodeIDE.Core
{
    /// <summary>
    /// IMGUI-узлы: окна, кнопки, ползунки. Так делают меню внутри игры —
    /// именно так написан CheatBox из реального мода MWCCheat.
    /// Раскладка ведётся переменной Y, которая растёт после каждого элемента.
    /// </summary>
    internal static class ImguiNodes
    {
        public const string Cat = "IMGUI (меню в игре)";

        public static void Register(NodeLibrary lib)
        {
            // --- окно меню ---
            // Порождает метод DrawWindow_<id>() и регистрирует его в OnGUI.
            var window = Def.Make("gui.window", Cat, "Menu Window",
                    "Окно меню. Рисуется каждый кадр в хуке OnGUI. " +
                    "Содержимое окна собирается из узлов Button/Label/Slider, " +
                    "подключённых к выходу «Содержимое».")
                .AddIn("title", "Заголовок", PortKind.String, literal: true, def_: "Меню")
                .AddIn("id", "ID окна", PortKind.Int, literal: true, def_: 100)
                .AddIn("width", "Ширина", PortKind.Float, literal: true, def_: 280f)
                .AddIn("height", "Высота", PortKind.Float, literal: true, def_: 500f)
                .AddOut("content", "Содержимое", PortKind.Execution)
                .AddOut("open", "Окно открыто", PortKind.Bool);
            window.EmitStatements = (c, n) => { };
            lib.Register(window);

            // --- кнопка ---
            var button = Def.Make("gui.button", Cat, "Button", "Кнопка. Выход «Да» срабатывает при нажатии.")
                .AddIn("y", "Y (позиция)", PortKind.Float, literal: true, def_: 10f)
                .AddIn("text", "Текст", PortKind.String, literal: true, def_: "Кнопка")
                .AddIn("width", "Ширина", PortKind.Float, literal: true, def_: 260f)
                .AddIn("height", "Высота", PortKind.Float, literal: true, def_: 28f)
                .AddOut("pressed", "Нажата", PortKind.Execution)
                .AddOut("notPressed", "Не нажата", PortKind.Execution);
            button.EmitStatements = (c, n) =>
            {
                var y = c.ResolveInput(n, c.Port(n, "y"));
                var text = c.ResolveInput(n, c.Port(n, "text"));
                var w = c.ResolveInput(n, c.Port(n, "width"));
                var h = c.ResolveInput(n, c.Port(n, "height"));
                var tmp = c.TempName("btn");
                c.Line($"bool {tmp} = GUI.Button(new Rect(10f, {y}, {w}, {h}), {text});");
                using (c.Scope($"if ({tmp})"))
                {
                    foreach (var conn in c.OutgoingAll(n, "pressed"))
                    {
                        var next = c.Graph.Find(conn.ToNode);
                        if (next != null) c.RunStatements(next, c.NodeDef(next));
                    }
                }
                using (c.Scope("else"))
                {
                    foreach (var conn in c.OutgoingAll(n, "notPressed"))
                    {
                        var next = c.Graph.Find(conn.ToNode);
                        if (next != null) c.RunStatements(next, c.NodeDef(next));
                    }
                }
                c.Line($"c_GuiY += {h} + 4f;");
            };
            lib.Register(button);

            // --- метка ---
            var label = Def.Make("gui.label", Cat, "Label", "Текстовая строка в меню")
                .AddIn("y", "Y (позиция)", PortKind.Float, literal: true, def_: 10f)
                .AddIn("text", "Текст", PortKind.String, literal: true, def_: "Текст")
                .AddIn("height", "Высота", PortKind.Float, literal: true, def_: 22f)
                .AddOut("out", "Дальше", PortKind.Execution);
            label.EmitStatements = (c, n) =>
            {
                var y = c.ResolveInput(n, c.Port(n, "y"));
                var text = c.ResolveInput(n, c.Port(n, "text"));
                var h = c.ResolveInput(n, c.Port(n, "height"));
                c.Line($"GUI.Label(new Rect(10f, {y}, {c.WidthExpr(n)}, {h}), {text});");
                c.Line($"c_GuiY += {h} + 4f;");
            };
            lib.Register(label);

            // --- ползунок ---
            var slider = Def.Make("gui.slider", Cat, "Slider", "Ползунок в меню. Значение пишется в переменную.")
                .AddIn("y", "Y (позиция)", PortKind.Float, literal: true, def_: 10f)
                .AddIn("name", "Переменная", PortKind.String, literal: true, def_: "mySliderValue")
                .AddIn("min", "Мин", PortKind.Float, literal: true, def_: 0f)
                .AddIn("max", "Макс", PortKind.Float, literal: true, def_: 10f)
                .AddOut("value", "Значение", PortKind.Float)
                .AddOut("out", "Дальше", PortKind.Execution);
            slider.EmitStatements = (c, n) =>
            {
                var y = c.ResolveInput(n, c.Port(n, "y"));
                var nm = Def.Key(n, "name", c.NodeDef(n), "mySliderValue");
                var min = c.ResolveInput(n, c.Port(n, "min"));
                var max = c.ResolveInput(n, c.Port(n, "max"));
                var field = c.VariableNameByLiteral(n, PortKind.Float);
                c.Line($"{field} = GUI.HorizontalSlider(new Rect(10f, {y}, {c.WidthExpr(n)}, 20f), " +
                      $"{field}, {min}, {max});");
                c.Line($"c_GuiY += 24f;");
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
            };
            lib.Register(slider);

            // --- чекбокс в меню ---
            var toggle = Def.Make("gui.toggle", Cat, "Toggle Button", "Переключаемая кнопка в меню")
                .AddIn("y", "Y (позиция)", PortKind.Float, literal: true, def_: 10f)
                .AddIn("name", "Переменная", PortKind.String, literal: true, def_: "myToggle")
                .AddIn("offText", "Текст выкл.", PortKind.String, literal: true, def_: "Выкл")
                .AddIn("onText", "Текст вкл.", PortKind.String, literal: true, def_: "Вкл")
                .AddOut("value", "Значение", PortKind.Bool)
                .AddOut("out", "Дальше", PortKind.Execution);
            toggle.EmitStatements = (c, n) =>
            {
                var y = c.ResolveInput(n, c.Port(n, "y"));
                var nm = Def.Key(n, "name", c.NodeDef(n), "myToggle");
                var offT = CodegenContext.Quote(Def.Key(n, "offText", c.NodeDef(n), "Выкл"));
                var onT = CodegenContext.Quote(Def.Key(n, "onText", c.NodeDef(n), "Вкл"));
                var field = c.VariableNameByLiteral(n, PortKind.Bool);
                var cond = c.TempName("tg");
                c.Line($"if (GUI.Button(new Rect(10f, {y}, {c.WidthExpr(n)}, 26f), " +
                      $"{field} ? {onT} : {offT}))");
                c.Line("{");
                c.PushIndent();
                c.Line($"    {field} = !{field};");
                c.PopIndent();
                c.Line("}");
                c.Line("c_GuiY += 30f;");
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
            };
            lib.Register(toggle);

            // --- разделитель ---
            var sep = Def.Make("gui.separator", Cat, "Separator", "Горизонтальная линия-разделитель")
                .AddIn("y", "Y (позиция)", PortKind.Float, literal: true, def_: 10f)
                .AddOut("out", "Дальше", PortKind.Execution);
            sep.EmitStatements = (c, n) =>
            {
                var y = c.ResolveInput(n, c.Port(n, "y"));
                c.Line($"GUI.Box(new Rect(10f, {y}, {c.WidthExpr(n)}, 2f), \"\");");
                c.Line("c_GuiY += 10f;");
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
            };
            lib.Register(sep);

            // --- прокрутка ---
            var scroll = Def.Make("gui.scrollbegin", Cat, "Scroll View (начало)", "Область с прокруткой")
                .AddIn("height", "Высота области", PortKind.Float, literal: true, def_: 460f)
                .AddOut("content", "Содержимое", PortKind.Execution)
                .AddOut("out", "Дальше", PortKind.Execution);
            scroll.EmitStatements = (c, n) =>
            {
                var h = c.ResolveInput(n, c.Port(n, "height"));
                c.Line("c_GuiScroll = GUI.BeginScrollView(new Rect(0f, 24f, c_GuiWidth, " +
                      $"{h}), c_GuiScroll, new Rect(0f, 0f, c_GuiWidth, 0f));");
                c.Line("c_GuiY = 8f;");
                foreach (var conn in c.OutgoingAll(n, "content"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
                c.Line("GUI.EndScrollView();");
                c.Line("GUI.DragWindow();");
            };
            lib.Register(scroll);
        }
    }
}