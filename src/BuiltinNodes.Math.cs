using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MSCNodeIDE.Core
{
    internal static class MathNodes
    {
        public const string Cat = "Математика";

        private static NodeDefinition Binary(string id, string title, string op, string tip = null)
        {
            var d = Def.Make(id, Cat, title, tip)
                .AddIn("a", "A", PortKind.Float, literal: true, def_: 0f)
                .AddIn("b", "B", PortKind.Float, literal: true, def_: 0f)
                .AddOut("out", "Результат", PortKind.Float);
            d.Emit = (c, n) =>
            {
                var a = c.ResolveInput(n, c.Port(n, "a"));
                var b = c.ResolveInput(n, c.Port(n, "b"));
                return $"({a} {op} {b})";
            };
            return d;
        }

        public static void Register(NodeLibrary lib)
        {
            lib.Register(Binary("math.add", "Add (A + B)", "+"));
            lib.Register(Binary("math.sub", "Subtract (A - B)", "-"));
            lib.Register(Binary("math.mul", "Multiply (A * B)", "*"));
            lib.Register(Binary("math.div", "Divide (A / B)", "/", "Деление на ноль даёт float.PositiveInfinity — используйте Check Divide если нужно"));
            lib.Register(Binary("math.mod", "Modulo (A % B)", "%"));
            lib.Register(Binary("math.pow", "Power (A ^ B)", "^", "Использует Mathf.Pow"));

            var clamp = Def.Make("math.clamp", Cat, "Clamp", "Ограничить значение min..max")
                .AddIn("value", "Значение", PortKind.Float, literal: true, def_: 0f)
                .AddIn("min", "Мин", PortKind.Float, literal: true, def_: 0f)
                .AddIn("max", "Макс", PortKind.Float, literal: true, def_: 1f)
                .AddOut("out", "Результат", PortKind.Float);
            clamp.Emit = (c, n) => $"Mathf.Clamp({c.ResolveInput(n, c.Port(n, "value"))}, " +
                                  $"{c.ResolveInput(n, c.Port(n, "min"))}, {c.ResolveInput(n, c.Port(n, "max"))})";
            lib.Register(clamp);

            var lerp = Def.Make("math.lerp", Cat, "Lerp", "Интерполяция от A к B")
                .AddIn("a", "Откуда", PortKind.Float, literal: true, def_: 0f)
                .AddIn("b", "Куда", PortKind.Float, literal: true, def_: 1f)
                .AddIn("t", "T", PortKind.Float, literal: true, def_: 0f)
                .AddOut("out", "Результат", PortKind.Float);
            lerp.Emit = (c, n) => $"Mathf.Lerp({c.ResolveInput(n, c.Port(n, "a"))}, " +
                                 $"{c.ResolveInput(n, c.Port(n, "b"))}, {c.ResolveInput(n, c.Port(n, "t"))})";
            lib.Register(lerp);

            var moveTowards = Def.Make("math.movetowards", Cat, "Move Towards", "Двигать значение к цели с шагом")
                .AddIn("current", "Текущее", PortKind.Float, literal: true, def_: 0f)
                .AddIn("target", "Цель", PortKind.Float, literal: true, def_: 0f)
                .AddIn("step", "Шаг", PortKind.Float, literal: true, def_: 0.01f)
                .AddOut("out", "Результат", PortKind.Float);
            moveTowards.Emit = (c, n) => $"Mathf.MoveTowards({c.ResolveInput(n, c.Port(n, "current"))}, " +
                                        $"{c.ResolveInput(n, c.Port(n, "target"))}, {c.ResolveInput(n, c.Port(n, "step"))})";
            lib.Register(moveTowards);

            NodeDefinition simple2(string name, string code, string tip)
            {
                var d = Def.Make("math." + name.ToLowerInvariant(), Cat, name, tip)
                    .AddIn("value", "Значение", PortKind.Float, literal: true, def_: 0f)
                    .AddOut("out", "Результат", PortKind.Float);
                d.Emit = (c, n) => $"{code}({c.ResolveInput(n, c.Port(n, "value"))})";
                return d;
            }

            lib.Register(simple2("Abs", "Mathf.Abs", "Модуль числа"));
            lib.Register(simple2("Sqrt", "Mathf.Sqrt", "Квадратный корень"));
            lib.Register(simple2("Sin", "Mathf.Sin", null));
            lib.Register(simple2("Cos", "Mathf.Cos", null));
            lib.Register(simple2("Tan", "Mathf.Tan", null));
            lib.Register(simple2("Floor", "Mathf.Floor", "Округлить вниз"));
            lib.Register(simple2("Ceil", "Mathf.Ceil", "Округлить вверх"));
            lib.Register(simple2("Round", "Mathf.Round", "Округлить"));
            lib.Register(simple2("Sign", "Mathf.Sign", "Знак: -1, 0, 1"));
            lib.Register(simple2("Asin", "Mathf.Asin", null));
            lib.Register(simple2("Acos", "Mathf.Acos", null));
            lib.Register(simple2("Atan", "Mathf.Atan", null));

            var randomRange = Def.Make("math.randomrange", Cat, "Random Range", "Случайное число в диапазоне")
                .AddIn("min", "Мин", PortKind.Float, literal: true, def_: 0f)
                .AddIn("max", "Макс", PortKind.Float, literal: true, def_: 1f)
                .AddOut("out", "Результат", PortKind.Float);
            randomRange.Emit = (c, n) => $"UnityEngine.Random.Range({c.ResolveInput(n, c.Port(n, "min"))}, " +
                                         $"{c.ResolveInput(n, c.Port(n, "max"))})";
            lib.Register(randomRange);

            var randomInt = Def.Make("math.randomint", Cat, "Random Int", "Случайное целое [min; max)")
                .AddIn("min", "Мин", PortKind.Int, literal: true, def_: 0)
                .AddIn("max", "Макс (не включ.)", PortKind.Int, literal: true, def_: 10)
                .AddOut("out", "Результат", PortKind.Int);
            randomInt.Emit = (c, n) => $"UnityEngine.Random.Range({c.ResolveInput(n, c.Port(n, "min"))}, " +
                                        $"{c.ResolveInput(n, c.Port(n, "max"))})";
            lib.Register(randomInt);

            var randomValue = Def.Make("math.randomvalue", Cat, "Random Value", "Случайное число 0..1")
                .AddOut("out", "Результат", PortKind.Float);
            randomValue.Emit = (c, n) => "UnityEngine.Random.value";
            lib.Register(randomValue);

            var checkDiv = Def.Make("math.checkdiv", Cat, "Check Divide", "Деление, если делитель не ноль")
                .AddIn("a", "A", PortKind.Float, literal: true, def_: 0f)
                .AddIn("b", "B", PortKind.Float, literal: true, def_: 1f)
                .AddOut("value", "Значение", PortKind.Float)
                .AddOut("valid", "Успешно", PortKind.Bool);
            checkDiv.EmitStatements = (c, n) => { };
            lib.Register(checkDiv);

            var percent = Def.Make("math.percent", Cat, "Percent", "Процент от числа")
                .AddIn("value", "Значение", PortKind.Float, literal: true, def_: 100f)
                .AddIn("percent", "Процент", PortKind.Float, literal: true, def_: 50f)
                .AddOut("out", "Результат", PortKind.Float);
            percent.Emit = (c, n) => $"(({c.ResolveInput(n, c.Port(n, "value"))}) * ({c.ResolveInput(n, c.Port(n, "percent"))}) / 100f)";
            lib.Register(percent);

            // Vector3 helpers
            var v3add = Def.Make("math.v3add", Cat, "Vector3 Add", "Сложить два вектора")
                .AddIn("a", "A", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddIn("b", "B", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddOut("out", "Результат", PortKind.Vector3);
            v3add.Emit = (c, n) => $"({c.ResolveInput(n, c.Port(n, "a"))} + {c.ResolveInput(n, c.Port(n, "b"))})";
            lib.Register(v3add);

            var v3mul = Def.Make("math.v3mul", Cat, "Vector3 Scale", "Умножить вектор на число")
                .AddIn("v", "Вектор", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddIn("f", "Множитель", PortKind.Float, literal: true, def_: 1f)
                .AddOut("out", "Результат", PortKind.Vector3);
            v3mul.Emit = (c, n) => $"({c.ResolveInput(n, c.Port(n, "v"))} * {c.ResolveInput(n, c.Port(n, "f"))})";
            lib.Register(v3mul);

            var v3norm = Def.Make("math.v3normalize", Cat, "Vector3 Normalize", "Нормализовать вектор")
                .AddIn("v", "Вектор", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddOut("out", "Результат", PortKind.Vector3);
            v3norm.Emit = (c, n) => $"{c.ResolveInput(n, c.Port(n, "v"))}.normalized";
            lib.Register(v3norm);

            var dist = Def.Make("math.v3distance", Cat, "Vector3 Distance", "Расстояние между точками")
                .AddIn("a", "A", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddIn("b", "B", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddOut("out", "Расстояние", PortKind.Float);
            dist.Emit = (c, n) => $"Vector3.Distance({c.ResolveInput(n, c.Port(n, "a"))}, " +
                                  $"{c.ResolveInput(n, c.Port(n, "b"))})";
            lib.Register(dist);

            var magnitude = Def.Make("math.v3magnitude", Cat, "Vector3 Magnitude", "Длина вектора")
                .AddIn("v", "Вектор", PortKind.Vector3, literal: true, def_: "0,0,0")
                .AddOut("out", "Длина", PortKind.Float);
            magnitude.Emit = (c, n) => $"{c.ResolveInput(n, c.Port(n, "v"))}.magnitude";
            lib.Register(magnitude);
        }
    }

    internal static class LogicNodes
    {
        public const string Cat = "Логика";

        public static void Register(NodeLibrary lib)
        {
            var ops = new[] { "==", "!=", "<", ">", "<=", ">=" };
            foreach (var op in ops)
            {
                var d = Def.Make("logic.compare" + op.GetHashCode().ToString(), Cat, $"Compare {op}")
                    .AddIn("a", "A", PortKind.Float, literal: true, def_: 0f)
                    .AddIn("b", "B", PortKind.Float, literal: true, def_: 0f)
                    .AddOut("out", "Результат", PortKind.Bool);
                d.Emit = (c, n) => $"({c.ResolveInput(n, c.Port(n, "a"))} {op} {c.ResolveInput(n, c.Port(n, "b"))})";
                lib.Register(d);
            }

            var eq = Def.Make("logic.equalsexec", Cat, "If Equal (float)", "Сравнить числа, выполнить ветку")
                .ExecIn()
                .AddIn("a", "A", PortKind.Float, literal: true, def_: 0f)
                .AddIn("b", "B", PortKind.Float, literal: true, def_: 0f)
                .Sel("equal", "Равно")
                .Sel("notEqual", "Не равно");
            eq.EmitStatements = (c, n) =>
            {
                var a = c.ResolveInput(n, c.Port(n, "a"));
                var b = c.ResolveInput(n, c.Port(n, "b"));
                using (c.Scope($"if (Mathf.Approximately({a}, {b}))"))
                {
                    foreach (var conn in c.OutgoingAll(n, "equal"))
                    {
                        var next = c.Graph.Find(conn.ToNode);
                        if (next != null) c.RunStatements(next, c.NodeDef(next));
                    }
                }
                using (c.Scope("else"))
                {
                    foreach (var conn in c.OutgoingAll(n, "notEqual"))
                    {
                        var next = c.Graph.Find(conn.ToNode);
                        if (next != null) c.RunStatements(next, c.NodeDef(next));
                    }
                }
            };
            lib.Register(eq);

            var boolOut = Def.Make("logic.not", Cat, "NOT", "Инвертировать логическое значение")
                .AddIn("value", "Значение", PortKind.Bool, literal: true, def_: false)
                .AddOut("out", "Результат", PortKind.Bool);
            boolOut.Emit = (c, n) => $"(!{c.ResolveInput(n, c.Port(n, "value"))})";
            lib.Register(boolOut);

            var boolAnd = Def.Make("logic.and", Cat, "AND", "Логическое И")
                .AddIn("a", "A", PortKind.Bool, literal: true, def_: false)
                .AddIn("b", "B", PortKind.Bool, literal: true, def_: false)
                .AddOut("out", "Результат", PortKind.Bool);
            boolAnd.Emit = (c, n) => $"({c.ResolveInput(n, c.Port(n, "a"))} && {c.ResolveInput(n, c.Port(n, "b"))})";
            lib.Register(boolAnd);

            var boolOr = Def.Make("logic.or", Cat, "OR", "Логическое ИЛИ")
                .AddIn("a", "A", PortKind.Bool, literal: true, def_: false)
                .AddIn("b", "B", PortKind.Bool, literal: true, def_: false)
                .AddOut("out", "Результат", PortKind.Bool);
            boolOr.Emit = (c, n) => $"({c.ResolveInput(n, c.Port(n, "a"))} || {c.ResolveInput(n, c.Port(n, "b"))})";
            lib.Register(boolOr);

            var boolToggle = Def.Make("logic.toggle", Cat, "Toggle", "Инвертировать bool-переменную")
                .ExecIn()
                .AddIn("value", "Значение", PortKind.Bool, literal: true, def_: false)
                .AddOut("out", "Новое значение", PortKind.Bool);
            boolToggle.Emit = (c, n) => $"(!{c.ResolveInput(n, c.Port(n, "value"))})";
            lib.Register(boolToggle);
        }
    }

    internal static class ValueNodes
    {
        public const string Cat = "Значения и переменные";

        public static void Register(NodeLibrary lib)
        {
            var fl = Def.Make("value.float", Cat, "Float", "Числовую константу")
                .AddOut("out", "Значение", PortKind.Float);
            fl.Emit = (c, n) => c.FormatLiteral(n.Get("value", "0") ?? "0", PortKind.Float);
            lib.Register(fl);

            var i = Def.Make("value.int", Cat, "Int", "Целочисленную константу")
                .AddOut("out", "Значение", PortKind.Int);
            i.Emit = (c, n) => c.FormatLiteral(n.Get("value", "0") ?? "0", PortKind.Int);
            lib.Register(i);

            var b = Def.Make("value.bool", Cat, "Bool", "Логическую константу")
                .AddOut("out", "Значение", PortKind.Bool);
            b.Emit = (c, n) => c.FormatLiteral(n.Get("value", "False") ?? "False", PortKind.Bool);
            lib.Register(b);

            var s = Def.Make("value.string", Cat, "String", "Текстовую константу")
                .AddOut("out", "Значение", PortKind.String);
            s.Emit = (c, n) => CodegenContext.Quote(n.Get("value", "") ?? "");
            lib.Register(s);

            var v3 = Def.Make("value.v3", Cat, "Vector3", "Вектор X,Y,Z")
                .AddOut("out", "Значение", PortKind.Vector3);
            v3.Emit = (c, n) => c.FormatLiteral(n.Get("value", "0,0,0") ?? "0,0,0", PortKind.Vector3);
            lib.Register(v3);

            var v2 = Def.Make("value.v2", Cat, "Vector2", "Вектор X,Y")
                .AddOut("out", "Значение", PortKind.Vector2);
            v2.Emit = (c, n) => c.FormatLiteral(n.Get("value", "0,0") ?? "0,0", PortKind.Vector2);
            lib.Register(v2);

            var col = Def.Make("value.color", Cat, "Color", "Цвет R,G,B,A")
                .AddOut("out", "Значение", PortKind.Color);
            col.Emit = (c, n) => c.FormatLiteral(n.Get("value", "1,1,1,1") ?? "1,1,1,1", PortKind.Color);
            lib.Register(col);

            // Переменные: генерируют поля класса
            var getVar = Def.Make("var.get", Cat, "Get Variable", "Прочитать переменную мода")
                .AddIn("name", "Имя", PortKind.String, literal: true, def_: "myVar")
                .AddOut("out", "Значение", PortKind.Float);
            getVar.Emit = (c, n) => c.VariableName(n, PortKind.Float);
            lib.Register(getVar);

            var setVar = Def.Make("var.set", Cat, "Set Variable", "Присвоить переменную мода")
                .ExecIn()
                .AddIn("name", "Имя", PortKind.String, literal: true, def_: "myVar")
                .AddIn("value", "Значение", PortKind.Float, literal: true, def_: 0f)
                .Exec("out", "");
            setVar.EmitStatements = (c, n) =>
            {
                var name = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "myVar"));
                var val = c.ResolveInput(n, c.Port(n, "value"));
                c.Line($"{c.VariableNameByLiteral(n, PortKind.Float)} = {val};");
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
            };
            lib.Register(setVar);

            // Универсальный get с типом
            var getTyped = Def.Make("var.gettyped", Cat, "Get Typed Variable", "Переменная с выбором типа")
                .EnumIn("type", "Тип", new[] { "Float", "Int", "Bool", "String" }, "Float")
                .AddIn("name", "Имя", PortKind.String, literal: true, def_: "myVar")
                .AddOut("out", "Значение", PortKind.Float);
            getTyped.Emit = (c, n) =>
            {
                var t = Def.Key(n, "type", c.NodeDef(n), "Float");
                var kind = t switch
                {
                    "Int" => PortKind.Int,
                    "Bool" => PortKind.Bool,
                    "String" => PortKind.String,
                    _ => PortKind.Float
                };
                return c.VariableName(n, kind);
            };
            lib.Register(getTyped);

            var setTyped = Def.Make("var.settyped", Cat, "Set Typed Variable", "Записать переменную выбранного типа")
                .ExecIn()
                .EnumIn("type", "Тип", new[] { "Float", "Int", "Bool", "String" }, "Float")
                .AddIn("name", "Имя", PortKind.String, literal: true, def_: "myVar")
                .AddIn("value", "Значение", PortKind.Float, literal: true, def_: 0f)
                .Exec("out", "");
            setTyped.EmitStatements = (c, n) =>
            {
                var t = Def.Key(n, "type", c.NodeDef(n), "Float");
                var kind = t switch
                {
                    "Int" => PortKind.Int,
                    "Bool" => PortKind.Bool,
                    "String" => PortKind.String,
                    _ => PortKind.Float
                };
                var name = CodegenContext.Quote(Def.Key(n, "name", c.NodeDef(n), "myVar"));
                var val = c.ResolveInput(n, c.Port(n, "value"));
                c.Line($"{c.VariableNameByLiteral(n, kind)} = {c.CoercePublic(val, kind)};");
                foreach (var conn in c.OutgoingAll(n, "out"))
                {
                    var next = c.Graph.Find(conn.ToNode);
                    if (next != null) c.RunStatements(next, c.NodeDef(next));
                }
            };
            lib.Register(setTyped);

            var constNode = Def.Make("value.const", Cat, "Constant", "Именованная константа (поле только для чтения)")
                .AddIn("name", "Имя", PortKind.String, literal: true, def_: "MY_CONST")
                .AddOut("out", "Значение", PortKind.Float);
            constNode.Emit = (c, n) => c.ConstName(n);
            lib.Register(constNode);
        }
    }

    internal static class StringNodes
    {
        public const string Cat = "Строки";

        public static void Register(NodeLibrary lib)
        {
            var concat = Def.Make("string.concat", Cat, "Concat", "Склеить строки")
                .AddIn("a", "A", PortKind.String, literal: true, def_: "")
                .AddIn("b", "B", PortKind.String, literal: true, def_: "")
                .AddOut("out", "Результат", PortKind.String);
            concat.Emit = (c, n) => $"({c.ResolveInput(n, c.Port(n, "a"))} + {c.ResolveInput(n, c.Port(n, "b"))})";
            lib.Register(concat);

            var contains = Def.Make("string.contains", Cat, "Contains", "Содержит подстроку?")
                .AddIn("value", "Строка", PortKind.String, literal: true, def_: "")
                .AddIn("sub", "Подстрока", PortKind.String, literal: true, def_: "")
                .AddOut("out", "Результат", PortKind.Bool);
            contains.Emit = (c, n) => $"{c.ResolveInput(n, c.Port(n, "value"))}.Contains({c.ResolveInput(n, c.Port(n, "sub"))})";
            lib.Register(contains);

            var len = Def.Make("string.length", Cat, "Length", "Длина строки")
                .AddIn("value", "Строка", PortKind.String, literal: true, def_: "")
                .AddOut("out", "Длина", PortKind.Int);
            len.Emit = (c, n) => $"{c.ResolveInput(n, c.Port(n, "value"))}.Length";
            lib.Register(len);

            var upper = Def.Make("string.upper", Cat, "To Upper", "В верхний регистр")
                .AddIn("value", "Строка", PortKind.String, literal: true, def_: "")
                .AddOut("out", "Результат", PortKind.String);
            upper.Emit = (c, n) => $"{c.ResolveInput(n, c.Port(n, "value"))}.ToUpperInvariant()";
            lib.Register(upper);

            var lower = Def.Make("string.lower", Cat, "To Lower", "В нижний регистр")
                .AddIn("value", "Строка", PortKind.String, literal: true, def_: "")
                .AddOut("out", "Результат", PortKind.String);
            lower.Emit = (c, n) => $"{c.ResolveInput(n, c.Port(n, "value"))}.ToLowerInvariant()";
            lib.Register(lower);

            var sub = Def.Make("string.substring", Cat, "Substring", "Часть строки")
                .AddIn("value", "Строка", PortKind.String, literal: true, def_: "")
                .AddIn("index", "Начало", PortKind.Int, literal: true, def_: 0)
                .AddOut("out", "Результат", PortKind.String);
            sub.Emit = (c, n) => $"{c.ResolveInput(n, c.Port(n, "value"))}.Substring({c.ResolveInput(n, c.Port(n, "index"))})";
            lib.Register(sub);

            var replace = Def.Make("string.replace", Cat, "Replace", "Заменить подстроку")
                .AddIn("value", "Строка", PortKind.String, literal: true, def_: "")
                .AddIn("from", "Что", PortKind.String, literal: true, def_: "")
                .AddIn("to", "На что", PortKind.String, literal: true, def_: "")
                .AddOut("out", "Результат", PortKind.String);
            replace.Emit = (c, n) => $"{c.ResolveInput(n, c.Port(n, "value"))}.Replace(" +
                                    $"{c.ResolveInput(n, c.Port(n, "from"))}, {c.ResolveInput(n, c.Port(n, "to"))})";
            lib.Register(replace);

            var split = Def.Make("string.split", Cat, "Split", "Разбить по разделителю")
                .AddIn("value", "Строка", PortKind.String, literal: true, def_: "")
                .AddIn("sep", "Разделитель", PortKind.String, literal: true, def_: ",")
                .AddOut("out", "Части", PortKind.Object);
            split.Emit = (c, n) => $"{c.ResolveInput(n, c.Port(n, "value"))}.Split({c.ResolveInput(n, c.Port(n, "sep"))})";
            lib.Register(split);

            var parse = Def.Make("string.tofloat", Cat, "To Float", "Строку в число (float.TryParse)")
                .AddIn("value", "Строка", PortKind.String, literal: true, def_: "")
                .AddIn("fallback", "Если не число", PortKind.Float, literal: true, def_: 0f)
                .AddOut("out", "Число", PortKind.Float);
            parse.Emit = (c, n) =>
            {
                var v = c.ResolveInput(n, c.Port(n, "value"));
                var fb = c.ResolveInput(n, c.Port(n, "fallback"));
                var tmp = "__p" + Math.Abs(v.GetHashCode()) % 100000;
                return $"float.TryParse({v}, out var {tmp}) ? {tmp} : {fb}";
            };
            lib.Register(parse);
        }
    }
}