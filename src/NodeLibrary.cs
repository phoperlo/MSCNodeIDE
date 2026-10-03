using System;
using System.Collections.Generic;
using System.Linq;

namespace MSCNodeIDE.Core
{
    /// <summary>Реестр типов узлов. Загружает встроенные категории + рефлексию по DLL игры.</summary>
    public sealed class NodeLibrary
    {
        private readonly Dictionary<string, NodeDefinition> _defs = new Dictionary<string, NodeDefinition>();
        private readonly List<string> _categories = new List<string>();

        public IEnumerable<NodeDefinition> All => _defs.Values;
        public IEnumerable<string> Categories => _categories;

        public NodeDefinition Find(string id) =>
            id != null && _defs.TryGetValue(id, out var d) ? d : null;

        public void Register(NodeDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) return;
            _defs[def.Id] = def;
            if (!string.IsNullOrEmpty(def.Category) && !_categories.Contains(def.Category))
                _categories.Add(def.Category);
        }

        public IEnumerable<NodeDefinition> InCategory(string cat) =>
            _defs.Values.Where(d => d.Category == cat)
                        .OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase);

        public IEnumerable<NodeDefinition> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return All.OrderBy(d => d.Title);
            var q = query.Trim().ToLowerInvariant();
            return All.Where(d =>
                (d.Title ?? "").ToLowerInvariant().Contains(q) ||
                (d.Id ?? "").ToLowerInvariant().Contains(q) ||
                (d.Description ?? "").ToLowerInvariant().Contains(q) ||
                (d.Category ?? "").ToLowerInvariant().Contains(q))
                .OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase);
        }

        public static NodeLibrary CreateDefault(ApiCatalog api = null)
        {
            var lib = new NodeLibrary();
            FlowNodes.Register(lib);
            ValueNodes.Register(lib);
            MathNodes.Register(lib);
            LogicNodes.Register(lib);
            StringNodes.Register(lib);
            UnityNodes.Register(lib);
            MscLoaderNodes.Register(lib);
            PlayMakerNodes.Register(lib);
            GlobalFsmNodes.Register(lib);
            ImguiNodes.Register(lib);
            SaveNodes.Register(lib);
            if (api != null) ApiNodes.Register(lib, api);
            return lib;
        }
    }
}