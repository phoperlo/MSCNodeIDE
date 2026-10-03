using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MSCNodeIDE.Core
{
    public sealed class ModProjectFile
    {
        public ModProjectSettings Settings { get; set; } = new ModProjectSettings();
        public List<NodeGraph> Graphs { get; set; } = new List<NodeGraph>();
        public string RootPath { get; set; }
        public string GeneratedDir => Path.Combine(RootPath ?? "", "Generated");
        public string GraphFilePath => Path.Combine(RootPath ?? "", "project.json");
    }

    public sealed class BuildResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = "";
        public List<BuildError> Errors { get; set; } = new List<BuildError>();
        public TimeSpan Duration { get; set; }
        public string DllPath { get; set; }
    }

    public sealed class BuildError
    {
        public string File { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
        public override string ToString()
            => $"{File}({Line},{Column}): {Severity}: {Message}";
    }

    /// <summary>Создаёт файлы проекта мода и запускает сборку.</summary>
    public static class ProjectManager
    {
        public const string DefaultNamespace = "";

        /// <summary>Ищет установленную My Summer Car через Steam-библиотеки.</summary>
        public static string FindGameDirectory()
        {
            var candidates = new List<string>();

            // 1. Steam-библиотеки из libraryfolders.vdf
            var steamRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\Steam",
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Steam",
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Steam",
                @"C:\Program Files (x86)\Steam",
                @"D:\SteamLibrary"
            };

            foreach (var root in steamRoots)
            {
                var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    var text = File.ReadAllText(vdf);
                    foreach (System.Text.RegularExpressions.Match m in
                        System.Text.RegularExpressions.Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
                    {
                        var p = m.Groups[1].Value.Replace("\\\\", "\\");
                        candidates.Add(Path.Combine(p, "steamapps", "common", "My Summer Car"));
                    }
                }
                catch { }
            }

            foreach (var root in steamRoots)
                candidates.Add(Path.Combine(root, "steamapps", "common", "My Summer Car"));

            foreach (var c in candidates.Distinct())
            {
                if (!string.IsNullOrEmpty(c) && Directory.Exists(c) &&
                    Directory.Exists(Path.Combine(c, "mysummercar_Data", "Managed")))
                    return c;
            }
            return null;
        }

        public static string ManagedDirectory(string gameDir)
            => Path.Combine(gameDir ?? "", "mysummercar_Data", "Managed");

        /// <summary>Создаёт структуру папок проекта мода.</summary>
        public static void CreateProjectStructure(string root, ModProjectSettings settings)
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "Generated"));
            Directory.CreateDirectory(Path.Combine(root, settings.Name + "_Assets"));
            var bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(bin);
        }

        /// <summary>Генерирует .csproj по официальному шаблону MSCLoader.</summary>
        public static string GenerateCsproj(ModProjectSettings settings, string gameDir)
        {
            var managed = ManagedDirectory(gameDir);
            var root = gameDir ?? "";
            var modsDir = Path.Combine(root, "Mods");

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
            sb.AppendLine("  <Import Project=\"$(MSBuildExtensionsPath)\\$(MSBuildToolsVersion)\\Microsoft.Common.props\" Condition=\"Exists('$(MSBuildExtensionsPath)\\$(MSBuildToolsVersion)\\Microsoft.Common.props')\" />");
            sb.AppendLine("  <PropertyGroup>");
            sb.AppendLine("    <Configuration Condition=\" '$(Configuration)' == '' \">Release</Configuration>");
            sb.AppendLine("    <Platform Condition=\" '$(Platform)' == '' \">AnyCPU</Platform>");
            sb.AppendLine("    <ProjectGuid></ProjectGuid>");
            sb.AppendLine("    <OutputType>Library</OutputType>");
            sb.AppendLine("    <AppDesignerFolder>Properties</AppDesignerFolder>");
            sb.AppendLine($"    <RootNamespace>{settings.CleanNamespace}</RootNamespace>");
            sb.AppendLine($"    <AssemblyName>{settings.Name}</AssemblyName>");
            sb.AppendLine($"    <TargetFrameworkVersion>{settings.TargetFramework}</TargetFrameworkVersion>");
            sb.AppendLine("    <FileAlignment>512</FileAlignment>");
            // TargetFrameworkProfile намеренно не указывается: профиль "Unity Full"
            // есть только в Visual Studio с Unity-модулем. При сборке через dotnet SDK
            // используются reference assemblies из NuGet-пакета.
            sb.AppendLine("    <LangVersion>13</LangVersion>");
            sb.AppendLine("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>");
            sb.AppendLine("    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>");
            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine("  <PropertyGroup Condition=\" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' \">");
            sb.AppendLine("    <DebugSymbols>true</DebugSymbols>");
            sb.AppendLine("    <DebugType>full</DebugType>");
            sb.AppendLine("    <Optimize>false</Optimize>");
            sb.AppendLine("    <OutputPath>bin\\</OutputPath>");
            sb.AppendLine("    <DefineConstants>DEBUG;TRACE</DefineConstants>");
            sb.AppendLine("    <WarningLevel>4</WarningLevel>");
            sb.AppendLine("    <PlatformTarget>x64</PlatformTarget>");
            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine("  <PropertyGroup Condition=\" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' \">");
            sb.AppendLine("    <DebugType>none</DebugType>");
            sb.AppendLine("    <Optimize>true</Optimize>");
            sb.AppendLine("    <OutputPath>bin\\</OutputPath>");
            sb.AppendLine("    <DefineConstants>TRACE</DefineConstants>");
            sb.AppendLine("    <WarningLevel>4</WarningLevel>");
            sb.AppendLine("    <PlatformTarget>x64</PlatformTarget>");
            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine("  <ItemGroup>");

            void Ref(string name, string file = null, string hintRoot = null)
            {
                var baseDir = hintRoot ?? managed;
                sb.AppendLine($"    <Reference Include=\"{name}\">");
                sb.AppendLine($"      <HintPath>{Path.Combine(baseDir, file ?? (name + ".dll"))}</HintPath>");
                sb.AppendLine("      <Private>False</Private>");
                sb.AppendLine("    </Reference>");
            }

            sb.AppendLine("    <Reference Include=\"System\" />");
            sb.AppendLine("    <Reference Include=\"System.Core\" />");
            sb.AppendLine("    <Reference Include=\"System.Xml\" />");
            sb.AppendLine("    <Reference Include=\"System.Xml.Linq\" />");
            sb.AppendLine("    <Reference Include=\"System.Data\" />");
            sb.AppendLine("    <Reference Include=\"System.Data.DataSetExtensions\" />");
            Ref("UnityEngine");
            Ref("MSCLoader");
            Ref("Assembly-CSharp");
            Ref("Assembly-CSharp-firstpass");
            Ref("cInput");
            Ref("PlayMaker");
            Ref("UnityEngine.UI");
            Ref("0Harmony");
            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine("    <Compile Include=\"Generated\\**\\*.cs\" />");
            sb.AppendLine("    <Compile Include=\"Properties\\AssemblyInfo.cs\" />");
            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine("  <Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" />");
            sb.AppendLine("  <PropertyGroup>");
            sb.AppendLine($"    <MSCMODSFOLDER>{modsDir}</MSCMODSFOLDER>");
            sb.AppendLine("  </PropertyGroup>");
            // Reference assemblies: нужны, чтобы собрать .NET Framework без Visual Studio.
            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine($"    <PackageReference Include=\"Microsoft.NETFramework.ReferenceAssemblies\" Version=\"1.0.3\">");
            sb.AppendLine($"      <PrivateAssets>all</PrivateAssets>");
            sb.AppendLine($"      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>");
            sb.AppendLine("    </PackageReference>");
            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine("</Project>");
            return sb.ToString();
        }

        public static string GenerateAssemblyInfo(ModProjectSettings settings)
        {
            // Только ASCII и только константные выражения: файл читает компилятор
            // пользователя, и он должен быть предсказуем в любой кодировке.
            var year = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.AppendLine("using System.Reflection;");
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine();
            sb.AppendLine("[assembly: AssemblyTitle(\"" + Escape(settings.Name) + "\")]");
            sb.AppendLine("[assembly: AssemblyDescription(\"" + Escape(settings.Description) + "\")]");
            sb.AppendLine("[assembly: AssemblyConfiguration(\"\")]");
            sb.AppendLine("[assembly: AssemblyCompany(\"" + Escape(settings.Author) + "\")]");
            sb.AppendLine("[assembly: AssemblyProduct(\"MSCNodeIDE\")]");
            sb.AppendLine("[assembly: AssemblyCopyright(\"Copyright (c) " + year + "\")]");
            sb.AppendLine($"[assembly: AssemblyVersion(\"{NormalizeVersion(settings.Version)}\")]");
            sb.AppendLine($"[assembly: AssemblyFileVersion(\"{NormalizeVersion(settings.Version)}\")]");
            return sb.ToString();
        }

        /// <summary>
        /// Кодировка для генерируемых .cs/.csproj: UTF-8 с BOM.
        /// Без BOM компилятор читает файл в системной ANSI-кодировке,
        /// и кириллица в текстах мода превращается в мусор.
        /// </summary>
        public static readonly Encoding Utf8Bom = new UTF8Encoding(true);

        /// <summary>Пишет файл проекта в UTF-8 с BOM.</summary>
        public static void WriteUtf8(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, content, Utf8Bom);
        }

        private static string NormalizeVersion(string v)
        {
            var parts = (v ?? "1.0.0").Split('.');
            while (parts.Length < 4) parts = parts.Concat(new[] { "0" }).ToArray();
            return string.Join(".", parts.Take(4));
        }

        private static string Escape(string s)
        {
            // В AssemblyInfo попадает только ASCII: файл читает компилятор пользователя.
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch > 127) continue;
                sb.Append(ch == '"' ? '\'' : (ch == '\r' || ch == '\n') ? ' ' : ch);
            }
            return sb.ToString();
        }

        /// <summary>Запускает msbuild/dotnet build проекта мода.</summary>
        public static BuildResult Build(string projectDir, ModProjectSettings settings, bool copyToMods)
        {
            var result = new BuildResult();
            var sw = Stopwatch.StartNew();
            var csproj = Directory.GetFiles(projectDir, "*.csproj").FirstOrDefault();
            if (csproj == null)
            {
                result.Errors.Add(new BuildError { Message = "Файл .csproj не найден в папке проекта" });
                return result;
            }

            var output = new StringBuilder();
            var errors = new List<BuildError>();

            int exitCode = -1;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{csproj}\" -c Release -v minimal --nologo",
                    WorkingDirectory = projectDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
                psi.Environment["DOTNET_NOLOGO"] = "1";

                using var proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                exitCode = proc.ExitCode;
            }
            catch (Exception ex)
            {
                result.Errors.Add(new BuildError { Message = "Не удалось запустить dotnet: " + ex.Message });
                output.AppendLine(ex.Message);
            }

            result.Output = output.ToString();
            result.Errors.AddRange(ParseErrors(result.Output));
            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Success = exitCode == 0 && !result.Errors.Any(e => e.Severity == "error");

            if (result.Success)
            {
                var dll = Path.Combine(projectDir, "bin", settings.Name + ".dll");
                if (File.Exists(dll))
                {
                    result.DllPath = dll;
                    if (copyToMods && !string.IsNullOrEmpty(settings.GameDirectory))
                    {
                        var mods = Path.Combine(settings.GameDirectory, "Mods");
                        Directory.CreateDirectory(mods);
                        File.Copy(dll, Path.Combine(mods, settings.Name + ".dll"), true);
                    }
                }
                else
                {
                    result.Success = false;
                    result.Errors.Add(new BuildError { Message = "Сборка успешна, но DLL не найдена: " + dll });
                }
            }
            return result;
        }

        public static List<BuildError> ParseErrors(string output)
        {
            var list = new List<BuildError>();
            foreach (var raw in (output ?? "").Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                if (line.Contains(" error ") || line.Contains(" warning "))
                {
                    var e = new BuildError();
                    var fileIdx = line.IndexOf('(');
                    if (fileIdx > 0)
                    {
                        var filePart = line.Substring(0, fileIdx);
                        var rest = line.Substring(fileIdx + 1);
                        var close = rest.IndexOf(')');
                        if (close > 0)
                        {
                            e.File = filePart;
                            var loc = rest.Substring(0, close).Split(',');
                            int.TryParse(loc[0], out var ln); e.Line = ln;
                            if (loc.Length > 1) { int.TryParse(loc[1], out var cl); e.Column = cl; }
                            var tail = rest.Substring(close + 1).Trim();
                            if (tail.StartsWith(":"))
                            {
                                tail = tail.Substring(1).Trim();
                                if (tail.StartsWith("error", StringComparison.OrdinalIgnoreCase))
                                {
                                    e.Severity = "error";
                                    e.Message = tail.Substring(5).Trim();
                                }
                                else if (tail.StartsWith("warning", StringComparison.OrdinalIgnoreCase))
                                {
                                    e.Severity = "warning";
                                    e.Message = tail.Substring(7).Trim();
                                }
                                else e.Message = tail;
                            }
                            else e.Message = tail;
                        }
                    }
                    if (string.IsNullOrEmpty(e.Message)) e.Message = line;
                    list.Add(e);
                }
            }
            return list;
        }
    }
}