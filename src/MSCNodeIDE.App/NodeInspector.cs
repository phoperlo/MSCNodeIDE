using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MSCNodeIDE.Core;

namespace MSCNodeIDE.App
{
    /// <summary>Инспектор: свойства выбранного узла — literal-значения, опции, enum.</summary>
    public sealed class NodeInspector : UserControl
    {
        private readonly FlowLayoutPanel _panel;
        private readonly Label _title;
        private readonly Label _hint;
        private NodeLibrary _library;
        private NodeInstance _node;
        private GraphCanvas _canvas;

        public event EventHandler Changed;

        public NodeInspector()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            BackColor = Color.FromArgb(240, 242, 245);

            _title = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 46,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 34, 40),
                Padding = new Padding(8, 8, 8, 4),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _hint = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(240, 0),
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(110, 116, 128),
                Padding = new Padding(8, 0, 8, 6)
            };

            _panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(6, 0, 6, 6)
            };

            Controls.Add(_panel);
            Controls.Add(_hint);
            Controls.Add(_title);
        }

        public void Bind(GraphCanvas canvas, NodeLibrary library)
        {
            _canvas = canvas;
            _library = library;
            Show(null);
        }

        public void Show(NodeInstance node)
        {
            _node = node;
            _panel.SuspendLayout();
            _panel.Controls.Clear();

            if (node == null || _library == null)
            {
                _title.Text = "Инспектор";
                _hint.Text = "Выберите узел на холсте, чтобы задать его параметры.\n\n" +
                             "Цветные кружки — порты данных.\nБелые — порты выполнения.";
                _panel.ResumeLayout(true);
                return;
            }

            var def = _library.Find(node.DefinitionId);
            if (def == null)
            {
                _title.Text = "Неизвестный узел";
                _hint.Text = node.DefinitionId;
                _panel.ResumeLayout(true);
                return;
            }

            _title.Text = def.Title;
            _hint.Text = def.Description ?? "";

            foreach (var port in def.Inputs.Where(p => p.IsLiteral))
                AddPortEditor(def, node, port);

            if (!def.Inputs.Any(p => p.IsLiteral))
            {
                _panel.Controls.Add(new Label
                {
                    Text = "У этого узла нет настраиваемых значений.",
                    AutoSize = true,
                    MaximumSize = new Size(220, 0),
                    ForeColor = Color.Gray,
                    Font = new Font("Segoe UI", 8.5f),
                    Margin = new Padding(2, 4, 2, 4)
                });
            }

            _panel.ResumeLayout(true);
        }

        private void AddPortEditor(NodeDefinition def, NodeInstance node, Port port)
        {
            var options = Def.OptionsFor(def.Id, port.Id);

            var label = new Label
            {
                Text = port.Name,
                AutoSize = false,
                Width = 220,
                Height = 18,
                Font = new Font("Segoe UI", 8.6f),
                ForeColor = Color.FromArgb(50, 56, 64),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(2, 6, 2, 0)
            };
            _panel.Controls.Add(label);

            string current = node.Get("in." + port.Id)
                             ?? Convert.ToString(port.LiteralValue, CultureInfo.InvariantCulture)
                             ?? "";

            Control editor;

            if (options != null && options.Length > 0)
            {
                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = 220,
                    SelectedItem = options.Contains(current) ? current : options[0]
                };
                combo.Items.AddRange(options);
                combo.SelectedIndexChanged += (s, e) =>
                {
                    node.Set("in." + port.Id, combo.SelectedItem?.ToString());
                    Raise();
                };
                editor = combo;
            }
            else
            {
                switch (port.Kind)
                {
                    case PortKind.Bool:
                    {
                        var chk = new CheckBox
                        {
                            Checked = current == "True" || current == "true" || current == "1",
                            Width = 220,
                            Height = 20
                        };
                        chk.CheckedChanged += (s, e) =>
                        {
                            node.Set("in." + port.Id, chk.Checked ? "True" : "False");
                            Raise();
                        };
                        editor = chk;
                        break;
                    }
                    case PortKind.Vector2:
                    case PortKind.Vector3:
                    {
                        var box = new TextBox
                        {
                            Text = current,
                            Width = 220,
                            Font = new Font("Consolas", 9f)
                        };
                        box.TextChanged += (s, e) => { node.Set("in." + port.Id, box.Text); Raise(); };
                        editor = box;
                        break;
                    }
                    case PortKind.Color:
                    {
                        var btn = new Button
                        {
                            Text = current,
                            Width = 220,
                            Height = 24,
                            BackColor = ParseColor(current)
                        };
                        btn.Click += (s, e) =>
                        {
                            using var dlg = new ColorDialog { Color = ParseColor(current) };
                            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                            var txt = $"{dlg.Color.R / 255f:0.###},{dlg.Color.G / 255f:0.###}," +
                                      $"{dlg.Color.B / 255f:0.###},{dlg.Color.A / 255f:0.###}";
                            node.Set("in." + port.Id, txt);
                            btn.Text = txt;
                            btn.BackColor = dlg.Color;
                            Raise();
                        };
                        editor = btn;
                        break;
                    }
                    default:
                    {
                        var multi = port.Kind == PortKind.String || port.Kind == PortKind.Object;
                        var box = new TextBox
                        {
                            Text = current,
                            Width = 220,
                            Multiline = multi,
                            Height = multi ? 48 : 22,
                            ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None,
                            Font = new Font("Consolas", 9f)
                        };
                        box.TextChanged += (s, e) => { node.Set("in." + port.Id, box.Text); Raise(); };
                        editor = box;
                        break;
                    }
                }
            }

            editor.Margin = new Padding(2, 2, 2, 4);
            _panel.Controls.Add(editor);
        }

        private void Raise()
        {
            Changed?.Invoke(this, EventArgs.Empty);
            _canvas?.Invalidate();
        }

        private static Color ParseColor(string csv)
        {
            try
            {
                var parts = csv.Split(',');
                if (parts.Length < 3) return Color.White;
                float Ch(int i) => i < parts.Length ? float.Parse(parts[i].Trim(),
                    CultureInfo.InvariantCulture) : 0f;
                return Color.FromArgb(
                    (int)(Ch(3) * 255),
                    (int)(Ch(0) * 255),
                    (int)(Ch(1) * 255),
                    (int)(Ch(2) * 255));
            }
            catch { return Color.White; }
        }
    }
}