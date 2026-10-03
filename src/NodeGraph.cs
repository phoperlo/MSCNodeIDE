using System;
using System.Collections.Generic;
using System.Linq;

namespace MSCNodeIDE.Core
{
    /// <summary>Тип данных, передаваемый через порт.</summary>
    public enum PortKind
    {
        Execution,
        Bool,
        Int,
        Float,
        String,
        Vector2,
        Vector3,
        Color,
        Object,
        GameObject,
        Component,
        Transform,
        Rigidbody,
        AudioClip,
        Texture2D,
        Mesh,
        Material,
        Void
    }

    public static class PortKindUtil
    {
        public static string CSharp(this PortKind k, string literal = null)
        {
            switch (k)
            {
                case PortKind.Bool: return "bool";
                case PortKind.Int: return "int";
                case PortKind.Float: return "float";
                case PortKind.String: return "string";
                case PortKind.Vector2: return "Vector2";
                case PortKind.Vector3: return "Vector3";
                case PortKind.Color: return "Color";
                case PortKind.GameObject: return "GameObject";
                case PortKind.Transform: return "Transform";
                case PortKind.Rigidbody: return "Rigidbody";
                case PortKind.AudioClip: return "AudioClip";
                case PortKind.Texture2D: return "Texture2D";
                case PortKind.Mesh: return "Mesh";
                case PortKind.Material: return "Material";
                case PortKind.Component: return "Component";
                case PortKind.Object: return "UnityEngine.Object";
                default: return "object";
            }
        }

        /// <summary>Можно ли подключить выход типа src к входу dst.</summary>
        public static bool Assignable(PortKind src, PortKind dst)
        {
            if (src == dst) return true;
            if (src == PortKind.Execution || dst == PortKind.Execution) return false;
            if (src == PortKind.Void || dst == PortKind.Void) return false;
            // Component -> любой производный тип компонента
            if (src == PortKind.Component) return true;
            if (dst == PortKind.Component) return true;
            if (dst == PortKind.Object) return true;
            if (src == PortKind.Object && dst != PortKind.Object) return false;
            // Числа смешиваем
            if (IsNumeric(src) && IsNumeric(dst)) return true;
            return false;
        }

        public static bool IsNumeric(PortKind k) =>
            k == PortKind.Int || k == PortKind.Float;

        public static bool IsObjectLike(PortKind k) =>
            k == PortKind.GameObject || k == PortKind.Component || k == PortKind.Object;
    }

    public sealed class Port
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public PortKind Kind { get; set; }
        public bool IsInput { get; set; }
        public bool IsLiteral { get; set; }          // можно задать значение прямо в инспекторе
        public object LiteralValue { get; set; }     // значение по умолчанию для literal-порта
        public bool Required { get; set; }
        public string Tooltip { get; set; }

        public Port Clone() => (Port)MemberwiseClone();
    }

    public sealed class NodeDefinition
    {
        public string Id { get; set; }               // "unity.enginetransform.get.position"
        public string Category { get; set; }         // "Unity / Transform"
        public string Title { get; set; }
        public string Description { get; set; }
        public List<Port> Ports { get; set; } = new List<Port>();

        // Резолв выражения. context даёт доступ к связанным значениям и генератору.
        public Func<CodegenContext, NodeInstance, string> Emit { get; set; }

        // Вызывается для узлов-заявлений (сгенерировать строки кода).
        public Action<CodegenContext, NodeInstance> EmitStatements { get; set; }

        public Port InputPort(string id) => Ports.FirstOrDefault(p => p.IsInput && p.Id == id);
        public Port OutputPort(string id) => Ports.FirstOrDefault(p => !p.IsInput && p.Id == id);
        public IEnumerable<Port> Inputs => Ports.Where(p => p.IsInput);
        public IEnumerable<Port> Outputs => Ports.Where(p => !p.IsInput);
    }

    public sealed class NodeInstance
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string DefinitionId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public string Comment { get; set; }

        /// <summary>Значения literal-портов и прочие свойства узла.</summary>
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();

        public string Get(string key, string fallback = null)
            => Properties.TryGetValue(key, out var v) ? v : fallback;

        public void Set(string key, string value)
        {
            if (value == null) Properties.Remove(key);
            else Properties[key] = value;
        }
    }

    public sealed class Connection
    {
        public string FromNode { get; set; }
        public string FromPort { get; set; }
        public string ToNode { get; set; }
        public string ToPort { get; set; }
    }

    /// <summary>Граф визуального скрипта — целиком сериализуется в JSON проекта.</summary>
    public sealed class NodeGraph
    {
        public string Name { get; set; } = "Main";
        public List<NodeInstance> Nodes { get; set; } = new List<NodeInstance>();
        public List<Connection> Connections { get; set; } = new List<Connection>();

        public NodeInstance Find(string id) => Nodes.FirstOrDefault(n => n.Id == id);

        public IEnumerable<Connection> Incoming(string nodeId, string portId) =>
            Connections.Where(c => c.ToNode == nodeId && c.ToPort == portId);

        public IEnumerable<Connection> Outgoing(string nodeId, string portId) =>
            Connections.Where(c => c.FromNode == nodeId && c.FromPort == portId);

        public Connection OutgoingAny(string nodeId) =>
            Connections.FirstOrDefault(c => c.FromNode == nodeId);

        public void RemoveNode(string id)
        {
            Nodes.RemoveAll(n => n.Id == id);
            Connections.RemoveAll(c => c.FromNode == id || c.ToNode == id);
        }
    }
}