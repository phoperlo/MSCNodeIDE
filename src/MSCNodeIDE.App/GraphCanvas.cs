using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using MSCNodeIDE.Core;

namespace MSCNodeIDE.App
{
    /// <summary>
    /// Холст графа узлов: перетаскивание, панорамирование, масштаб,
    /// рисование связей, выделение, контекстное меню добавления узлов.
    /// </summary>
    public sealed class GraphCanvas : Control
    {
        // ---------- состояние ----------
        private NodeGraph _graph;
        private NodeLibrary _library;
        private double _zoom = 1.0;
        private double _panX, _panY;

        private string _draggingNodeId;
        private string _draggingLinkFromNode;
        private string _draggingLinkFromPort;
        private bool _panning;
        private Point _lastMouse;
        private readonly HashSet<string> _selected = new HashSet<string>();
        private NodeInstance _hoverPortOwner;
        private string _hoverPortId;

        // ---------- палитра ----------
        private static readonly Color ColBg = Color.FromArgb(28, 30, 34);
        private static readonly Color ColGrid = Color.FromArgb(44, 47, 53);
        private static readonly Color ColGridMajor = Color.FromArgb(58, 62, 70);
        private static readonly Color ColNode = Color.FromArgb(48, 52, 60);
        private static readonly Color ColNodeSel = Color.FromArgb(62, 84, 110);
        private static readonly Color ColTitle = Color.FromArgb(66, 72, 84);
        private static readonly Color ColTitleEntry = Color.FromArgb(48, 96, 74);
        private static readonly Color ColBorder = Color.FromArgb(20, 22, 26);
        private static readonly Color ColText = Color.FromArgb(226, 230, 236);
        private static readonly Color ColTextDim = Color.FromArgb(150, 158, 170);
        private static readonly Color ColExec = Color.FromArgb(232, 236, 242);
        private static readonly Color ColFloat = Color.FromArgb(126, 196, 122);
        private static readonly Color ColInt = Color.FromArgb(122, 168, 232);
        private static readonly Color ColBool = Color.FromArgb(232, 168, 96);
        private static readonly Color ColString = Color.FromArgb(226, 122, 196);
        private static readonly Color ColVector = Color.FromArgb(150, 200, 230);
        private static readonly Color ColObject = Color.FromArgb(200, 170, 120);
        private static readonly Color ColLink = Color.FromArgb(120, 160, 200);
        private static readonly Color ColLinkActive = Color.FromArgb(240, 190, 90);

        private const int NodeWidth = 190;
        private const int HeaderHeight = 26;
        private const int PortRowHeight = 20;
        private const int PortSpacing = 6;

        public event EventHandler GraphChanged;
        public event EventHandler SelectionChanged;
        public event EventHandler<NodeInstance> NodeDoubleClicked;
        public event EventHandler StatusText;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public NodeGraph Graph { get => _graph; set { _graph = value; Invalidate(); } }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public NodeLibrary Library { get => _library; set { _library = value; Invalidate(); } }

        [Browsable(false)]
        public IReadOnlyCollection<string> SelectedNodes => _selected;

        public GraphCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable, true);
            BackColor = ColBg;
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ColBg);
            if (_graph == null || _library == null) return;

            DrawGrid(g);

            var state = g.Save();
            g.TranslateTransform((float)_panX, (float)_panY);
            g.ScaleTransform((float)_zoom, (float)_zoom);

            // связи — под узлами
            DrawLinks(g, dragging: true);
            foreach (var node in _graph.Nodes)
                DrawNode(g, node);

            if (_draggingLinkFromNode != null) DrawPendingLink(g);
            g.Restore(state);
        }

        // ---------------- геометрия ----------------

        private RectangleF NodeRect(NodeInstance n)
        {
            var def = _library.Find(n.DefinitionId);
            var inputs = def?.Inputs.Count(p => p.Kind != PortKind.Execution) ?? 0;
            var outputs = def?.Outputs.Count(p => p.Kind != PortKind.Execution) ?? 0;
            var rows = Math.Max(inputs, outputs);
            var hasExecIn = def?.Inputs.Any(p => p.Kind == PortKind.Execution) == true;
            var hasExecOut = def?.Outputs.Any(p => p.Kind == PortKind.Execution) == true;

            var bodyTop = HeaderHeight + (hasExecIn ? PortRowHeight : 0);
            var height = bodyTop + (rows > 0 ? rows * (PortRowHeight + PortSpacing) + 8 : 14);
            if (hasExecOut) height += PortRowHeight;

            return new RectangleF((float)n.X, (float)n.Y, NodeWidth, height);
        }

        private PointF PortCenter(NodeInstance n, Port p)
        {
            var rect = NodeRect(n);
            var def = _library.Find(n.DefinitionId);
            var hasExecIn = def?.Inputs.Any(x => x.Kind == PortKind.Execution) == true;

            if (p.Kind == PortKind.Execution)
            {
                if (p.IsInput)
                    return new PointF(rect.Left, rect.Top + HeaderHeight / 2f);
                return new PointF(rect.Left + NodeWidth, rect.Top + HeaderHeight / 2f);
            }

            var list = (p.IsInput ? def.Inputs : def.Outputs)
                .Where(x => x.Kind != PortKind.Execution).ToList();
            var idx = list.FindIndex(x => x.Id == p.Id);

            if (p.IsInput)
            {
                var y = rect.Top + HeaderHeight + (hasExecIn ? PortRowHeight : 0)
                        + idx * (PortRowHeight + PortSpacing) + PortRowHeight / 2f;
                return new PointF(rect.Left, y);
            }
            else
            {
                var outputs = def.Outputs.Where(x => x.Kind != PortKind.Execution).ToList();
                var y = rect.Top + HeaderHeight + PortRowHeight / 2f
                        + idx * (PortRowHeight + PortSpacing) + PortRowHeight / 2f;
                return new PointF(rect.Left + NodeWidth, y);
            }
        }

        private PointF ScreenToGraph(Point p)
            => new PointF((float)((p.X - _panX) / _zoom), (float)((p.Y - _panY) / _zoom));

        private RectangleF ScreenRect(NodeInstance n) => NodeRect(n);

        // ---------------- отрисовка ----------------

        private void DrawGrid(Graphics g)
        {
            float step = 24 * (float)_zoom;
            if (step < 6) return;
            var ox = (float)(_panX % step);
            var oy = (float)(_panY % step);

            using var fine = new Pen(ColGrid, 1f);
            for (float x = ox; x < Width; x += step)
                g.DrawLine(fine, x, 0, x, Height);

            float major = step * 4;
            var mx = (float)(_panX % major);
            var my = (float)(_panY % major);
            using var bold = new Pen(ColGridMajor, 1f);
            for (float x = mx; x < Width; x += major)
                g.DrawLine(bold, x, 0, x, Height);
            for (float y = my; y < Height; y += major)
                g.DrawLine(bold, 0, y, Width, y);
        }

        private void DrawNode(Graphics g, NodeInstance n)
        {
            var def = _library.Find(n.DefinitionId);
            if (def == null) return;
            var rect = NodeRect(n);
            var selected = _selected.Contains(n.Id);
            var isEntry = n.DefinitionId.StartsWith("flow.on") || n.DefinitionId == "flow.update";

            using var body = new SolidBrush(selected ? ColNodeSel : ColNode);
            using var path = RoundedRect(rect, 6);
            g.FillPath(body, path);

            // заголовок
            var header = new RectangleF(rect.Left, rect.Top, rect.Width, HeaderHeight);
            using var hdrBrush = new SolidBrush(isEntry ? ColTitleEntry : ColTitle);
            using var hdrPath = RoundedTop(rect, 6, HeaderHeight);
            g.FillPath(hdrBrush, hdrPath);

            using var border = new Pen(selected ? Color.FromArgb(120, 180, 240) : ColBorder, selected ? 2f : 1f);
            g.DrawPath(border, path);

            using var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var textBrush = new SolidBrush(ColText);
            var title = def.Title;
            var maxChars = (int)((NodeWidth - 16) / 6.2);
            if (title.Length > maxChars) title = title.Substring(0, maxChars - 1) + "…";
            g.DrawString(title, titleFont, textBrush, rect.Left + 8, rect.Top + 5);

            // порты
            using var portFont = new Font("Segoe UI", 7.6f);
            using var dimBrush = new SolidBrush(ColTextDim);

            foreach (var p in def.Inputs)
            {
                var c = PortCenter(n, p);
                var col = PortColor(p.Kind);
                using var b = new SolidBrush(col);
                if (p.Kind == PortKind.Execution) g.FillEllipse(b, c.X - 5, c.Y - 5, 10, 10);
                else g.FillEllipse(b, c.X - 4, c.Y - 4, 8, 8);

                if (p.Kind != PortKind.Execution)
                {
                    var text = p.Name;
                    if (p.IsLiteral && n.Properties.TryGetValue("in." + p.Id, out var val))
                        text = Trim(val, 14);
                    g.DrawString(Trim(text, 20), portFont, dimBrush, c.X + 8, c.Y - 7);
                }
            }

            foreach (var p in def.Outputs)
            {
                var c = PortCenter(n, p);
                var col = PortColor(p.Kind);
                using var b = new SolidBrush(col);
                if (p.Kind == PortKind.Execution) g.FillEllipse(b, c.X - 5, c.Y - 5, 10, 10);
                else g.FillEllipse(b, c.X - 4, c.Y - 4, 8, 8);

                if (p.Kind != PortKind.Execution)
                {
                    var label = Trim(p.Name, 16);
                    var size = g.MeasureString(label, portFont);
                    g.DrawString(label, portFont, dimBrush, c.X - 8 - size.Width, c.Y - 7);
                }
            }

            // подсветка literal-портов, значение которых меняли
            foreach (var p in def.Inputs.Where(p => p.IsLiteral))
            {
                if (!n.Properties.ContainsKey("in." + p.Id)) continue;
                var c = PortCenter(n, p);
                using var b = new SolidBrush(Color.FromArgb(90, 220, 140));
                g.FillEllipse(b, c.X - 6, c.Y - 6, 12, 12);
            }
        }

        private void DrawLinks(Graphics g, bool dragging)
        {
            using var pen = new Pen(ColLink, 2f);
            foreach (var c in _graph.Connections)
            {
                var from = _graph.Find(c.FromNode);
                var to = _graph.Find(c.ToNode);
                if (from == null || to == null) continue;
                var defFrom = _library.Find(from.DefinitionId);
                var pFrom = defFrom?.OutputPort(c.FromPort);
                if (pFrom == null) continue;

                var a = PortCenter(from, pFrom);
                var b = PortCenter(to, _library.Find(to.DefinitionId)?.InputPort(c.ToPort));
                var isExec = pFrom.Kind == PortKind.Execution;
                using var p = new Pen(isExec ? ColExec : ColLink,
                                        isExec ? 2.4f : 1.8f);
                DrawBezier(g, p, a, b);
            }
        }

        private void DrawPendingLink(Graphics g)
        {
            var from = _graph.Find(_draggingLinkFromNode);
            if (from == null) return;
            var def = _library.Find(from.DefinitionId);
            var port = def?.OutputPort(_draggingLinkFromPort);
            if (port == null) return;

            var a = PortCenter(from, port);
            var b = new PointF((float)((MousePosition.X - _panX) / _zoom),
                               (float)((MousePosition.Y - _panY) / _zoom));
            using var pen = new Pen(ColLinkActive, 2f) { DashStyle = DashStyle.Dash };
            DrawBezier(g, pen, a, b);
        }

        private static void DrawBezier(Graphics g, Pen pen, PointF a, PointF b)
        {
            float dx = Math.Max(40f, Math.Abs(b.X - a.X) * 0.5f);
            g.DrawCurve(pen, a,
                new PointF(a.X + dx, a.Y),
                new PointF(b.X - dx, b.Y),
                b);
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static GraphicsPath RoundedTop(RectangleF r, float radius, float headerH)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddLine(r.Right, r.Top + headerH, r.Left, r.Top + headerH);
            p.CloseFigure();
            return p;
        }

        private static Color PortColor(PortKind k)
        {
            switch (k)
            {
                case PortKind.Execution: return ColExec;
                case PortKind.Float: return ColFloat;
                case PortKind.Int: return ColInt;
                case PortKind.Bool: return ColBool;
                case PortKind.String: return ColString;
                case PortKind.Vector2:
                case PortKind.Vector3:
                case PortKind.Color: return ColVector;
                case PortKind.Void: return Color.Gray;
                default: return ColObject;
            }
        }

        private static string Trim(string s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length > max ? s.Substring(0, max - 1) + "…" : s);

        // ---------------- взаимодействие ----------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var gp = ScreenToGraph(e.Location);

            if (e.Button == MouseButtons.Middle ||
                (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Shift) == Keys.Shift))
            {
                _panning = true;
                _lastMouse = e.Location;
                Cursor = Cursors.SizeAll;
                return;
            }

            var port = HitPort(gp, out var ownerNode, out var portId, out var isInput);
            if (port != null && !isInput)
            {
                _draggingLinkFromNode = ownerNode.Id;
                _draggingLinkFromPort = portId;
                Cursor = Cursors.Cross;
                return;
            }
            if (port != null && isInput)
            {
                // тянем существующую связь от этого входа
                var existing = _graph.Incoming(ownerNode.Id, portId).FirstOrDefault();
                if (existing != null)
                {
                    _graph.Connections.Remove(existing);
                    _draggingLinkFromNode = existing.FromNode;
                    _draggingLinkFromPort = existing.FromPort;
                    RaiseChanged();
                    Cursor = Cursors.Cross;
                }
                return;
            }

            var node = HitNode(gp);
            if (node != null)
            {
                bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
                if (e.Button == MouseButtons.Right) { _selected.Clear(); _selected.Add(node.Id); }
                else if (!shift) _selected.Clear();
                if (shift && _selected.Contains(node.Id)) _selected.Remove(node.Id);
                else _selected.Add(node.Id);

                _draggingNodeId = node.Id;
                _lastMouse = e.Location;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                _selected.Clear();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                ShowCanvasMenu(e.Location);
            }
            else
            {
                _selected.Clear();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_panning)
            {
                _panX += e.X - _lastMouse.X;
                _panY += e.Y - _lastMouse.Y;
                _lastMouse = e.Location;
                Invalidate();
                return;
            }

            if (_draggingNodeId != null)
            {
                var node = _graph.Find(_draggingNodeId);
                if (node != null)
                {
                    node.X += (e.X - _lastMouse.X) / _zoom;
                    node.Y += (e.Y - _lastMouse.Y) / _zoom;
                    _lastMouse = e.Location;
                    Invalidate();
                }
                return;
            }

            if (_draggingLinkFromNode != null) { Invalidate(); return; }

            // подсветка порта под курсором
            var gp = ScreenToGraph(e.Location);
            var port = HitPort(gp, out var owner, out var portId, out _);
            var newOwner = port != null ? owner?.Id : null;
            if (newOwner != _hoverPortOwner?.Id || portId != _hoverPortId)
            {
                _hoverPortOwner = newOwner != null ? owner : null;
                _hoverPortId = portId;
                Cursor = port != null ? Cursors.Cross : Cursors.Default;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_panning) { _panning = false; Cursor = Cursors.Default; }

            if (_draggingNodeId != null)
            {
                _draggingNodeId = null;
                RaiseChanged();
            }

            if (_draggingLinkFromNode != null)
            {
                var gp = ScreenToGraph(e.Location);
                var port = HitPort(gp, out var target, out var portId, out var isInput);
                if (port != null && isInput && target.Id != _draggingLinkFromNode)
                    Connect(_draggingLinkFromNode, _draggingLinkFromPort, target.Id, portId);
                else if (port == null)
                {
                    // отпустили в пустоте — предложить создать value-узел
                    var ctx = HitNode(gp);
                    if (ctx != null) TryAutoValueNode(gp, _draggingLinkFromNode,
                        _draggingLinkFromPort, ctx.Id);
                }
                _draggingLinkFromNode = null;
                _draggingLinkFromPort = null;
                Cursor = Cursors.Default;
                RaiseChanged();
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var before = ScreenToGraph(e.Location);
            var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
            _zoom = Math.Clamp(_zoom * factor, 0.3, 2.5);
            var after = ScreenToGraph(e.Location);
            _panX += (after.X - before.X) * (float)_zoom;
            _panY += (after.Y - before.Y) * (float)_zoom;
            StatusText?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            var gp = ScreenToGraph(e.Location);
            var node = HitNode(gp);
            if (node != null) NodeDoubleClicked?.Invoke(this, node);
        }

        private void Connect(string fromNode, string fromPort, string toNode, string toPort)
        {
            var fromDef = _library.Find(_graph.Find(fromNode)?.DefinitionId);
            var toDef = _library.Find(_graph.Find(toNode)?.DefinitionId);
            var srcPort = fromDef?.OutputPort(fromPort);
            var dstPort = toDef?.InputPort(toPort);
            if (srcPort == null || dstPort == null) return;

            if (!PortKindUtil.Assignable(srcPort.Kind, dstPort.Kind))
            {
                StatusText?.Invoke(this, EventArgs.Empty);
                MessageBox.Show($"Несовместимые типы портов:\n{srcPort.Name} ({srcPort.Kind}) → " +
                                $"{dstPort.Name} ({dstPort.Kind})", "Неверное подключение",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // один вход — одна связь
            _graph.Connections.RemoveAll(c => c.ToNode == toNode && c.ToPort == toPort);
            _graph.Connections.Add(new Connection
            {
                FromNode = fromNode,
                FromPort = fromPort,
                ToNode = toNode,
                ToPort = toPort
            });
        }

        /// <summary>Автозаполнение: если отпустили выход над узлом, создаём подходящий узел.</summary>
        private void TryAutoValueNode(PointF gp, string fromNode, string fromPort, string targetNodeId)
        {
            var fromDef = _library.Find(_graph.Find(fromNode)?.DefinitionId);
            var srcPort = fromDef?.OutputPort(fromPort);
            var targetDef = _library.Find(_graph.Find(targetNodeId)?.DefinitionId);
            if (srcPort == null || targetDef == null) return;

            // ищем входной порт того же типа, который ещё свободен
            var free = targetDef.Inputs.FirstOrDefault(p =>
                p.Kind != PortKind.Execution &&
                !_graph.Incoming(targetNodeId, p.Id).Any() &&
                PortKindUtil.Assignable(srcPort.Kind, p.Kind));
            if (free == null) return;

            var node = AddNode(GiveId(), MakeValueNodeDef(srcPort.Kind), gp.X - 20, gp.Y - 60);
            Connect(fromNode, fromPort, node.Id, node.DefinitionId == "value.float"
                ? "out" : node.Id != null ? "out" : "out");
            Connect(node.Id, "out", targetNodeId, free.Id);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public static string GiveId() => Guid.NewGuid().ToString("N")[..8];

        public static NodeDefinition MakeValueNodeDef(PortKind kind)
        {
            var lib = NodeLibrary.CreateDefault();
            var id = kind switch
            {
                PortKind.Float => "value.float",
                PortKind.Int => "value.int",
                PortKind.Bool => "value.bool",
                PortKind.String => "value.string",
                PortKind.Vector3 => "value.v3",
                PortKind.Vector2 => "value.v2",
                PortKind.Color => "value.color",
                _ => null
            };
            return id != null ? lib.Find(id) : null;
        }

        // ---------------- поиск по координатам ----------------

        private NodeInstance HitNode(PointF gp)
        {
            // сверху вниз по списку: последние нарисованные сверху
            for (int i = _graph.Nodes.Count - 1; i >= 0; i--)
            {
                var n = _graph.Nodes[i];
                if (NodeRect(n).Contains(gp)) return n;
            }
            return null;
        }

        private Port HitPort(PointF gp, out NodeInstance owner, out string portId, out bool isInput)
        {
            owner = null; portId = null; isInput = false;
            const float tolerance = 7f;
            for (int i = _graph.Nodes.Count - 1; i >= 0; i--)
            {
                var n = _graph.Nodes[i];
                var def = _library.Find(n.DefinitionId);
                if (def == null) continue;
                foreach (var p in def.Ports)
                {
                    var c = PortCenter(n, p);
                    if (Math.Abs(c.X - gp.X) > tolerance) continue;
                    if (Math.Abs(c.Y - gp.Y) > tolerance) continue;
                    owner = n; portId = p.Id; isInput = p.IsInput;
                    return p;
                }
            }
            return null;
        }

        // ---------------- операции ----------------

        public NodeInstance AddNode(string id, NodeDefinition def, double x, double y)
        {
            var node = new NodeInstance
            {
                Id = id ?? GiveId(),
                DefinitionId = def.Id,
                X = Math.Round(x),
                Y = Math.Round(y)
            };
            // предзаполняем literal-порты значениями по умолчанию
            foreach (var p in def.Inputs.Where(p => p.IsLiteral && p.LiteralValue != null))
                node.Set("in." + p.Id, Convert.ToString(p.LiteralValue,
                    System.Globalization.CultureInfo.InvariantCulture));
            _graph.Nodes.Add(node);
            _selected.Clear();
            _selected.Add(node.Id);
            return node;
        }

        public void DeleteSelected()
        {
            if (_selected.Count == 0) return;
            foreach (var id in _selected.ToList())
                _graph.RemoveNode(id);
            _selected.Clear();
            RaiseChanged();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void CopySelected()
        {
            _clipboard = _graph.Nodes.Where(n => _selected.Contains(n.Id))
                .Select(n => new NodeInstance
                {
                    Id = GiveId(),
                    DefinitionId = n.DefinitionId,
                    X = n.X + 24,
                    Y = n.Y + 24,
                    Properties = new Dictionary<string, string>(n.Properties)
                }).ToList();
            if (_clipboard.Count > 0) StatusText?.Invoke(this, EventArgs.Empty);
        }

        private List<NodeInstance> _clipboard;

        public void Paste()
        {
            if (_clipboard == null) return;
            _selected.Clear();
            foreach (var n in _clipboard)
            {
                var copy = new NodeInstance
                {
                    Id = GiveId(),
                    DefinitionId = n.DefinitionId,
                    X = n.X,
                    Y = n.Y,
                    Properties = new Dictionary<string, string>(n.Properties)
                };
                _graph.Nodes.Add(copy);
                _selected.Add(copy.Id);
            }
            RaiseChanged();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void SelectAll()
        {
            _selected.Clear();
            foreach (var n in _graph.Nodes) _selected.Add(n.Id);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void FrameAll()
        {
            if (_graph.Nodes.Count == 0) { _zoom = 1; _panX = 0; _panY = 0; Invalidate(); return; }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var n in _graph.Nodes)
            {
                var r = NodeRect(n);
                minX = Math.Min(minX, r.Left); minY = Math.Min(minY, r.Top);
                maxX = Math.Max(maxX, r.Right); maxY = Math.Max(maxY, r.Bottom);
            }

            var w = Math.Max(1, maxX - minX);
            var h = Math.Max(1, maxY - minY);
            var zx = Width / (w + 120);
            var zy = Height / (h + 120);
            _zoom = Math.Clamp(Math.Min(zx, zy), 0.3, 1.6);
            _panX = (float)(Width / 2 - (minX + w / 2) * _zoom);
            _panY = (float)(Height / 2 - (minY + h / 2) * _zoom);
            Invalidate();
        }

        private void RaiseChanged() => GraphChanged?.Invoke(this, EventArgs.Empty);

        // ---------------- меню ----------------

        private void ShowCanvasMenu(Point location)
        {
            var menu = new ContextMenuStrip();
            foreach (var cat in _library.Categories)
            {
                var items = _library.InCategory(cat).Take(40).ToList();
                if (items.Count == 0) continue;

                var sub = new ToolStripMenuItem(cat);
                foreach (var def in items)
                {
                    var item = new ToolStripMenuItem(def.Title) { Tag = def.Id };
                    if (!string.IsNullOrEmpty(def.Description)) item.ToolTipText = def.Description;
                    item.Click += (s, e) =>
                    {
                        var gp = ScreenToGraph(location);
                        var def2 = _library.Find((string)((ToolStripMenuItem)s).Tag);
                        if (def2 == null) return;
                        AddNode(GiveId(), def2, gp.X - 60, gp.Y - 20);
                        RaiseChanged();
                        SelectionChanged?.Invoke(this, EventArgs.Empty);
                        Invalidate();
                    };
                    sub.DropDownItems.Add(item);
                }
                menu.Items.Add(sub);
            }
            menu.Show(this, location);
        }

        protected override bool IsInputKey(Keys keyData) => true;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Delete: DeleteSelected(); e.Handled = true; break;
                case Keys.F: FrameAll(); e.Handled = true; break;
            }
        }

        public double Zoom => _zoom;
    }
}