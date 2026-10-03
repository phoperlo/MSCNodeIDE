using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MSCNodeIDE.Core
{
    /// <summary>Сохранение и загрузка проекта мода в JSON.</summary>
    public static class ProjectSerializer
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static void Save(ModProjectFile project, string path)
        {
            var json = JsonSerializer.Serialize(project, Options);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // С BOM: проект читают и редактор, и пользователь в блокноте
        File.WriteAllText(path, json, new System.Text.UTF8Encoding(true));
        }

        public static ModProjectFile Load(string path)
        {
            var json = File.ReadAllText(path);
            var project = JsonSerializer.Deserialize<ModProjectFile>(json, Options);
            project.RootPath = Path.GetDirectoryName(Path.GetFullPath(path));
            return project;
        }

        public static bool TryLoad(string path, out ModProjectFile project, out string error)
        {
            project = null;
            error = null;
            try
            {
                project = Load(path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TrySaveTo(string path, ModProjectFile project, out string error)
        {
            error = null;
            try
            {
                project.RootPath = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
                Save(project, path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Шаблон нового проекта: точка входа + лог + сообщение в игре.</summary>
        public static NodeGraph CreateStarterGraph(string modName = "MyMod", string version = "1.0.0")
        {
            var g = new NodeGraph { Name = "Main" };

            var entry = new NodeInstance { Id = "onload1", DefinitionId = "flow.onload", X = 80, Y = 120 };

            var log = new NodeInstance { Id = "log1", DefinitionId = "unity.log", X = 360, Y = 120 };
            log.Set("in.message", "Мод загружен!");
            log.Set("in.level", "Log");

            var msg = new NodeInstance { Id = "msg1", DefinitionId = "unity.showmessage", X = 360, Y = 260 };
            msg.Set("in.message", "Мод работает!");
            msg.Set("in.title", modName);
            msg.Set("in.subtitle", "v" + version);

            g.Nodes.Add(entry);
            g.Nodes.Add(log);
            g.Nodes.Add(msg);

            g.Connections.Add(new Connection { FromNode = entry.Id, FromPort = "out", ToNode = log.Id, ToPort = "exec" });
            g.Connections.Add(new Connection { FromNode = entry.Id, FromPort = "out", ToNode = msg.Id, ToPort = "exec" });

            return g;
        }
    }
}