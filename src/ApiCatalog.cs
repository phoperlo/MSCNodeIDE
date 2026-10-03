using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MSCNodeIDE.Core
{
    public sealed class ApiParameter
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public bool HasDefault { get; set; }
        public string DefaultText { get; set; }
    }

    public sealed class ApiMethod
    {
        public string Key { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public string Signature { get; set; }
        public string ReturnType { get; set; }
        public string ReturnName { get; set; }
        public TypeInfo OwningType { get; set; }
        public ApiParameter[] Parameters { get; set; } = Array.Empty<ApiParameter>();
        public bool IsVoid => ReturnType == "void";
    }

    /// <summary>
    /// Сканирует реальные DLL игры (MetadataLoadContext) и строит каталог методов.
    /// Только чтение метаданных — ничего не выполняется.
    /// </summary>
    public sealed class ApiCatalog : IDisposable
    {
        private MetadataLoadContext _mlc;
        public List<ApiMethod> StaticMethods { get; } = new List<ApiMethod>();
        public List<string> AssembliesLoaded { get; } = new List<string>();
        public List<string> LoadErrors { get; } = new List<string>();

        public string ManagedPath { get; private set; }

        public static ApiCatalog Load(string managedPath)
        {
            var cat = new ApiCatalog();
            cat.ManagedPath = managedPath;
            try
            {
                cat.Build(managedPath);
            }
            catch (Exception ex)
            {
                cat.LoadErrors.Add("Не удалось прочитать сборки: " + ex.Message);
            }
            return cat;
        }

        private void Build(string managedPath)
        {
            // Игра поставляет свой mscorlib 2.0.5.0, а рантайм .NET содержит 4.0.0.0.
            // PathAssemblyResolver чувствителен к версии, поэтому единого правильного
            // набора путей нет: в зависимости от окружения лучше работает то один,
            // то другой вариант. Перебираем кандидатов и оставляем тот, где
            // читается больше всего типов игры.
            var targets = new[] { "MSCLoader.dll", "UnityEngine.dll", "Assembly-CSharp.dll", "PlayMaker.dll" }
                .Select(f => Path.Combine(managedPath, f))
                .Where(File.Exists)
                .ToList();

            if (targets.Count == 0)
            {
                LoadErrors.Add("В папке Managed не найдено ни одной сборки игры.");
                return;
            }

            var gameDlls = Directory.GetFiles(managedPath, "*.dll");
            var rt = RuntimeEnvironmentDir();
            var runtimeDlls = (!string.IsNullOrEmpty(rt) && Directory.Exists(rt))
                ? Directory.GetFiles(rt, "*.dll")
                : Array.Empty<string>();

            var candidates = new List<(string Name, string[] Paths)>
            {
                ("runtime-core", Merge(runtimeDlls, gameDlls)),
                ("game-core",    Merge(gameDlls, runtimeDlls)),
            };

            MetadataLoadContext best = null;
            var bestScore = -1;
            var bestName = "";
            var bestMethods = new List<ApiMethod>();
            var bestLoaded = new List<string>();
            var allErrors = new List<string>();

            foreach (var (name, paths) in candidates)
            {
                var ctx = new MetadataLoadContext(new PathAssemblyResolver(paths));
                var methods = new List<ApiMethod>();
                var loaded = new List<string>();
                var errors = new List<string>();
                var score = 0;

                foreach (var file in targets)
                {
                    var asmName = Path.GetFileName(file);
                    try
                    {
                        var asm = ctx.LoadFromAssemblyPath(file);
                        var before = methods.Count;
                        CollectFrom(asm, Path.GetFileNameWithoutExtension(file), methods);
                        loaded.Add(asmName);
                        score += methods.Count - before;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{asmName}: {ex.Message}");
                    }
                }

                if (score > bestScore)
                {
                    best?.Dispose();
                    best = ctx;
                    bestScore = score;
                    bestName = name;
                    bestMethods = methods;
                    bestLoaded = loaded;
                    allErrors = errors;
                }
                else
                {
                    ctx.Dispose();
                }
            }

            _mlc = best;
            StaticMethods.AddRange(bestMethods);
            AssembliesLoaded.AddRange(bestLoaded);

            // ошибки показываем только если что-то реально не прочиталось
            if (bestLoaded.Count < targets.Count)
                LoadErrors.AddRange(allErrors);

            LoadErrors.Insert(0, $"резолвер: {bestName} (прочитано типов: {bestScore})");
        }

        /// <summary>Объединяет наборы путей; приоритет у первого аргумента.</summary>
        private static string[] Merge(string[] primary, string[] secondary)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in primary)
            {
                var n = Path.GetFileName(p);
                if (!map.ContainsKey(n)) map[n] = p;
            }
            foreach (var p in secondary)
            {
                var n = Path.GetFileName(p);
                if (!map.ContainsKey(n)) map[n] = p;
            }
            return map.Values.ToArray();
        }

        private static void CollectFrom(Assembly asm, string asmName, List<ApiMethod> into)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }
            catch { return; }

            foreach (var t in types)
            {
                if (t == null || !t.IsPublic && !t.IsNestedPublic) continue;
                if (t.Name.StartsWith("<")) continue;

                MethodInfo[] methods;
                try { methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly); }
                catch { continue; }

                foreach (var mi in methods)
                {
                    if (mi.Name.StartsWith("get_") || mi.Name.StartsWith("set_")) continue;
                    if (mi.IsSpecialName) continue;
                    if (mi.DeclaringType == null) continue;
                    if (mi.ContainsGenericParameters) continue;

                    var ps = mi.GetParameters();
                    if (ps.Length > 6) continue;
                    bool bad = false;
                    foreach (var p in ps)
                    {
                        if (p.ParameterType == null) { bad = true; break; }
                        if (p.ParameterType.IsByRef) { bad = true; break; }
                        if (p.ParameterType.IsPointer) { bad = true; break; }
                        if (p.ParameterType.ContainsGenericParameters) { bad = true; break; }
                    }
                    if (bad) continue;

                    var returnType = TypeName(mi.ReturnType);
                    var sb = new StringBuilder();
                    sb.Append($"static {returnType} {t.Name}.{mi.Name}(");
                    sb.Append(string.Join(", ", ps.Select((p, i) =>
                        $"{TypeName(p.ParameterType)} {p.Name ?? ("arg" + i)}")));
                    sb.Append(")  —  из " + asmName + ".dll");

                    into.Add(new ApiMethod
                    {
                        Key = asmName + "|" + t.FullName + "|" + mi.Name + "|" +
                              string.Join(",", ps.Select(p => TypeName(p.ParameterType))),
                        Category = asmName + " / " + (t.Namespace ?? "Global"),
                        Name = mi.Name,
                        Signature = sb.ToString(),
                        ReturnType = returnType,
                        ReturnName = returnType == "void" ? "—" : returnType,
                        OwningType = t.GetTypeInfo(),
                        Parameters = ps.Select((p, i) => new ApiParameter
                        {
                            Name = p.Name ?? ("arg" + i),
                            TypeName = TypeName(p.ParameterType)
                        }).ToArray()
                    });
                }
            }
        }

        private static string TypeName(Type t)
        {
            if (t == null) return "object";
            if (t.IsByRef) return TypeName(t.GetElementType());
            switch (t.FullName)
            {
                case "System.Boolean": return "bool";
                case "System.Int32": return "int";
                case "System.Single": return "float";
                case "System.String": return "string";
                case "System.Void": return "void";
                case "System.Object": return "object";
                case "UnityEngine.Vector2": return "UnityEngine.Vector2";
                case "UnityEngine.Vector3": return "UnityEngine.Vector3";
                case "UnityEngine.Vector4": return "UnityEngine.Vector4";
                case "UnityEngine.Quaternion": return "UnityEngine.Quaternion";
                case "UnityEngine.Color": return "UnityEngine.Color";
                case "UnityEngine.GameObject": return "UnityEngine.GameObject";
                case "UnityEngine.Transform": return "UnityEngine.Transform";
                case "UnityEngine.Rigidbody": return "UnityEngine.Rigidbody";
                case "UnityEngine.AudioClip": return "UnityEngine.AudioClip";
                case "UnityEngine.Texture2D": return "UnityEngine.Texture2D";
                case "UnityEngine.Mesh": return "UnityEngine.Mesh";
                case "UnityEngine.Material": return "UnityEngine.Material";
                case "UnityEngine.Camera": return "UnityEngine.Camera";
                case "UnityEngine.Light": return "UnityEngine.Light";
            }
            if (t.IsGenericType)
            {
                var name = t.Name.Split('`')[0];
                var args = t.GetGenericArguments().Select(TypeName);
                return name + "<" + string.Join(", ", args) + ">";
            }
            if (t.IsEnum) return t.Name;
            return t.FullName ?? t.Name;
        }

        /// <summary>
        /// Каталог с реальными файлами сборок .NET для MetadataLoadContext.
        /// В single-file публикации typeof(object).Assembly.Location пуст, поэтому
        /// ищем установленный общий рантайм на диске.
        /// </summary>
        private static string RuntimeEnvironmentDir()
        {
            // 1) обычный случай: приложение запущено из папки
            var dir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                if (File.Exists(Path.Combine(dir, "System.Runtime.dll")) ||
                    File.Exists(Path.Combine(dir, "System.Private.CoreLib.dll")))
                    return dir;
            }

            // 2) single-file: ищем установленный .NET (общий или в комплекте)
            var candidates = new List<string>();

            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(dotnetRoot))
                candidates.Add(dotnetRoot);

            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.Add(Path.Combine(pf, "dotnet"));

            // 3) папка самого приложения (в папочной публикации там есть DLL)
            candidates.Add(AppContext.BaseDirectory);

            foreach (var root in candidates)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                var shared = Path.Combine(root, "shared");
                if (!Directory.Exists(shared)) continue;

                string best = null;
                Version bestVer = null;
                foreach (var tfm in new[] { "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App" })
                {
                    var tfmDir = Path.Combine(shared, tfm);
                    if (!Directory.Exists(tfmDir)) continue;
                    foreach (var ver in Directory.GetDirectories(tfmDir))
                    {
                        if (!File.Exists(Path.Combine(ver, "System.Runtime.dll"))) continue;
                        var name = Path.GetFileName(ver);
                        if (!Version.TryParse(name, out var v)) continue;
                        if (bestVer == null || v > bestVer) { bestVer = v; best = ver; }
                    }
                }
                if (best != null) return best;

                if (File.Exists(Path.Combine(root, "System.Runtime.dll"))) return root;
            }

            // 4) последний шанс — папка приложения
            return AppContext.BaseDirectory;
        }

        public void Dispose()
        {
            _mlc?.Dispose();
            _mlc = null;
        }
    }
}