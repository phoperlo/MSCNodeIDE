using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MSCNodeIDE.Core
{
    public sealed class ModProjectSettings
    {
        public string Name { get; set; } = "MyMod";
        public string Author { get; set; } = "Author";
        public string Version { get; set; } = "1.0.0";
        public string Description { get; set; } = "Mod created with MSCNodeIDE";
        public string Namespace { get; set; } = "";
        public string GameDirectory { get; set; } = "";
        public string ModsDirectory { get; set; } = "";
        public bool CopyToModsOnBuild { get; set; } = true;
        public string TargetFramework { get; set; } = "v3.5";

        /// <summary>Игра: MySummerCar или MyWinterCar. Влияет на свойство Mod.SupportedGames.</summary>
        public string SupportedGames { get; set; } = "MySummerCar";

        public string CleanNamespace
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Namespace))
                    return Namespace.Trim();
                var parts = (Name ?? "MyMod").Split(new[] { ' ', '-', '_' },
                    StringSplitOptions.RemoveEmptyEntries);
                return string.Join("", parts);
            }
        }
    }

    public sealed class GeneratedCode
    {
        public string SourceCode { get; set; } = "";
        public List<CodegenDiagnostic> Diagnostics { get; set; } = new List<CodegenDiagnostic>();
        public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Собирает из графа полный .cs файл класса мода.</summary>
    public static class CodeGenerator
    {
        private static readonly Dictionary<string, string[]> EntryNodes = new Dictionary<string, string[]>
        {
            { "flow.onload",      new[] { "OnLoad" } },
            { "flow.update",      new[] { "Update" } },
            { "flow.onnewgame",   new[] { "OnNewGame" } },
            { "flow.onmenuload",  new[] { "OnMenuLoad" } },
            { "flow.onsave",      new[] { "OnSave" } },
            { "flow.ongui",       new[] { "OnGui" } },
            { "flow.fixedupdate", new[] { "FixedUpdate" } },
            { "flow.postload",    new[] { "PostLoad" } },
        };

        public static GeneratedCode Generate(NodeGraph graph, ModProjectSettings project, NodeLibrary library)
        {
            var result = new GeneratedCode();
            var ctx = new CodegenContext(graph, library, result.Diagnostics);

            // 1. Собираем тела методов по точкам входа
            // Окна IMGUI: собираем список один раз, до генерации тел хуков
            RegisterImguiWindows(ctx, library, graph);

            // Настройки мода: запоминаем, какие объекты Settings нужно создать
            ctx.ProjectName = project.Name;
            foreach (var sn in graph.Nodes.Where(n => n.DefinitionId != null &&
                                                     n.DefinitionId.StartsWith("msc.settings.")))
            {
                var id = ctx.SettingId(sn);
                switch (sn.DefinitionId)
                {
                    case "msc.settings.toggle": ctx.ToggleSettings.Add(id); break;
                    case "msc.settings.slider": ctx.SliderSettings.Add(id); break;
                    case "msc.settings.text": ctx.TextSettings.Add(id); break;
                }
            }

            foreach (var (entryDefId, methods) in EntryNodes)
            {
                var entryNodes = graph.Nodes.Where(n => n.DefinitionId == entryDefId).ToList();
                if (entryNodes.Count == 0) continue;

                foreach (var methodName in methods)
                {
                    ctx.BeginMethod(methodName);
                    ctx.Emitted.Clear();
                    ctx.ClearExternal();
                    ctx.PlanHoisting(ReachableFrom(graph, entryNodes, library));
                    foreach (var entry in entryNodes)
                    {
                        // Пропускаем испускание самого entry-узла (он пустой), идём по цепочке
                        var first = graph.OutgoingAny(entry.Id);
                        if (first == null) continue;
                        foreach (var conn in graph.Outgoing(entry.Id, first.FromPort))
                        {
                            var next = graph.Find(conn.ToNode);
                            if (next == null) continue;
                            var nd = library.Find(next.DefinitionId);
                            if (nd != null) ctx.RunStatements(next, nd);
                        }
                    }
                    var body = ctx.EndMethod();
                    ctx.MethodBodies[methodName] = body;
                }
            }

            // 2. Если Update есть, но пуст — всё равно создаём
            if (ctx.MethodBodies.Count == 0)
            {
                result.Diagnostics.Add(new CodegenDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message = "В графе нет точек входа. Добавьте узел On Load или Update."
                });
            }

            // 3. Пишем файл
            result.SourceCode = BuildFile(ctx, project, graph, result.Diagnostics);
            return result;
        }

        /// <summary>
        /// Регистрирует окна IMGUI: для каждого узла gui.window генерируется
        /// отдельный метод отрисовки, а в хуке OnGUI появляется вызов GUI.Window.
        /// Так же отмечает, какие помощники нужны в сгенерированном классе.
        /// </summary>
        private static void RegisterImguiWindows(CodegenContext ctx, NodeLibrary library, NodeGraph graph)
        {
            foreach (var node in graph.Nodes.Where(n => n.DefinitionId == "gui.window"))
            {
                var id = node.Get("in.id", "100");
                var title = node.Get("in.title", "Меню");
                if (ctx.ImguiWindows.Any(w => w.Id == id)) continue;
                ctx.ImguiWindows.Add((id, title, node));

                // Тело окна собираем здесь, до написания блока using:
                // иначе используемые в нём пространства имён не попадут в заголовок.
                var body = ctx.GetInjectedBody(node, "content");
                ctx.ImguiWindowBodies[id] = string.IsNullOrWhiteSpace(body)
                    ? ""
                    : Reindent(body.TrimEnd(), 3) + "\r\n";
            }

            foreach (var node in graph.Nodes)
            {
                switch (node.DefinitionId)
                {
                    case "unity.findallbyname":
                        ctx.NeedsObjectFinder = true;
                        break;
                    case "unity.teleportsafe":
                        ctx.NeedsTeleportHelper = true;
                        break;
                }
            }
        }

        /// <summary>Все узлы, достижимые из точек входа (по связям любого типа).</summary>
        private static IEnumerable<string> ReachableFrom(NodeGraph graph, List<NodeInstance> entries,
            NodeLibrary library)
        {
            var seen = new HashSet<string>();
            var stack = new Stack<NodeInstance>();
            foreach (var e in entries) stack.Push(e);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur == null || !seen.Add(cur.Id)) continue;
                foreach (var c in graph.Connections.Where(x => x.FromNode == cur.Id))
                {
                    var nxt = graph.Find(c.ToNode);
                    if (nxt != null && !seen.Contains(nxt.Id)) stack.Push(nxt);
                }
            }
            return seen;
        }

        private static string BuildFile(CodegenContext ctx, ModProjectSettings project,
            NodeGraph graph, List<CodegenDiagnostic> diags)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// ============================================================");
            sb.AppendLine($"//  {project.Name} v{project.Version}");
            sb.AppendLine($"//  Сгенерировано MSCNodeIDE");
            sb.AppendLine($"//  Автор: {project.Author}");
            sb.AppendLine("//  Правьте ноды в редакторе — этот файл перезаписывается при сборке.");
            sb.AppendLine("// ============================================================");
            sb.AppendLine();

            var usings = new List<string> { "System", "System.Collections.Generic", "UnityEngine", "MSCLoader" };
            foreach (var u in ctx.Usings)
                if (!usings.Contains(u)) usings.Add(u);
            // FsmVariables лежит в глобальном пространстве имён, а FsmFloat/FsmBool
            // и прочие типы — в HutongGames.PlayMaker.
            foreach (var u in usings)
                sb.AppendLine($"using {u};");
            sb.AppendLine();

            var ns = project.CleanNamespace;
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {project.Name} : Mod");
            sb.AppendLine("    {");

            // --- поля-переменные ---
            var vars = ctx.Variables.ToList();
            if (vars.Count > 0)
            {
                sb.AppendLine("        // ---------- Переменные мода ----------");
                foreach (var (name, kind) in vars)
                {
                    var safe = Sanitize(name);
                    var field = "c_" + safe;
                    var type = kind.CSharp();
                    sb.AppendLine($"        private {type} {field} = {(kind == PortKind.String ? "\"\"" : kind.CSharp() switch
                    {
                        "bool" => "false",
                        "int" => "0",
                        "float" => "0f",
                        _ => "null"
                    })};");
                }
                sb.AppendLine();
            }

            // --- константы ---
            foreach (var konst in ctx.Constants)
            {
                sb.AppendLine($"        private const float C_{Sanitize(konst).ToUpperInvariant()} = 0f;");
            }
            if (ctx.Constants.Any()) sb.AppendLine();

            // --- свойства доступа к переменным ---
            if (vars.Count > 0)
            {
                sb.AppendLine("        // ---------- Публичный доступ к переменным ----------");
                foreach (var (name, kind) in vars)
                {
                    var safe = Sanitize(name);
                    var field = "c_" + safe;
                    var type = kind.CSharp();
                    sb.AppendLine($"        public {type} {Cap(safe)}");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            get => {field};");
                    sb.AppendLine($"            set => {field} = value;");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
            }

            // Какие помощники глобальных переменных нужны
            var globalSets = graph.Nodes
                .Where(n => n.DefinitionId != null && n.DefinitionId.StartsWith("pm.globalset"))
                .Select(n => n.DefinitionId)
                .ToList();

            // --- поля для чтения значений настроек ---
            if (ctx.ToggleSettings.Count > 0 || ctx.SliderSettings.Count > 0 || ctx.TextSettings.Count > 0)
            {
                sb.AppendLine("        // ---------- Настройки мода ----------");
                sb.AppendLine("        // Значения читаются через GetValue() у объектов, которые вернул Settings.");
                foreach (var id in ctx.ToggleSettings)
                    sb.AppendLine($"        private SettingsCheckBox c_SettingToggle_{Sanitize(id)};");
                foreach (var id in ctx.SliderSettings)
                    sb.AppendLine($"        private SettingsSlider c_SettingSlider_{Sanitize(id)};");
                foreach (var id in ctx.TextSettings)
                    sb.AppendLine($"        private SettingsTextBox c_SettingText_{Sanitize(id)};");
                sb.AppendLine();
            }

            // --- тела хуков как отдельные методы ---
            // В MSCLoader 1.4.x хуки объявлены internal, поэтому override невозможен.
            // Правильный способ регистрации — ModSetup() + SetupFunction(Setup, Action).
            var hooks = new[]
            {
                ("PreLoad", "PreLoad"),
                ("OnLoad", "OnLoad"),
                ("OnNewGame", "OnNewGame"),
                ("OnMenuLoad", "OnMenuLoad"),
                ("OnSave", "OnSave"),
                ("Update", "Update"),
                ("OnGui", "OnGUI"),
                ("FixedUpdate", "FixedUpdate"),
                ("PostLoad", "PostLoad"),
            };

            foreach (var (methodName, hookName) in hooks)
            {
                if (!ctx.MethodBodies.TryGetValue(methodName, out var body)) continue;
                sb.AppendLine($"        private void {methodName}()");
                sb.AppendLine("        {");
                sb.Append(string.IsNullOrWhiteSpace(body)
                    ? "            // (пусто)\n"
                    : body);
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // --- регистрация хуков ---
            sb.AppendLine("        public override void ModSetup()");
            sb.AppendLine("        {");
            foreach (var (methodName, hookName) in hooks)
            {
                if (!ctx.MethodBodies.ContainsKey(methodName)) continue;
                sb.AppendLine($"            SetupFunction(Setup.{hookName}, {methodName});");
            }

            // Окна IMGUI рисуются каждый кадр, если они есть в графе
            foreach (var (id, _, _) in ctx.ImguiWindows)
            {
                sb.AppendLine($"            SetupFunction(Setup.OnGUI, c_OnGui{id});");
            }
            // Настройки регистрируются отдельным хуком ModSettings
            var hasSettings = graph.Nodes.Any(n => n.DefinitionId != null &&
                                                  n.DefinitionId.StartsWith("msc.settings."));
            if (hasSettings)
            {
                sb.AppendLine("            SetupFunction(Setup.ModSettings, c_RegisterSettings);");
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            // --- обязательные свойства мода ---
            sb.AppendLine("        // ---------- Свойства мода ----------");
            sb.AppendLine($"        public override string ID => \"{EscapeId(project.Name)}\";");
            sb.AppendLine($"        public override string Name => \"{EscapeId(project.Name)}\";");
            sb.AppendLine($"        public override string Version => \"{EscapeId(project.Version)}\";");
            sb.AppendLine($"        public override string Author => \"{EscapeId(project.Author)}\";");
            sb.AppendLine($"        public override string Description => \"{EscapeId(project.Description)}\";");
            sb.AppendLine($"        public override Game SupportedGames => Game.{EscapeId(project.SupportedGames)};");
            sb.AppendLine();

            // --- настройки мода (тело формируется ниже как метод) ---
            var settingsNodes = graph.Nodes
                .Where(n => n.DefinitionId != null && n.DefinitionId.StartsWith("msc.settings."))
                .ToList();

            sb.AppendLine("        private void c_RegisterSettings()");
            sb.AppendLine("        {");
            if (settingsNodes.Count > 0)
            {
                foreach (var sn in settingsNodes)
                {
                    var title = CodegenContext.Quote(sn.Get("in.title", "Опция"));
                    var id = CodegenContext.Quote(project.Name + "_" + sn.Get("in.name", "option"));
                    var label = CodegenContext.Quote(sn.Get("in.name", "option"));
                    switch (sn.DefinitionId)
                    {
                        case "msc.settings.toggle":
                        {
                            var raw = sn.Get("in.default", "False");
                            var dv = raw == "True" || raw == "true" ? "true" : "false";
                            // AddCheckBox возвращает SettingsCheckBox - его сохраняем в поле,
                            // иначе прочитать значение later не получится.
                            sb.AppendLine($"            c_SettingToggle_{Sanitize(ctx.SettingId(sn))} = Settings.AddCheckBox({id}, {title}, {dv});");
                            break;
                        }
                        case "msc.settings.slider":
                            // AddSlider(settingID, name, min, max, value)
                            sb.AppendLine($"            c_SettingSlider_{Sanitize(ctx.SettingId(sn))} = Settings.AddSlider({id}, {title}, " +
                                          $"{sn.Get("in.min", "0")}f, {sn.Get("in.max", "10")}f, " +
                                          $"{sn.Get("in.default", "1")}f);");
                            break;
                        case "msc.settings.text":
                            // AddTextBox(settingID, name, value, placeholderText)
                            sb.AppendLine($"            c_SettingText_{Sanitize(ctx.SettingId(sn))} = Settings.AddTextBox({id}, {title}, " +
                                          $"{CodegenContext.Quote(sn.Get("in.default", ""))}, {title});");
                            break;
                        case "msc.settings.header":
                            sb.AppendLine($"            Settings.AddHeader({title});");
                            break;
                        case "msc.settings.button":
                            sb.AppendLine($"            Settings.AddButton({title}, OnButton);");
                            break;
                    }
                }
            }
            else
            {
                sb.AppendLine("            Settings.AddText(\"Настройки можно добавить узлами в палитре.\");");
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            // --- вспомогательные методы ---
            sb.AppendLine("        // ---------- Вспомогательные методы ----------");

            // Хранилище значений мода: простой текстовый файл "key=value" в папке мода.
            // MSCLoader 1.4.x не имеет SaveLoad.SaveValue, поэтому используем свой файл.
            sb.AppendLine("        private string c_DataPath =>");
            sb.AppendLine($"            System.IO.Path.Combine(ModLoader.GetModSettingsFolder(this), \"{EscapeId(project.Name)}_data.txt\");");
            sb.AppendLine();
            sb.AppendLine("        private void c_SaveText(string key, string value)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var path = c_DataPath;");
            sb.AppendLine("                var lines = System.IO.File.Exists(path)");
            sb.AppendLine("                    ? System.IO.File.ReadAllLines(path)");
            sb.AppendLine("                    : new string[0];");
            sb.AppendLine("                var list = new System.Collections.Generic.List<string>(lines);");
            sb.AppendLine("                int idx = list.FindIndex(l => l.StartsWith(key + \"=\", System.StringComparison.Ordinal));");
            sb.AppendLine("                var entry = key + \"=\" + (value ?? \"\");");
            sb.AppendLine("                if (value == null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    if (idx >= 0) list.RemoveAt(idx);");
            sb.AppendLine("                }");
            sb.AppendLine("                else if (idx >= 0) list[idx] = entry;");
            sb.AppendLine("                else list.Add(entry);");
            sb.AppendLine("                System.IO.File.WriteAllLines(path, list.ToArray());");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (System.Exception ex) { ModConsole.Error(\"Save failed: \" + ex.Message); }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private string c_LoadText(string key, string fallback)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var path = c_DataPath;");
            sb.AppendLine("                if (!System.IO.File.Exists(path)) return fallback;");
            sb.AppendLine("                foreach (var line in System.IO.File.ReadAllLines(path))");
            sb.AppendLine("                {");
            sb.AppendLine("                    if (line.StartsWith(key + \"=\", System.StringComparison.Ordinal))");
            sb.AppendLine("                        return line.Substring(key.Length + 1);");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (System.Exception ex) { ModConsole.Error(\"Load failed: \" + ex.Message); }");
            sb.AppendLine("            return fallback;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private float c_LoadFloat(string key, float fallback)");
            sb.AppendLine("        {");
            sb.AppendLine("            var raw = c_LoadText(key, null);");
            sb.AppendLine("            if (raw != null && float.TryParse(raw, System.Globalization.NumberStyles.Float,");
            sb.AppendLine("                System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;");
            sb.AppendLine("            return fallback;");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        private void c_SaveFloat(string key, float value)");
            sb.AppendLine("        {");
            sb.AppendLine("            c_SaveText(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        private void c_SaveInt(string key, int value)");
            sb.AppendLine("        {");
            sb.AppendLine("            c_SaveText(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        private void OnButton()");
            sb.AppendLine("        {");
            sb.AppendLine("            ModConsole.Log(\"Кнопка нажата\");");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        private System.Collections.IEnumerator c_SecondWait(float seconds)");
            sb.AppendLine("        {");
            sb.AppendLine("            yield return new WaitForSeconds(seconds);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Mod не наследует MonoBehaviour, поэтому для корутин нужен свой runner.
            sb.AppendLine("        private static MonoBehaviour c_CoroutineRunner;");
            sb.AppendLine();
            sb.AppendLine("        private void c_StartCoroutine(System.Collections.IEnumerator routine)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (c_CoroutineRunner == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                var go = new GameObject(\"MSCNodeIDE_Coroutines\");");
            sb.AppendLine("                UnityEngine.Object.DontDestroyOnLoad(go);");
            sb.AppendLine("                c_CoroutineRunner = go.AddComponent<MonoBehaviour>();");
            sb.AppendLine("                go.hideFlags = HideFlags.HideAndDontSave;");
            sb.AppendLine("            }");
            sb.AppendLine("            c_CoroutineRunner.StartCoroutine(routine);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // --- помощники для глобальных переменных PlayMaker ---
            // Реальные моды пишут FsmVariables.GlobalVariables.FindVariable("X") as FsmFloat.
            // Здесь это спрятано в перегруженные методы, чтобы код читался чисто.
            if (globalSets.Count > 0 || ctx.NeedsGlobalObjectReader)
            {
                sb.AppendLine("        // ---------- Глобальные переменные PlayMaker ----------");
                sb.AppendLine("        private static void c_GlobalSet(string name, float value)");
                sb.AppendLine("        {");
                sb.AppendLine("            var v = FsmVariables.GlobalVariables.FindVariable(name) as FsmFloat;");
                sb.AppendLine("            if (v != null) v.Value = value;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        private static void c_GlobalSet(string name, int value)");
                sb.AppendLine("        {");
                sb.AppendLine("            var v = FsmVariables.GlobalVariables.FindVariable(name) as FsmInt;");
                sb.AppendLine("            if (v != null) v.Value = value;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        private static void c_GlobalSet(string name, bool value)");
                sb.AppendLine("        {");
                sb.AppendLine("            var v = FsmVariables.GlobalVariables.FindVariable(name) as FsmBool;");
                sb.AppendLine("            if (v != null) v.Value = value;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        private static void c_GlobalSet(string name, string value)");
                sb.AppendLine("        {");
                sb.AppendLine("            var v = FsmVariables.GlobalVariables.FindVariable(name) as FsmString;");
                sb.AppendLine("            if (v != null) v.Value = value;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        private static bool c_GlobalGetObject(string name, out GameObject result)");
                sb.AppendLine("        {");
                sb.AppendLine("            var v = FsmVariables.GlobalVariables.FindVariable(name) as FsmGameObject;");
                sb.AppendLine("            if (v != null && v.Value != null) { result = v.Value; return true; }");
                sb.AppendLine("            var any = FsmVariables.GlobalVariables.FindVariable(name) as FsmObject;");
                sb.AppendLine("            if (any != null && any.Value != null)");
                sb.AppendLine("            {");
                sb.AppendLine("                var rb = any.Value as Rigidbody;");
                sb.AppendLine("                result = rb != null ? rb.gameObject : any.Value as GameObject;");
                sb.AppendLine("                return result != null;");
                sb.AppendLine("            }");
                sb.AppendLine("            result = null;");
                sb.AppendLine("            return false;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // --- помощники IMGUI ---
            if (ctx.ImguiWindows.Count > 0)
            {
                sb.AppendLine("        // ---------- IMGUI ----------");
                sb.AppendLine("        private static float c_GuiY = 0f;");
                sb.AppendLine("        private static float c_GuiWidth = 280f;");
                sb.AppendLine("        private static Vector2 c_GuiScroll = Vector2.zero;");
                sb.AppendLine();
                foreach (var (id, title, node) in ctx.ImguiWindows)
                {
                    var height = node.Get("in.height", "500");
                    // Метод, регистрируемый как хук OnGUI: рисует окно
                    sb.AppendLine($"        private void c_OnGui{id}()");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            c_GuiRect{id} = GUI.Window({id}, c_GuiRect{id}, c_DrawGui{id}, {CodegenContext.Quote(title)});");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                    sb.AppendLine($"        private void c_DrawGui{id}(int id)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            c_GuiWidth = {node.Get("in.width", "280")}f;");
                    sb.AppendLine("            c_GuiY = 24f;");
                    sb.AppendLine("            " + (ctx.ImguiWindowBodies.TryGetValue(id, out var __b) ? __b.TrimEnd() : ""));
                    sb.AppendLine("            GUI.DragWindow();");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                    // Прямоугольник окна
                    sb.AppendLine($"        private Rect c_GuiRect{id} = new Rect(20f, 20f, {node.Get("in.width", "280")}f, {height}f);");
                    sb.AppendLine();
                }
            }

            // --- поиск объектов и безопасный телепорт ---
            if (ctx.NeedsObjectFinder)
            {
                sb.AppendLine("        private static GameObject c_FindByName(string namePart)");
                sb.AppendLine("        {");
                sb.AppendLine("            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())");
                sb.AppendLine("                if (go != null && go.name.Contains(namePart)) return go;");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        private static GameObject c_FindByNameWithRigidbody(string namePart)");
                sb.AppendLine("        {");
                sb.AppendLine("            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())");
                sb.AppendLine("                if (go != null && go.name.Contains(namePart) && go.GetComponent<Rigidbody>() != null)");
                sb.AppendLine("                {");
                sb.AppendLine("                    var scene = go.scene;");
                sb.AppendLine("                    if (scene.IsValid() && scene.isLoaded) return go;");
                sb.AppendLine("                }");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            if (ctx.NeedsTeleportHelper)
            {
                sb.AppendLine("        private static void c_TeleportSafe(GameObject player, GameObject target)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (player == null || target == null) return;");
                sb.AppendLine("            var cc = player.GetComponent<CharacterController>();");
                sb.AppendLine("            if (cc != null) cc.enabled = false;");
                sb.AppendLine("            try");
                sb.AppendLine("            {");
                sb.AppendLine("                player.transform.position = target.transform.position");
                sb.AppendLine("                    + Vector3.up * 0.2f + target.transform.right * -1.5f;");
                sb.AppendLine("            }");
                sb.AppendLine("            finally { if (cc != null) cc.enabled = true; }");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Проигрывание звука: MSCLoader 1.4.x не имеет ModAudio.Play3DAudioClip,
            // поэтому создаём временный AudioSource (стандартный приём Unity-модов).
            sb.AppendLine("        private void c_PlaySound(string fileName, Vector3 position, float volume, float pitch, bool spatialize)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var path = System.IO.Path.Combine(ModLoader.GetModAssetsFolder(this), fileName);");
            sb.AppendLine("                if (!System.IO.File.Exists(path))");
            sb.AppendLine("                {");
            sb.AppendLine("                    ModConsole.Warning(\"Sound not found: \" + path);");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine("                var clip = ModAudio.LoadAudioClipFromFile(path, false);");
            sb.AppendLine("                if (clip == null) return;");
            sb.AppendLine("                var go = new GameObject(\"MSCNodeIDE_Sound\");");
            sb.AppendLine("                go.transform.position = position;");
            sb.AppendLine("                var src = go.AddComponent<AudioSource>();");
            sb.AppendLine("                src.clip = clip;");
            sb.AppendLine("                src.volume = Mathf.Clamp01(volume);");
            sb.AppendLine("                src.pitch = pitch;");
            sb.AppendLine("                src.spatialBlend = spatialize ? 1f : 0f;");
            sb.AppendLine("                src.Play();");
            sb.AppendLine("                UnityEngine.Object.Destroy(go, clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch)));");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (System.Exception ex) { ModConsole.Error(\"Sound failed: \" + ex.Message); }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// Тело подграфа, подключённого к порту узла, как готовый текст.
        /// Используется для методов отрисовки IMGUI-окон.
        /// </summary>
        private static string GetInjectedBodyLiteral(CodegenContext ctx, NodeInstance node, string port)
        {
            var body = ctx.GetInjectedBody(node, port);
            if (string.IsNullOrWhiteSpace(body)) return "";
            return body.TrimEnd() + "\r\n";
        }

        /// <summary>Сдвигает блок кода на указанное число уровней вложенности.</summary>
        private static string Reindent(string code, int levels)
        {
            var pad = new string(' ', levels * 4);
            var lines = code.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i].TrimEnd('\r');
                if (l.Trim().Length == 0) { lines[i] = ""; continue; }
                lines[i] = pad + l.TrimStart();
            }
            return string.Join("\r\n", lines);
        }

        private static string EscapeId(string s) =>
            (s ?? "").Replace("\\", "").Replace("\"", "'").Trim();

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "v";
            var sb = new StringBuilder();
            foreach (var ch in s)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            var r = sb.ToString().Trim('_');
            if (r.Length == 0) return "v";
            if (char.IsDigit(r[0])) return "_" + r;
            return r;
        }

        private static string Cap(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}

