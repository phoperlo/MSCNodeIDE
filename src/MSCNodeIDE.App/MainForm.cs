using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MSCNodeIDE.Core;

namespace MSCNodeIDE.App
{
    public sealed class MainForm : Form
    {
        // ---------- состояние ----------
        private ModProjectFile _project;
        private NodeLibrary _library;
        private GraphCanvas _canvas;
        private NodePalette _palette;
        private NodeInspector _inspector;
        private TextBox _codeView;
        private TextBox _logView;
        private ToolStripStatusLabel _status;
        private ToolStripStatusLabel _gameStatus;
        private ApiCatalog _api;

        private const string CatFlow = "Поток выполнения";

        public MainForm()
        {
            Text = "MSCNodeIDE — визуальный скриптинг модов My Summer Car";
            Width = 1500;
            Height = 940;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            BuildUi();
            StartEmptyProject();
        }

        /// <summary>
        /// Пустой проект в памяти: приложение должно открываться сразу готовым к работе,
        /// без модальных диалогов. Папку создаст пользователь через "Сохранить как...".
        /// </summary>
        private void StartEmptyProject()
        {
            var gameDir = ProjectManager.FindGameDirectory() ?? "";
            const string modName = "MyMod";
            const string modVersion = "1.0.0";

            _project = new ModProjectFile
            {
                RootPath = null,
                Settings = new ModProjectSettings
                {
                    Name = modName,
                    Author = Environment.UserName,
                    Version = modVersion,
                    Description = "Мод создан в MSCNodeIDE",
                    GameDirectory = gameDir
                },
                Graphs = { ProjectSerializer.CreateStarterGraph(modName, modVersion) }
            };

            _library = NodeLibrary.CreateDefault();
            _palette.SetLibrary(_library);
            _inspector.Bind(_canvas, _library);

            var graph = _project.Graphs[0];
            _canvas.Graph = graph;
            _canvas.Library = _library;
            _canvas.FrameAll();

            UpdateGameStatus();
            Log("MSCNodeIDE готов.");
            Log("Игра найдена автоматически: " + (gameDir.Length > 0 ? gameDir : "нет"));
            Log("Перед сборкой выбери папку проекта — программа спросит её сама.");
            SetStatus("Новый проект (не сохранён)");
        }

        // ---------------- интерфейс ----------------

        private void BuildUi()
        {
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("Проект");
            file.DropDownItems.Add("Новый проект...", null, (s, e) => NewProject());
            file.DropDownItems.Add("Открыть...", null, (s, e) => OpenProject());
            file.DropDownItems.Add("Сохранить", null, (s, e) => SaveProject());
            file.DropDownItems.Add("Сохранить как...", null, (s, e) => SaveProjectAs());
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("Настройки проекта...", null, (s, e) => EditSettings());
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("Выход", null, (s, e) => Close());

            var build = new ToolStripMenuItem("Сборка");
            build.DropDownItems.Add("Сгенерировать код", null, (s, e) => GenerateCode(showPanel: true));
            build.DropDownItems.Add("Собрать мод (DLL)", null, (s, e) => BuildMod());
            build.DropDownItems.Add("Собрать и установить в Mods", null, (s, e) => BuildMod(copyToMods: true));

            var help = new ToolStripMenuItem("Справка");
            help.DropDownItems.Add("Найти папку игры", null, (s, e) => FindGame());
            help.DropDownItems.Add("Сканировать API игры", null, (s, e) => ScanApi());
            help.DropDownItems.Add("Открыть папку проекта", null, (s, e) => OpenFolder(_project.RootPath));
            help.DropDownItems.Add("О программе", null, (s, e) =>
                MessageBox.Show(this,
                    "MSCNodeIDE\n\nВизуальный конструктор C#-модов для My Summer Car (MSCLoader).\n" +
                    "Граф нод → чистый C# → рабочая DLL.",
                    "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information));

            menu.Items.AddRange(new ToolStripItem[] { file, build, help });
            MainMenuStrip = menu;

            // ---- центральные панели ----
            _palette = new NodePalette { Dock = DockStyle.Left, Width = 250 };
            _palette.NodeChosen += (s, def) =>
            {
                _canvas.AddNode(GraphCanvas.GiveId(), def, _canvas.Width * 0.4, _canvas.Height * 0.4);
                OnGraphEdited();
            };

            _inspector = new NodeInspector { Dock = DockStyle.Right, Width = 264 };

            _canvas = new GraphCanvas { Dock = DockStyle.Fill };
            _canvas.SelectionChanged += (s, e) => RefreshInspector();
            _canvas.NodeDoubleClicked += (s, node) => _inspector.Show(node);
            _canvas.GraphChanged += (s, e) => { OnGraphEdited(); };

            var codePanel = new Panel { Dock = DockStyle.Right, Width = 430, Visible = false };
            _codeView = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9.5f),
                BackColor = Color.FromArgb(250, 250, 252)
            };
            codePanel.Controls.Add(_codeView);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 520
            };
            split.Panel1.Controls.Add(_canvas);
            split.Panel1.Controls.Add(codePanel);
            codePanel.BringToFront();

            var logPanel = new Panel { Dock = DockStyle.Bottom, Height = 150 };
            _logView = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 8.8f),
                BackColor = Color.FromArgb(24, 26, 30),
                ForeColor = Color.FromArgb(200, 210, 220)
            };
            logPanel.Controls.Add(_logView);
            split.Panel2.Controls.Add(logPanel);

            var body = new Panel { Dock = DockStyle.Fill };
            body.Controls.Add(split);
            body.Controls.Add(_inspector);
            body.Controls.Add(_palette);

            // ---- панель инструментов ----
            var tool = new ToolStrip();
            tool.GripStyle = ToolStripGripStyle.Hidden;
            tool.Font = new Font("Segoe UI", 9f);

            tool.Items.Add(Btn("Новый", () => NewProject()));
            tool.Items.Add(Btn("Открыть", () => OpenProject()));
            tool.Items.Add(Btn("Сохранить", () => SaveProject()));
            tool.Items.Add(new ToolStripSeparator());
            tool.Items.Add(Btn("Сгенерировать код", () => GenerateCode(showPanel: true)));
            tool.Items.Add(Btn("Собрать", () => BuildMod()));
            tool.Items.Add(Btn("Собрать + в Mods", () => BuildMod(copyToMods: true)));
            tool.Items.Add(new ToolStripSeparator());
            tool.Items.Add(Btn("Показать код", null, () =>
            {
                codePanel.Visible = !codePanel.Visible;
                if (codePanel.Visible) ShowCodeOnly();
            }));
            tool.Items.Add(Btn("Вписать всё", () => _canvas.FrameAll()));
            tool.Items.Add(Btn("Удалить", () => _canvas.DeleteSelected()));

            var status = new StatusStrip();
            _status = new ToolStripStatusLabel("Готово") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _gameStatus = new ToolStripStatusLabel("Игра: не найдена");
            status.Items.Add(_status);
            status.Items.Add(_gameStatus);

            Controls.Add(body);
            Controls.Add(tool);
            Controls.Add(status);
            Controls.Add(menu);
        }

        private static ToolStripButton Btn(string text, Action click, Action extra = null)
        {
            var b = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            b.Click += (s, e) => { click?.Invoke(); extra?.Invoke(); };
            return b;
        }

        // ---------------- проект ----------------

        private void NewProject()
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Папка для нового проекта мода",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var root = Path.Combine(dlg.SelectedPath, "MyMod");
            var name = PromptForName("MyMod", "Название мода (имя класса и DLL)");
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!IsValidIdentifier(name))
            {
                MessageBox.Show(this, "Имя должно начинаться с буквы и содержать только буквы, цифры и _",
                    "Некорректное имя", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _project = new ModProjectFile
            {
                RootPath = root,
                Settings = new ModProjectSettings
                {
                    Name = name,
                    Author = Environment.UserName,
                    Version = "1.0.0",
                    Description = "Мод создан в MSCNodeIDE",
                    GameDirectory = ProjectManager.FindGameDirectory() ?? ""
                },
                Graphs = { ProjectSerializer.CreateStarterGraph(name, "1.0.0") }
            };

            ProjectManager.CreateProjectStructure(root, _project.Settings);
            SaveProject(silent: true);
            AttachGraph();

            Log($"Создан проект: {root}");
            Log($"Папка игры: {_project.Settings.GameDirectory}");
            UpdateGameStatus();
        }

        private void AttachGraph()
        {
            var graph = _project.Graphs.FirstOrDefault() ?? new NodeGraph { Name = "Main" };
            _canvas.Graph = graph;
            _canvas.Library = _library;
            _canvas.FrameAll();
            RefreshInspector();
        }

        private static bool IsValidIdentifier(string s)
            => !string.IsNullOrEmpty(s) && char.IsLetter(s[0]) &&
               s.All(ch => char.IsLetterOrDigit(ch) || ch == '_');

        private static string PromptForName(string def, string caption)
        {
            using var f = new Form
            {
                Width = 380, Height = 160, Text = caption,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false
            };
            var lbl = new Label { Text = caption, Left = 12, Top = 14, Width = 340, AutoSize = false, Height = 18 };
            var tb = new TextBox { Text = def, Left = 12, Top = 38, Width = 340 };
            var ok = new Button { Text = "OK", Left = 196, Top = 76, Width = 78, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Отмена", Left = 278, Top = 76, Width = 78 };
            cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
            f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : null;
        }

        private void OpenProject()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Проект MSCNodeIDE|project.json|All files (*.*)|*.*",
                Title = "Открыть проект мода"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            if (!ProjectSerializer.TryLoad(dlg.FileName, out var project, out var error))
            {
                MessageBox.Show(this, "Не удалось открыть проект:\n" + error, "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _project = project;
            if (string.IsNullOrEmpty(_project.Settings.GameDirectory))
                _project.Settings.GameDirectory = ProjectManager.FindGameDirectory() ?? "";

            AttachGraph();
            UpdateGameStatus();
            Log($"Открыт проект: {_project.RootPath}");
        }

        private void SaveProjectAs()
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "Проект MSCNodeIDE|project.json",
                FileName = "project.json",
                InitialDirectory = _project?.RootPath
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (!ProjectSerializer.TrySaveTo(dlg.FileName, _project, out var error))
                MessageBox.Show(this, error, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else
                Log("Проект сохранён: " + dlg.FileName);
        }

        private void SaveProject(bool silent = false)
        {
            if (_project?.RootPath == null)
            {
                SaveProjectAs();
                return;
            }
            Directory.CreateDirectory(_project.RootPath);
            var path = Path.Combine(_project.RootPath, "project.json");
            if (!ProjectSerializer.TrySaveTo(path, _project, out var error))
            {
                Log("Ошибка сохранения: " + error);
                if (!silent) MessageBox.Show(this, error, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Log("Проект сохранён: " + path);
        }

        private void EditSettings()
        {
            var s = _project.Settings;
            using var f = new Form
            {
                Width = 460, Height = 330, Text = "Настройки проекта",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog
            };

            var l1 = Lbl("Название мода:", 14);
            var t1 = Txt(s.Name, 14, 26);
            var l2 = Lbl("Автор:", 60);
            var t2 = Txt(s.Author, 60, 72);
            var l3 = Lbl("Версия:", 106);
            var t3 = Txt(s.Version, 106, 118);
            var l4 = Lbl("Описание:", 152);
            var t4 = new TextBox { Left = 14, Top = 164, Width = 430, Height = 50, Multiline = true, Text = s.Description };
            var l5 = Lbl("Папка игры (путь к My Summer Car):", 222);
            var t5 = new TextBox { Left = 14, Top = 240, Width = 340, Text = s.GameDirectory };
            var browse = new Button { Text = "Обзор...", Left = 360, Top = 238, Width = 84, Height = 23 };
            var chk = new CheckBox
            {
                Left = 14, Top = 272, Width = 300,
                Text = "Копировать DLL в папку Mods при сборке",
                Checked = s.CopyToModsOnBuild
            };
            var ok = new Button { Text = "OK", Left = 272, Top = 296, Width = 82, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Отмена", Left = 362, Top = 296, Width = 82 };
            cancel.Click += (a, b) => f.DialogResult = DialogResult.Cancel;

            browse.Click += (a, b) =>
            {
                using var d = new FolderBrowserDialog();
                if (d.ShowDialog() == DialogResult.OK) t5.Text = d.SelectedPath;
            };
            ok.Click += (a, b) =>
            {
                s.Name = t1.Text.Trim();
                s.Author = t2.Text.Trim();
                s.Version = t3.Text.Trim();
                s.Description = t4.Text.Trim();
                s.GameDirectory = t5.Text.Trim();
                s.CopyToModsOnBuild = chk.Checked;
                f.DialogResult = DialogResult.OK;
            };

            f.Controls.AddRange(new Control[] { l1, t1, l2, t2, l3, t3, l4, t4, l5, t5, browse, chk, ok, cancel });
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            if (f.ShowDialog() == DialogResult.OK)
            {
                UpdateGameStatus();
                Log($"Настройки обновлены. Игра: {s.GameDirectory}");
            }
        }

        private static Label Lbl(string t, int y) => new Label
        { Text = t, Left = 14, Top = y, Width = 420, AutoSize = false, Height = 16 };

        private static TextBox Txt(string t, int y, int ty) => new TextBox
        { Text = t, Left = 14, Top = ty, Width = 430 };

        // ---------------- генерация и сборка ----------------

        private GeneratedCode CurrentCode()
        {
            var graph = _project.Graphs.FirstOrDefault() ?? new NodeGraph();
            return CodeGenerator.Generate(graph, _project.Settings, _library);
        }

        /// <summary>Только показывает код в панели — без записи файлов на диск.</summary>
        private void ShowCodeOnly()
        {
            if (_project == null) return;
            var gen = CurrentCode();
            _codeView.Text = gen.SourceCode;
            var errors = gen.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            Log($"Код показан: {gen.SourceCode.Split('\n').Length} строк, ошибок: {errors}");
            foreach (var d in gen.Diagnostics.Take(20))
                Log($"  [{d.Severity}] {d.Message}");
            SetStatus(errors > 0 ? $"Ошибок генерации: {errors}" : "Код показан");
        }

        private void GenerateCode(bool showPanel)
        {
            if (_project == null) { Log("Сначала создайте проект."); return; }

            var gen = CurrentCode();
            _codeView.Text = gen.SourceCode;

            var errors = gen.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            var warns = gen.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

            Log($"Код сгенерирован: {gen.SourceCode.Split('\n').Length} строк, " +
                $"ошибок: {errors}, предупреждений: {warns}");

            foreach (var d in gen.Diagnostics.Take(30))
                Log($"  [{d.Severity}] {d.Message}");

            // Показать код можно и без сохранённого проекта — файлы не нужны.
            if (showPanel)
            {
                _codeView.Parent.Visible = true;
                _codeView.Focus();
            }

            if (!EnsureProjectLocation()) return;

            try
            {
                WriteProjectFiles(gen.SourceCode);
            }
            catch (Exception ex)
            {
                Log("Ошибка записи файлов проекта: " + ex.Message);
                MessageBox.Show(this,
                    "Не удалось записать файлы проекта:\n" + ex.Message,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SetStatus(errors > 0 ? $"Ошибок генерации: {errors}" : "Код сгенерирован без ошибок");
        }

        /// <summary>
        /// Убеждается, что проект существует на диске. Если пользователь ещё не
        /// сохранял проект — спрашивает папку. Без этого Path.Combine падал бы на null.
        /// </summary>
        private bool EnsureProjectLocation()
        {
            if (!string.IsNullOrEmpty(_project.RootPath) && Directory.Exists(_project.RootPath))
                return true;

            var answer = MessageBox.Show(this,
                "Проект ещё не сохранён на диск.\n" +
                "Выбрать папку для проекта сейчас?\n\n" +
                "(Файлы .cs и .csproj создаются только в папке проекта.)",
                "Нужна папка проекта", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return false;

            using var dlg = new FolderBrowserDialog
            {
                Description = "Папка проекта мода",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;

            var root = Path.Combine(dlg.SelectedPath, _project.Settings.Name);
            if (Directory.Exists(root) &&
                System.IO.File.Exists(Path.Combine(root, "project.json")))
            {
                if (!ProjectSerializer.TryLoad(Path.Combine(root, "project.json"),
                        out var loaded, out var err))
                {
                    MessageBox.Show(this, "Не удалось загрузить проект:\n" + err,
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
                // сохраняем настройки редактора поверх загруженного проекта
                loaded.Settings.GameDirectory = _project.Settings.GameDirectory;
                _project = loaded;
            }

            _project.RootPath = root;
            ProjectManager.CreateProjectStructure(root, _project.Settings);
            SaveProject(silent: true);
            Log("Папка проекта: " + root);
            return true;
        }

        private void WriteProjectFiles(string sourceCode)
        {
            // Всё через UTF8 BOM: без BOM компилятор читает .cs в ANSI-кодировке,
            // и кириллица в текстах мода ломается.
            var genDir = Path.Combine(_project.RootPath, "Generated");
            Directory.CreateDirectory(genDir);
            ProjectManager.WriteUtf8(Path.Combine(genDir, _project.Settings.Name + ".cs"), sourceCode);

            ProjectManager.WriteUtf8(
                Path.Combine(_project.RootPath, _project.Settings.Name + ".csproj"),
                ProjectManager.GenerateCsproj(_project.Settings, _project.Settings.GameDirectory));

            ProjectManager.WriteUtf8(
                Path.Combine(_project.RootPath, "Properties", "AssemblyInfo.cs"),
                ProjectManager.GenerateAssemblyInfo(_project.Settings));
        }

        private void BuildMod(bool copyToMods = false)
        {
            if (_project == null) { Log("Сначала создайте проект."); return; }
            if (string.IsNullOrEmpty(_project.Settings.GameDirectory) ||
                !Directory.Exists(ProjectManager.ManagedDirectory(_project.Settings.GameDirectory)))
            {
                Log("Не найдена папка игры. Откройте Настройки проекта.");
                MessageBox.Show(this,
                    "Не удалось определить папку My Summer Car.\nУкажите её в Настройках проекта.",
                    "Игра не найдена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!EnsureProjectLocation()) return;

            SaveProject(silent: true);
            GenerateCode(showPanel: false);
            SetStatus("Сборка...");
            Refresh();

            var use = copyToMods || _project.Settings.CopyToModsOnBuild;
            var sw = Stopwatch.StartNew();
            var res = ProjectManager.Build(_project.RootPath, _project.Settings, use);
            sw.Stop();

            Log($"--- Сборка {(res.Success ? "успешна" : "провалена")} за {sw.ElapsedMilliseconds} мс ---");
            foreach (var line in (res.Output ?? "").Split('\n'))
                if (!string.IsNullOrWhiteSpace(line)) Log("  " + line.TrimEnd());

            foreach (var e in res.Errors)
                Log($"  !! {e}");

            if (res.Success)
            {
                Log($"DLL: {res.DllPath}");
                if (use)
                {
                    var mods = Path.Combine(_project.Settings.GameDirectory, "Mods");
                    Log($"Скопировано в: {Path.Combine(mods, _project.Settings.Name + ".dll")}");
                    MessageBox.Show(this,
                        $"Мод собран и установлен:\n{Path.Combine(mods, _project.Settings.Name + ".dll")}\n\n" +
                        "Запустите My Summer Car.",
                        "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                SetStatus("Сборка успешна");
            }
            else
            {
                SetStatus("Ошибки сборки — смотрите лог");
                MessageBox.Show(this, "Сборка не удалась. Подробности — внизу окна.",
                    "Ошибка сборки", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------- сервис ----------------

        private void FindGame()
        {
            var dir = ProjectManager.FindGameDirectory();
            if (dir == null)
            {
                MessageBox.Show(this, "Папка игры не найдена.\nУкажите её вручную в Настройках проекта.",
                    "Не найдено", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Log("Папка игры: " + dir);
            _project.Settings.GameDirectory = dir;
            UpdateGameStatus();
            MessageBox.Show(this, dir, "Папка игры найдена",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ScanApi()
        {
            var gameDir = _project.Settings.GameDirectory;
            if (string.IsNullOrEmpty(gameDir))
            {
                Log("Сначала укажите папку игры.");
                return;
            }

            SetStatus("Сканирование API...");
            Refresh();
            var sw = Stopwatch.StartNew();

            _api?.Dispose();
            _api = ApiCatalog.Load(ProjectManager.ManagedDirectory(gameDir));
            _library = NodeLibrary.CreateDefault(_api);
            _palette.SetLibrary(_library);
            sw.Stop();

            Log($"API просканирован за {sw.ElapsedMilliseconds} мс");
            Log($"Сборки: {string.Join(", ", _api.AssembliesLoaded)}");
            Log($"Статических методов найдено: {_api.StaticMethods.Count}");
            foreach (var err in _api.LoadErrors.Take(5)) Log("  ! " + err);

            SetStatus($"В палитре {_library.All.Count()} узлов");
        }

        private void RefreshInspector()
        {
            var id = _canvas.SelectedNodes.FirstOrDefault();
            _inspector.Show(id != null ? _canvas.Graph.Find(id) : null);
        }

        private void MarkDirty()
        {
            SetStatus("Есть несохранённые изменения");
        }

        /// <summary>Холст не умеет поднимать своё событие наружу — делаем это здесь.</summary>
        private void OnGraphEdited()
        {
            MarkDirty();
            RefreshInspector();
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.Text = text;
        }

        private void UpdateGameStatus()
        {
            var dir = _project?.Settings.GameDirectory;
            var ok = !string.IsNullOrEmpty(dir) && Directory.Exists(dir);
            _gameStatus.Text = ok
                ? "Игра: " + Path.GetFileName(dir.TrimEnd('\\')) + "  (" + dir + ")"
                : "Игра: не найдена — укажите в настройках";
        }

        private void Log(string message)
        {
            if (_logView == null) return;
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            _logView.AppendText($"[{stamp}] {message}\r\n");
            _logView.SelectionStart = _logView.TextLength;
            _logView.ScrollToCaret();
        }

        /// <summary>Вызывается глобальным обработчиком: пишет стек ошибки в лог.</summary>
        public void ReportInternalError(Exception ex)
        {
            Log("!!! Внутренняя ошибка: " + ex.Message);
            Log(ex.StackTrace ?? "(без стека)");
            if (ex.InnerException != null) Log("Причина: " + ex.InnerException.Message);
        }

        private void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                MessageBox.Show(this, "Папка не найдена", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _api?.Dispose();
            base.OnFormClosing(e);
        }
    }
}