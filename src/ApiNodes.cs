using System;
using System.Collections.Generic;
using System.Linq;

namespace MSCNodeIDE.Core
{

/// <summary>
    /// Узлы, сгенерированные автоматически из реальных DLL игры через ApiCatalog
    /// (MetadataLoadContext). Позволяют вызывать любой статический метод
    /// MSCLoader / UnityEngine / Assembly-CSharp без ручного написания кода.
    /// </summary>
    internal static class ApiNodes
    {
        public static void Register(NodeLibrary lib, ApiCatalog api)
        {
            if (api == null) return;

            foreach (var m in api.StaticMethods)
            {
                var def = Def.Make("api." + m.Key, m.Category,
                    $"{m.OwningType.Name}.{m.Name}", m.Signature);
                for (int i = 0; i < m.Parameters.Length; i++)
                {
                    var p = m.Parameters[i];
                    def.AddIn("p" + i, p.Name + " : " + p.TypeName, MapKind(p.TypeName),
                              literal: true, def_: null);
                }
                if (!m.IsVoid)
                    def.AddOut("out", m.ReturnName, MapKind(m.ReturnType));
                def.AddOut("execOut", "Выполнить", PortKind.Execution);

                def.EmitStatements = (c, n) =>
                {
                    var args = new List<string>();
                    for (int i = 0; i < m.Parameters.Length; i++)
                        args.Add(c.ResolveInput(n, c.Port(n, "p" + i)));

                    var call = $"{m.OwningType.FullName}.{m.Name}({string.Join(", ", args)})";

                    // Если результат не подключён к выходу — просто вызываем,
                    // иначе сохраняем в именованную локальную переменную.
                    var consumers = c.OutgoingAll(n, "out").ToList();
                    if (m.IsVoid || consumers.Count == 0)
                    {
                        c.Line(call + ";");
                    }
                    else
                    {
                        var kind = MapKind(m.ReturnType);
                        var typeName = kind.CSharp();
                        var declType = (PortKindUtil.IsObjectLike(kind) || kind == PortKind.Void)
                            ? "var" : typeName;
                        var tmp = c.TempName(SanitizeName(m.OwningType.Name + m.Name));
                        c.Line($"{declType} {tmp} = {call};");
                        foreach (var conn in consumers)
                        {
                            var next = c.Graph.Find(conn.ToNode);
                            if (next != null) c.SetExternalValue(next, "in." + conn.ToPort, tmp);
                        }
                    }

                    foreach (var conn in c.OutgoingAll(n, "execOut"))
                    {
                        var next = c.Graph.Find(conn.ToNode);
                        if (next != null) c.RunStatements(next, c.NodeDef(next));
                    }
                };

                lib.Register(def);
            }
        }

        private static string SanitizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "v";
            var sb = new System.Text.StringBuilder();
            foreach (var ch in s)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            return sb.ToString();
        }

        private static PortKind MapKind(string clrType)
        {
            if (clrType == null) return PortKind.Object;
            switch (clrType)
            {
                case "bool": return PortKind.Bool;
                case "int": return PortKind.Int;
                case "float": return PortKind.Float;
                case "string": return PortKind.String;
                case "UnityEngine.Vector2": return PortKind.Vector2;
                case "UnityEngine.Vector3": return PortKind.Vector3;
                case "UnityEngine.Color": return PortKind.Color;
                case "UnityEngine.GameObject": return PortKind.GameObject;
                case "UnityEngine.Transform": return PortKind.Transform;
                case "UnityEngine.Rigidbody": return PortKind.Rigidbody;
                case "UnityEngine.AudioClip": return PortKind.AudioClip;
                case "UnityEngine.Texture2D": return PortKind.Texture2D;
                case "UnityEngine.Mesh": return PortKind.Mesh;
                case "UnityEngine.Material": return PortKind.Material;
                default: return PortKind.Object;
            }
        }
    }
    }
