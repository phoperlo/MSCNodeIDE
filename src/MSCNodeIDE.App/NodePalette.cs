using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MSCNodeIDE.Core;

namespace MSCNodeIDE.App
{
    /// <summary>Палитра узлов: дерево категорий + поиск, перетаскивание на холст.</summary>
    public sealed class NodePalette : UserControl
    {
        private readonly TreeView _tree;
        private readonly TextBox _search;
        private NodeLibrary _library;

        public event EventHandler<NodeDefinition> NodeChosen;

        public NodePalette()
        {
            Dock = DockStyle.Fill;

            _search = new TextBox
            {
                Dock = DockStyle.Top,
                PlaceholderText = "Поиск узлов...",
                Font = new Font("Segoe UI", 9f)
            };
            _search.TextChanged += (s, e) => Rebuild();

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                FullRowSelect = true,
                Font = new Font("Segoe UI", 9f)
            };
            _tree.NodeMouseDoubleClick += OnDoubleClick;
            _tree.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) ActivateSelected();
            };

            var searchPanel = new Panel { Dock = DockStyle.Top, Height = 34 };
            searchPanel.Paint += (s, e) =>
                e.Graphics.FillRectangle(Brushes.White, searchPanel.ClientRectangle);
            searchPanel.Controls.Add(_search);
            _search.Dock = DockStyle.Fill;

            Controls.Add(_tree);
            Controls.Add(searchPanel);
        }

        public void SetLibrary(NodeLibrary lib)
        {
            _library = lib;
            Rebuild();
        }

        private void OnDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node?.Tag is string id && _library?.Find(id) != null)
                NodeChosen?.Invoke(this, _library.Find(id));
        }

        private void ActivateSelected()
        {
            if (_tree.SelectedNode?.Tag is string id && _library?.Find(id) != null)
                NodeChosen?.Invoke(this, _library.Find(id));
        }

        private void Rebuild()
        {
            if (_library == null) return;
            var q = _search.Text?.Trim() ?? "";
            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            if (!string.IsNullOrEmpty(q))
            {
                var results = _library.Search(q).Take(200).ToList();
                if (results.Count == 0)
                {
                    _tree.Nodes.Add("Ничего не найдено");
                }
                else
                {
                    foreach (var def in results)
                        _tree.Nodes.Add(Node(def.Title, def.Id, def.Category));
                }
            }
            else
            {
                foreach (var cat in _library.Categories)
                {
                    var defs = _library.InCategory(cat).ToList();
                    if (defs.Count == 0) continue;
                    var node = new TreeNode(cat);
                    foreach (var def in defs.Take(300))
                        node.Nodes.Add(Node(def.Title, def.Id, def.Description));
                    _tree.Nodes.Add(node);
                }
            }
            _tree.ExpandAll();
            _tree.EndUpdate();
        }

        private static TreeNode Node(string text, string id, string tooltip)
            => new TreeNode(text) { Tag = id, ToolTipText = tooltip };
    }
}