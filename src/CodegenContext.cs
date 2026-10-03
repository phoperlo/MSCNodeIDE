using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MSCNodeIDE.Core
{
    /// <summary>
    /// Конвертирует граф в читаемый C#: строки с правильным отступом,
    /// блоки if/for, выражения из связей.
    /// </summary>
    public sealed class CodegenContext
    {
        private StringBuilder _sb = new StringBuilder();
        private int _indent;
        private readonly NodeGraph _graph;
        private readonly NodeLibrary _library;
        private readonly List<CodegenDiagnostic> _diagnostics;

        public HashSet<string> Emitted { get; } = new HashSet<string>();
        public List<string> Usings { get; } = new List<string>();
        public Dictionary<string, string> MethodBodies { get; } = new Dictionary<string, string>();
        public bool InMethod { get; private set; }
        public string CurrentMethod { get; private set; }

        public NodeGraph Graph => _graph;
        public NodeLibrary Library => _library;

        private readonly Dictionary<string, string> _loopIndex = new Dictionary<string, string>();

        // ---------- Hoisting: вынос повторяющихся подвыражений в локальные переменные ----------

        /// <summary>Ключи (nodeId::portId), которые нужно вынести в переменную.</summary>
        private readonly HashSet<string> _hoist = new HashSet<string>();

        /// <summary>Уже объявленные переменные: ключ -> имя.</summary>
        private readonly Dictionary<string, string> _hoistNames = new Dictionary<string, string>();

        /// <summary>
        /// Считает, сколько раз каждый выход используется, и помечает frequently-used
        /// подвыражения для выноса в локальную переменную. Без этого сгенерированный код
        /// раздувается повторениями (GameObject.Find("PLAYER") в каждом обращении).
        /// </summary>
        public void PlanHoisting(IEnumerable<string> reachableNodeIds)
        {
            _hoist.Clear();
            _hoistNames.Clear();
            var reachable = new HashSet<string>(reachableNodeIds);

            var counts = new Dictionary<string, int>();
            foreach (var c in _graph.Connections)
            {
                if (!reachable.Contains(c.FromNode)) continue;
                var key = c.FromNode + "::" + c.FromPort;
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }

            foreach (var kv in counts)
            {
                // Вынос делаем только для действительно дорогих/длинных выражений,
                // чтобы не плодить переменные ради констант.
                if (kv.Value < 2) continue;
                var nodeId = kv.Key.Split("::")[0];
                var node = _graph.Find(nodeId);
                if (node == null) continue;
                var nd = _library.Find(node.DefinitionId);
                if (nd?.Emit == null) continue;   // операторы не хoистим
                _hoist.Add(kv.Key);
            }
        }

        private string HoistKey(string nodeId, string portId) => nodeId + "::" + portId;

        /// <summary>
        /// Простое выражение — литерал или обращение к переменной/полю без вызовов.
        /// Такие можно безопасно подставлять инлайн, не плодя локальных переменных.
        /// </summary>
        private static bool IsSimpleExpression(string e)
        {
            if (string.IsNullOrEmpty(e)) return true;
            if (e.IndexOf('(') >= 0) return false;   // есть вызов функции/конструктора
            if (e.IndexOf('"') >= 0) return false;   // строковый литерал — оставляем инлайн
            if (e == "true" || e == "false" || e == "null") return true;

            // Числовой литерал, возможно со скобками/суффиксом f
            var t = e.Trim();
            if (t.Length == 0) return true;
            if (t.Length <= 24 && (char.IsDigit(t[0]) || t[0] == '-' || t[0] == '.') &&
                t.All(ch => char.IsDigit(ch) || ch == '.' || ch == '-' || ch == 'f' || ch == 'F'))
                return true;

            // Идентификатор с точками: Time.deltaTime, c_counter, GameObject.Find — нет,
            // последнее содержит скобки, поэтому сюда не попадёт.
            if (t.All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '-'))
                return true;

            return false;
        }

        public CodegenContext(NodeGraph graph, NodeLibrary library, List<CodegenDiagnostic> diagnostics)
        {
            _graph = graph;
            _library = library;
            _diagnostics = diagnostics;
        }

        // ---------- помощники для узлов ----------

        public NodeDefinition NodeDef(NodeInstance n) => _library.Find(n?.DefinitionId);
        public NodeInstance CurrentNode(NodeInstance n) => n;

        public Port Port(NodeInstance n, string portId)
        {
            var d = NodeDef(n);
            if (d == null) return new Port { Id = portId, Kind = PortKind.Object };
            return d.Ports.FirstOrDefault(p => p.Id == portId && p.IsInput)
                   ?? new Port { Id = portId, Kind = PortKind.Object };
        }

        public Port OutPort(NodeInstance n, string portId)
        {
            var d = NodeDef(n);
            if (d == null) return new Port { Id = portId, Kind = PortKind.Object, IsInput = false };
            return d.Ports.FirstOrDefault(p => p.Id == portId && !p.IsInput)
                   ?? new Port { Id = portId, Kind = PortKind.Object, IsInput = false };
        }

        /// <summary>Все связи, выходящие из указанного выходного порта узла.</summary>
        public IEnumerable<Connection> OutgoingAll(NodeInstance n, string portId)
            => _graph.Outgoing(n.Id, portId);

        public IEnumerable<Connection> Outgoing(string portId)
            => OutgoingAll(CurrentNodeInstance, portId);

        public NodeInstance CurrentNodeInstance { get; private set; }
        public void SetCurrent(NodeInstance n) => CurrentNodeInstance = n;

        /// <summary>
        /// Внешнее значение: результат оператора передан в порт узла-потребителя,
        /// минуя обычное разрешение по связям (для Create* узлов).
        /// </summary>
        private readonly Dictionary<string, string> _external = new Dictionary<string, string>();

        public void SetExternalValue(NodeInstance target, string portPropertyKey, string expr)
        {
            if (target == null) return;
            _external[target.Id + "::" + portPropertyKey] = expr;
        }

        public string TryGetExternal(NodeInstance target, string portPropertyKey)
            => _external.TryGetValue(target?.Id + "::" + portPropertyKey, out var v) ? v : null;

        public void ClearExternal() => _external.Clear();

        public void PushIndent() => _indent++;
        public void PopIndent() => _indent = Math.Max(0, _indent - 1);

        /// <summary>Вставляет уже готовый фрагмент кода с корректным отступом.</summary>
        public void Raw(string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            foreach (var line in code.Split('\n'))
            {
                var clean = line.TrimEnd('\r');
                if (clean.Trim().Length == 0) continue;
                Line(clean.TrimStart());
            }
        }

        /// <summary>
        /// Собирает тело подписки/обработчика: подграфы, подключённые к порту узла
        /// (без самого узла), генерируются в отдельный буфер.
        /// </summary>
        public string GetInjectedBody(NodeInstance node, string port)
        {
            var savedSb = _sb;
            var savedIndent = _indent;
            var savedEmitted = new HashSet<string>(Emitted);
            var savedExternal = new Dictionary<string, string>(_external);

            _sb = new StringBuilder();
            _indent = savedIndent;

            foreach (var conn in Graph.Outgoing(node.Id, port))
            {
                var next = Graph.Find(conn.ToNode);
                if (next == null) continue;
                var nd = _library.Find(next.DefinitionId);
                if (nd != null) RunStatements(next, nd);
            }

            var body = _sb.ToString();
            _sb = savedSb;
            _indent = savedIndent;
            Emitted.Clear();
            foreach (var k in savedEmitted) Emitted.Add(k);
            _external.Clear();
            foreach (var kv in savedExternal) _external[kv.Key] = kv.Value;
            return body;
        }

        public void SetLoopIndex(NodeInstance n, string varName) => _loopIndex[n.Id] = varName;
        public void ClearLoopIndex(NodeInstance n) => _loopIndex.Remove(n.Id);
        public string LoopIndex(NodeInstance n)
            => _loopIndex.TryGetValue(n.Id, out var v) ? v : null;

        // ---------- Настройки мода: чтение значений через объекты Settings ----------

        /// <summary>Имя проекта для формирования полного settingID настройки.</summary>
        public string ProjectName { get; set; } = "MyMod";

        /// <summary>
        /// Полный settingID настройки. Должен совпадать с тем, что генерирует
        /// регистрация в c_RegisterSettings(), иначе GetValue() вернёт не то поле.
        /// </summary>
        public string SettingId(NodeInstance n)
        {
            var key = n.Get("in.id") ?? n.Get("in.name") ?? "option";
            return ProjectName + "_" + key;
        }

        /// <summary>Настройки, для которых нужны поля-объекты в классе мода.</summary>
        public List<string> ToggleSettings { get; } = new List<string>();
        public List<string> SliderSettings { get; } = new List<string>();
        public List<string> TextSettings { get; } = new List<string>();

        public void Use(params string[] usings)
        {
            foreach (var u in usings)
                if (!Usings.Contains(u)) Usings.Add(u);
        }

        // ---------- структура ----------

        public void Line(string text = "")
        {
            if (string.IsNullOrEmpty(text)) _sb.AppendLine();
            else _sb.Append(new string(' ', _indent * 4)).AppendLine(text);
        }

        public IDisposable Block(string header)
        {
            Line(header);
            Line("{");
            _indent++;
            return new BlockScope(this);
        }

        public IDisposable Scope(string header, string suffix = "")
        {
            Line(header);
            Line("{");
            _indent++;
            return new BlockScope(this, suffix);
        }

        public void CloseBlock(string suffix = "")
        {
            _indent--;
            Line("}" + suffix);
        }

        private sealed class BlockScope : IDisposable
        {
            private readonly CodegenContext _ctx;
            private readonly string _suffix;
            public BlockScope(CodegenContext ctx, string suffix = "") { _ctx = ctx; _suffix = suffix; }
            public void Dispose() => _ctx.CloseBlock(_suffix);
        }

        public void BeginMethod(string signature)
        {
            if (InMethod) throw new InvalidOperationException("Метод уже открыт");
            _sb.Clear();
            _indent = 2;
            InMethod = true;
            CurrentMethod = signature;
            _hoistNames.Clear();
        }

        public string EndMethod()
        {
            InMethod = false;
            var body = _sb.ToString();
            _sb.Clear();
            CurrentMethod = null;
            return body;
        }

        // ---------- разрешение значений ----------

        /// <summary>
        /// Возвращает C#-выражение для входного порта узла.
        /// Если порт подключён — берём выход источника. Иначе literal или дефолт.
        /// </summary>
        public string ResolveInput(NodeInstance node, Port port)
        {
            // Внешнее значение оператора (Create* и т.п.)
            var ext = TryGetExternal(node, "in." + port.Id);
            if (ext != null) return CoercePublic(ext, port.Kind);

            var conn = _graph.Incoming(node.Id, port.Id).FirstOrDefault();
            if (conn != null)
            {
                var srcNode = _graph.Find(conn.FromNode);
                if (srcNode == null)
                {
                    Diag(DiagnosticSeverity.Error, node,
                        $"Порт '{port.Name}' подключён к несуществующему узлу.");
                    return DefaultLiteral(port);
                }
                var def = _library.Find(srcNode.DefinitionId);
                if (def == null)
                {
                    Diag(DiagnosticSeverity.Error, node, $"Неизвестный тип узла '{srcNode.DefinitionId}'.");
                    return DefaultLiteral(port);
                }
                string expr;
                if (def.Emit == null)
                {
                    // Узел-операция не выдаёт значение. Раньше здесь молча
                    // подставлялся null, и код получался нерабочим.
                    Diag(DiagnosticSeverity.Error, node,
                        $"Вход '{port.Name}' подключён к узлу «{def.Title}», который не выдаёт значение. " +
                        "Подключите узел-источник значения.");
                    expr = DefaultLiteral(port);
                }
                else
                {
                    try
                    {
                        expr = def.Emit(this, srcNode);
                    }
                    catch (Exception ex)
                    {
                        Diag(DiagnosticSeverity.Error, node, $"Ошибка узля '{def.Title}': {ex.Message}");
                        expr = DefaultLiteral(port);
                    }
                }

                if (string.IsNullOrWhiteSpace(expr))
                {
                    Diag(DiagnosticSeverity.Error, node,
                        $"Узел «{def.Title}» вернул пустое значение для входа '{port.Name}'.");
                    expr = DefaultLiteral(port);
                }
                var srcPort = def.Ports.FirstOrDefault(p => !p.IsInput && p.Id == conn.FromPort);
                var coerced = Coerce(expr, srcPort?.Kind ?? PortKind.Object, port.Kind);

                // Выносим в локальную переменную всё, что не является простой
                // константой или обращением к полю. Иначе выражения вроде
                // GameObject.Find("PLAYER") дублировались бы внутри тернарника.
                var key = HoistKey(srcNode.Id, conn.FromPort);
                bool needsHoist = _hoist.Contains(key) || !IsSimpleExpression(coerced);

                if (needsHoist)
                {
                    if (_hoistNames.TryGetValue(key, out var varName))
                        return varName;

                    // Имя выводим из заголовка узла: читаемо и уникально
                    var baseName = Sanitize(def.Title);
                    if (string.IsNullOrEmpty(baseName)) baseName = "v";
                    varName = "_" + baseName + (_hoistNames.Count + 1);

                    var kind = srcPort?.Kind ?? PortKind.Object;
                    var typeName = kind.CSharp();
                    var declType = (PortKindUtil.IsObjectLike(kind) || kind == PortKind.AudioClip
                                    || kind == PortKind.Texture2D || kind == PortKind.Mesh
                                    || kind == PortKind.Material)
                        ? "var" : typeName;
                    if (declType == "object") declType = "var";

                    _hoistNames[key] = varName;
                    Line($"{declType} {varName} = {coerced};");
                    return varName;
                }

                return coerced;
            }

            // Не подключён: значение из инспектора, иначе значение по умолчанию из узла
            var raw = node.Get("in." + port.Id);
            if (raw != null) return FormatLiteral(raw, port.Kind);

            if (port.LiteralValue != null)
                return FormatLiteral(Convert.ToString(port.LiteralValue,
                    CultureInfo.InvariantCulture), port.Kind);

            return DefaultLiteral(port);
        }

        /// <summary>Выполняет узел как оператор (для узлов-действий).</summary>
        public void RunStatements(NodeInstance node, NodeDefinition def)
        {
            if (!Emitted.Add(node.Id)) return;   // защита от повторного/рекурсивного выполнения
            if (def.EmitStatements == null) return;
            SetCurrent(node);
            try
            {
                def.EmitStatements(this, node);
            }
            catch (Exception ex)
            {
                Diag(DiagnosticSeverity.Error, node, $"Ошибка в '{def.Title}': {ex.Message}");
            }
        }

        public bool HasRun(NodeInstance node) => Emitted.Contains(node.Id);

        /// <summary>
        /// Разрешает цепочку выполнения: текущий узел -> все подключённые дальше по Execution.
        /// Это позволяет строить последовательности, не дублируя обход.
        /// </summary>
        public void RunChain(NodeInstance node)
        {
            var stack = new Stack<NodeInstance>();
            stack.Push(node);
            var guard = new HashSet<string>();
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur == null) continue;
                if (!guard.Add(cur.Id)) continue;
                var def = _library.Find(cur.DefinitionId);
                if (def == null) continue;
                RunStatements(cur, def);

                foreach (var conn in _graph.Connections.Where(c => c.FromNode == cur.Id))
                {
                    // цепочка идёт только по Execution-портам
                    var outPort = def.OutputPort(conn.FromPort);
                    if (outPort == null || outPort.Kind != PortKind.Execution) continue;
                    var next = _graph.Find(conn.ToNode);
                    if (next != null) stack.Push(next);
                }
            }
        }

        // ---------- литералы ----------

        public string FormatLiteral(string raw, PortKind kind)
        {
            if (raw == null) return DefaultLiteral(new Port { Kind = kind });

            switch (kind)
            {
                case PortKind.Bool:
                    return (raw == "True" || raw == "true" || raw == "1") ? "true" : "false";

                case PortKind.Int:
                    return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                        ? i.ToString(CultureInfo.InvariantCulture)
                        : "0";

                case PortKind.Float:
                {
                    var v = ParseFloat(raw);
                    return v.ToString("R", CultureInfo.InvariantCulture) + "f";
                }

                case PortKind.String:
                    return Quote(raw);

                case PortKind.Vector2:
                {
                    var parts = SplitFloats(raw, 2);
                    return $"new Vector2({F(parts[0])}, {F(parts[1])})";
                }

                case PortKind.Vector3:
                {
                    var parts = SplitFloats(raw, 3);
                    return $"new Vector3({F(parts[0])}, {F(parts[1])}, {F(parts[2])})";
                }

                case PortKind.Color:
                {
                    var parts = SplitFloats(raw, 4);
                    return $"new Color({F(parts[0])}, {F(parts[1])}, {F(parts[2])}, {F(parts[3])})";
                }

                default:
                    return "null";
            }
        }

        public static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static float ParseFloat(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? f : 0f;

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";

        private static float[] SplitFloats(string raw, int n)
        {
            var res = new float[n];
            var parts = raw.Split(',');
            for (int i = 0; i < n; i++)
                res[i] = i < parts.Length ? ParseFloat(parts[i].Trim()) : 0f;
            return res;
        }

        public string DefaultLiteral(Port port)
        {
            switch (port.Kind)
            {
                case PortKind.Bool: return "false";
                case PortKind.Int: return "0";
                case PortKind.Float: return "0f";
                case PortKind.String: return "\"\"";
                case PortKind.Vector2: return "Vector2.zero";
                case PortKind.Vector3: return "Vector3.zero";
                case PortKind.Color: return "Color.white";
                default: return "null";
            }
        }

        // ---------- приведение типов ----------

        /// <summary>
        /// Если типы не совпадают, добавляет аккуратное приведение.
        /// Unity-объекты приводим через GetComponent, числа — простым cast.
        /// </summary>
        private string Coerce(string expr, PortKind srcKind, PortKind target)
        {
            if (srcKind == target) return expr;
            if (srcKind == target) return expr;
            if (PortKindUtil.IsNumeric(srcKind) && PortKindUtil.IsNumeric(target))
                return $"(({target.CSharp()})({expr}))";
            if (srcKind == PortKind.GameObject && target == PortKind.Transform)
                return $"(({expr}) != null ? ({expr}).transform : null)";
            if (srcKind == PortKind.Transform && target == PortKind.GameObject)
                return $"(({expr}) != null ? ({expr}).gameObject : null)";
            if (srcKind == PortKind.GameObject && target != PortKind.GameObject &&
                PortKindUtil.IsObjectLike(target) && target != PortKind.Object)
                return $"(({expr}) != null ? ({expr}).GetComponent<{target.CSharp()}>() : null)";
            if (target == PortKind.String && PortKindUtil.IsNumeric(srcKind))
                return $"({expr}).ToString(CultureInfo.InvariantCulture)";
            if (target == PortKind.Float && srcKind == PortKind.Int)
                return $"((float)({expr}))";
            if (target == PortKind.Int && srcKind == PortKind.Float)
                return $"((int)({expr}))";
            if (target == PortKind.Object)
                return expr;
            return expr;
        }

        // ---------- переменные / константы ----------

        private readonly Dictionary<string, PortKind> _varTypes = new Dictionary<string, PortKind>();
        private readonly HashSet<string> _constants = new HashSet<string>();
        private int _nameSeed;

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "v";
            var sb = new StringBuilder();
            foreach (var ch in s)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            var r = sb.ToString();
            if (r.Length == 0 || char.IsDigit(r[0])) r = "_" + r;
            return r;
        }

        public string VarKey(NodeInstance n)
        {
            var d = NodeDef(n);
            var name = d?.Ports.FirstOrDefault(p => p.Id == "name" && p.IsLiteral) != null
                ? Def.Key(n, "name", d, "var")
                : n.Get("name", "var");
            return name;
        }

        public string VariableName(NodeInstance n, PortKind kind)
        {
            var name = VarKey(n);
            _varTypes[name] = kind;
            return "c_" + Sanitize(name);
        }

        public string VariableNameByLiteral(NodeInstance n, PortKind kind)
        {
            var name = Def.Key(n, "name", NodeDef(n), "var");
            _varTypes[name] = kind;
            return "c_" + Sanitize(name);
        }

        public string CoercePublic(string expr, PortKind kind)
        {
            switch (kind)
            {
                case PortKind.Bool:
                    return expr == "true" || expr == "false" ? expr : expr;
                case PortKind.Int:
                    return expr.Contains('.') || expr.Contains('f') ? $"((int)({expr}))" : expr;
                case PortKind.Float:
                    return expr.Contains('.') || expr.EndsWith("f") ? expr : $"(({expr}) * 1f)";
                case PortKind.String:
                    return expr.StartsWith("\"") || expr.Contains("(") ? expr : CodegenContext.Quote(expr);
                default:
                    return expr;
            }
        }

        public string ConstName(NodeInstance n)
        {
            var d = NodeDef(n);
            var name = Def.Key(n, "name", d, "CONST");
            if (!_constants.Contains(name))
            {
                _constants.Add(name);
                Use("System");
            }
            return "C_" + Sanitize(name).ToUpperInvariant();
        }

        /// <summary>Поля класса, которые нужно объявить в сгенерированном классе.</summary>
        public IEnumerable<KeyValuePair<string, PortKind>> Variables => _varTypes;

        public IEnumerable<string> Constants => _constants;

        /// <summary>Уникальное имя временной переменной.</summary>
        public string TempName(string prefix = "t") => "_" + prefix + (++_nameSeed);

        /// <summary>Ширина окна GUI: используется ширина последнего объявленного окна.</summary>
        public float GuiWidth { get; set; } = 280f;
        public string WidthExpr(NodeInstance n) => "c_GuiWidth";

        /// <summary>Список имён глобальных переменных, которые нужно обработать в подписи.</summary>
        public HashSet<string> ReferencedGlobalVars { get; } = new HashSet<string>();

        /// <summary>Нужны ли помощники поиска объектов и безопасного телепорта.</summary>
        public bool NeedsObjectFinder { get; set; }
        public bool NeedsTeleportHelper { get; set; }
        public bool NeedsGlobalObjectReader { get; set; }

        /// <summary>Имена окон IMGUI: для генерации методов отрисовки.</summary>
        public List<(string Id, string Title, NodeInstance Node)> ImguiWindows { get; } = new List<(string, string, NodeInstance)>();
        public Dictionary<string, string> ImguiWindowBodies { get; } = new Dictionary<string, string>();

        // ---------- диагностика ----------

        public void Diag(DiagnosticSeverity sev, NodeInstance node, string message)
        {
            _diagnostics.Add(new CodegenDiagnostic
            {
                Severity = sev,
                Message = message,
                NodeId = node?.Id,
                NodeTitle = node != null && _library.Find(node.DefinitionId) != null
                    ? _library.Find(node.DefinitionId).Title : null,
                Method = CurrentMethod
            });
        }

        public void WarnUnusedInputs(NodeInstance node, NodeDefinition def)
        {
            foreach (var p in def.Inputs)
            {
                if (p.Kind == PortKind.Execution) continue;
                if (!_graph.Incoming(node.Id, p.Id).Any() && !p.IsLiteral)
                    Diag(DiagnosticSeverity.Info, node,
                        $"Вход '{p.Name}' не подключён — используется значение по умолчанию.");
            }
        }
    }

    public enum DiagnosticSeverity { Info, Warning, Error }

    public sealed class CodegenDiagnostic
    {
        public DiagnosticSeverity Severity { get; set; }
        public string Message { get; set; }
        public string NodeId { get; set; }
        public string NodeTitle { get; set; }
        public string Method { get; set; }
    }
}
