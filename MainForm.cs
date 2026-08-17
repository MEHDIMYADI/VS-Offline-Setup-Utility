// Copyright (c) 2026 MEHDIMYADI (https://github.com/MEHDIMYADI/)
// Licensed under the MIT License. See LICENSE file in the project root.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VSOfflineTool
{
    internal class MainForm : Form
    {
        private readonly HttpClient _http = new HttpClient();
        private readonly AppSettings _settings;

        // Prevent recursive updates while synchronizing the two folder boxes.
        private bool _updatingSharedFolder;

        // Prevent control-change events from overwriting saved settings
        // while the initial settings are being restored.
        private bool _loadingSettings;

        // ---------------- DOWNLOAD ----------------

        private ComboBox _editionCombo;
        private ComboBox _languageCombo;

        private CheckBox _recommendedCheck;
        private CheckBox _optionalCheck;

        private TextBox _folderBox;
        private TreeView _workloadTree;
        private TextBox _cliPreview;

        private Button _downloadButton;

        private List<Workload> _currentWorkloads = new List<Workload>();

        // ---------------- CLEANUP ----------------

        private TextBox _cleanupFolderBox;
        private ListView _oldModulesList;
        private Button _deleteOldButton;
        private Button _officialCleanButton;

        private List<VsModule> _oldModules = new List<VsModule>();
        private bool _cleanupTabActivated;

        // ---------------- TREE ----------------

        private ImageList _treeStateImages;

        public MainForm()
        {
            _settings = SettingsStore.Load();

            Text = "VS Offline Setup Utility";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            Width = 1000;
            Height = 760;
            MinimumSize = new Size(850, 620);
            StartPosition = FormStartPosition.CenterScreen;

            BuildTreeStateImages();

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill
            };

            var downloadTab = BuildDownloadTab();
            var cleanupTab = BuildCleanupTab();

            tabs.TabPages.Add(downloadTab);
            tabs.TabPages.Add(cleanupTab);

            tabs.SelectedIndexChanged += (s, e) =>
            {
                if (tabs.SelectedTab == cleanupTab)
                {
                    _cleanupTabActivated = true;
                    RefreshCleanupListIfPossible();
                }
            };

            Controls.Add(tabs);

            var footer = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                Text = "VS Offline Setup Utility — No warranty provided. Not affiliated with Microsoft or any third party. No user data is collected.",
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 7.5f),
                TextAlign = ContentAlignment.MiddleCenter,
            };

            Controls.Add(footer);

            // Restore the user's last saved settings after all controls exist.
            ApplySavedSettings();

            /*
             * ============================================================
             * TEMPORARILY DISABLED - CUSTOM THEME SYSTEM
             * ============================================================
             * Custom light/dark theme is currently disabled.
             * Keeping the code here for possible future re-enablement.
             *
             * ApplyCurrentTheme();
             *
             * ThemeManager.StartListening();
             * ThemeManager.ThemeChanged += OnWindowsThemeChanged;
             *
             * Reason:
             * Native WinForms controls such as CheckBox, ComboBox,
             * TreeView and the Windows title bar do not always render
             * consistently when forcing a custom dark theme.
             *
             * For now the application uses the standard Windows/WinForms
             * appearance.
             * ============================================================
             */
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // The native window handle now exists, so DWM title-bar
            // attributes can actually be applied.
            // Custom ThemeManager temporarily disabled.
            // BeginInvoke((Action)ApplyCurrentTheme);
        }

        /*
        private void OnWindowsThemeChanged()
        {
            // SystemEvents raises this on a non-UI thread.
            if (InvokeRequired)
            {
                BeginInvoke((Action)ApplyCurrentTheme);
                return;
            }

            ApplyCurrentTheme();
        }
        */

        /*private void ApplyCurrentTheme()
        {
            bool dark = ThemeManager.IsDarkTheme();

            ThemeManager.Apply(this, dark);

            // The tree's checkbox glyphs are drawn bitmaps with a baked-in
            // background color, so they need to be regenerated per theme.
            BuildTreeStateImages(dark);
            RefreshAllTreeStates();
        }*/

        // ============================================================
        // SHARED SETTINGS / FOLDER
        // ============================================================

        private void ApplySavedSettings()
        {
            _loadingSettings = true;

            try
            {
                // Restore language.
                _languageCombo.SelectedItem = _settings.SelectedLanguage;

                if (_languageCombo.SelectedIndex < 0)
                    _languageCombo.SelectedIndex = 0;

                // Restore checkboxes.
                _recommendedCheck.Checked = _settings.IncludeRecommended;
                _optionalCheck.Checked = _settings.IncludeOptional;

                // Restore edition.
                if (!string.IsNullOrWhiteSpace(_settings.SelectedEdition))
                {
                    for (int i = 0; i < _editionCombo.Items.Count; i++)
                    {
                        if (_editionCombo.Items[i] is VsEdition edition &&
                            edition.Name.Equals(
                                _settings.SelectedEdition,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            _editionCombo.SelectedIndex = i;
                            break;
                        }
                    }
                }

                if (_editionCombo.SelectedIndex < 0 &&
                    _editionCombo.Items.Count > 0)
                {
                    _editionCombo.SelectedIndex = 0;
                }

                // Restore shared download/cleanup folder.
                SetSharedFolder(
                    _settings.OfflineLayoutPath,
                    reloadCleanup: false,
                    save: false);
            }
            finally
            {
                _loadingSettings = false;
            }

            // Synchronize the runtime component settings after restoration.
            ComponentSettings.Language =
                _languageCombo.SelectedItem as string ?? "en-US";

            ComponentSettings.IsRecommended =
                _recommendedCheck.Checked;

            ComponentSettings.IsOptional =
                _optionalCheck.Checked;

            // Load workloads for the restored edition.
            _ = LoadWorkloadsAsync();

            // Generate the CLI using the restored settings.
            RegenerateCli();
        }

        private string SharedFolderPath => _folderBox?.Text?.Trim() ?? "";

        private void SetSharedFolder(string path, bool reloadCleanup = true, bool save = true)
        {
            if (_updatingSharedFolder)
                return;

            _updatingSharedFolder = true;
            try
            {
                path = path?.Trim() ?? "";

                _folderBox.Text = path;
                _cleanupFolderBox.Text = path;

                _settings.OfflineLayoutPath = path;
                if (save)
                    SaveSettings();

                RegenerateCli();

                if (reloadCleanup)
                    RefreshCleanupListIfPossible();
            }
            finally
            {
                _updatingSharedFolder = false;
            }
        }

        private void FolderBox_TextChanged(object sender, EventArgs e)
        {
            if (_updatingSharedFolder)
                return;

            SetSharedFolder(((TextBox)sender).Text, reloadCleanup: true, save: true);
        }

        private void SaveSettings()
        {
            _settings.SelectedLanguage = _languageCombo?.SelectedItem as string ?? "en-US";
            _settings.IncludeRecommended = _recommendedCheck?.Checked ?? false;
            _settings.IncludeOptional = _optionalCheck?.Checked ?? false;

            if (_editionCombo?.SelectedItem is VsEdition edition)
                _settings.SelectedEdition = edition.Name;

            SettingsStore.Save(_settings);
        }

        // ============================================================
        // DOWNLOAD TAB
        // ============================================================

        private TabPage BuildDownloadTab()
        {
            var page = new TabPage("Download");

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // options row
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // note label
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); // tree
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // cli label
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20)); // cli box
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // download button

            // ---------------- TOP ROW ----------------

            var topRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };

            _editionCombo = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var edition in VsEditionCatalog.GetAll())
                _editionCombo.Items.Add(edition);
            _editionCombo.DisplayMember = "Name";
            _editionCombo.SelectedIndexChanged += async (s, e) =>
            {
                if (_loadingSettings)
                    return;

                SaveSettings();
                await LoadWorkloadsAsync();
            };
            topRow.Controls.Add(Labeled("Edition:", _editionCombo));

            _languageCombo = new ComboBox { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            _languageCombo.Items.AddRange(new object[]
            {
                "cs-CZ", "de-DE", "en-US", "es-ES", "fr-FR", "it-IT", "ja-JP",
                "ko-KR", "pl-PL", "pt-BR", "ru-RU", "tr-TR", "zh-CN", "zh-TW"
            });
            _languageCombo.SelectedIndexChanged += (s, e) =>
            {
                ComponentSettings.Language =
                    _languageCombo.SelectedItem as string ?? "en-US";

                if (_loadingSettings)
                    return;

                SaveSettings();
                RegenerateCli();
            };
            topRow.Controls.Add(Labeled("Language:", _languageCombo));

            _recommendedCheck = new CheckBox { Text = "Include recommended", AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
            _recommendedCheck.CheckedChanged += (s, e) =>
            {
                ComponentSettings.IsRecommended = _recommendedCheck.Checked;

                if (_loadingSettings)
                    return;

                SaveSettings();
                RefreshAllTreeStates();
                RegenerateCli();
            };
            topRow.Controls.Add(_recommendedCheck);

            _optionalCheck = new CheckBox { Text = "Include optional", AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
            _optionalCheck.CheckedChanged += (s, e) =>
            {
                ComponentSettings.IsOptional = _optionalCheck.Checked;

                if (_loadingSettings)
                    return;

                SaveSettings();
                RefreshAllTreeStates();
                RegenerateCli();
            };
            topRow.Controls.Add(_optionalCheck);

            var folderButton = new Button { Text = "Select folder...", AutoSize = true, Margin = new Padding(12, 3, 3, 3) };
            folderButton.Click += (s, e) => PickDownloadFolder();
            topRow.Controls.Add(folderButton);

            _folderBox = new TextBox { Width = 400, ReadOnly = false, Margin = new Padding(3, 8, 3, 3) };
            _folderBox.TextChanged += FolderBox_TextChanged;
            topRow.Controls.Add(_folderBox);

            layout.Controls.Add(topRow, 0, 0);

            // ---------------- NOTE (mirrors the original app's guidance text) ----------------

            var noteLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(950, 0),
                ForeColor = SystemColors.ControlDarkDark,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Italic),
                Margin = new Padding(3, 0, 3, 6),
                Text = "Note: If no checkbox below is selected, all workload packages will be installed " +
                       "(the CLI command is generated without any --add switch, so the installer falls back " +
                       "to its full default layout). Check individual workloads/components to customize the selection.",
            };
            layout.Controls.Add(noteLabel, 0, 1);

            // ---------------- WORKLOAD TREE ----------------

            _workloadTree = new TreeView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = false, // we render our own 3-state checkbox glyphs via StateImageList
                StateImageList = _treeStateImages,
                HideSelection = false,
                ShowNodeToolTips = true,
                FullRowSelect = false,
            };
            _workloadTree.MouseDown += WorkloadTree_MouseDown;
            layout.Controls.Add(_workloadTree, 0, 2);

            var cliLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(3, 6, 3, 0),
                Text = "Command Prompt will execute the command below:",
            };
            layout.Controls.Add(cliLabel, 0, 3);

            // ---------------- CLI PREVIEW ----------------

            _cliPreview = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9),
            };
            layout.Controls.Add(_cliPreview, 0, 4);

            // ---------------- DOWNLOAD BUTTON ----------------

            _downloadButton = new Button
            {
                Text = "Download setup && generate .bat",
                AutoSize = true,
                Margin = new Padding(3, 8, 3, 3),
            };
            _downloadButton.Click += async (s, e) => await DownloadAndRunAsync();
            layout.Controls.Add(_downloadButton, 0, 5);

            page.Controls.Add(layout);
            return page;
        }

        private static Control Labeled(string label, Control input)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(3) };
            panel.Controls.Add(new Label { Text = label, AutoSize = true });
            panel.Controls.Add(input);
            return panel;
        }

        private void PickDownloadFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Select the offline setup layout folder" })
            {
                if (Directory.Exists(SharedFolderPath))
                    dialog.SelectedPath = SharedFolderPath;

                if (dialog.ShowDialog() == DialogResult.OK)
                    SetSharedFolder(dialog.SelectedPath, reloadCleanup: true, save: true);
            }
        }

        private async Task LoadWorkloadsAsync()
        {
            if (!(_editionCombo.SelectedItem is VsEdition edition))
                return;

            _workloadTree.Nodes.Clear();
            _currentWorkloads.Clear();
            _cliPreview.Text = "";

            Cursor = Cursors.WaitCursor;
            try
            {
                var markdown = await _http.GetStringAsync(edition.WorkloadMarkdownUri);
                _currentWorkloads = MarkdownWorkloadParser.Parse(markdown);

                foreach (var workload in _currentWorkloads)
                {
                    var workloadNode = new TreeNode(workload.Name) { Tag = workload, ToolTipText = workload.Id };

                    foreach (var component in workload.Components)
                    {
                        var componentNode = new TreeNode(component.FullName) { Tag = component, ToolTipText = component.Id };
                        workloadNode.Nodes.Add(componentNode);
                    }

                    _workloadTree.Nodes.Add(workloadNode);
                    RefreshNodeState(workloadNode);
                }

                RegenerateCli();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error occured: " + ex.GetType() + ". Make sure internet connection is available.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        // ============================================================
        // THREE-STATE TREE (unchecked / checked / indeterminate)
        // ============================================================

        private void BuildTreeStateImages(bool dark = false)
        {
            _treeStateImages = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            _treeStateImages.Images.Add(CreateCheckImage(ButtonState.Normal, dark));    // 0 = unchecked
            _treeStateImages.Images.Add(CreateCheckImage(ButtonState.Checked, dark));   // 1 = checked
            _treeStateImages.Images.Add(CreateCheckImage(ButtonState.Inactive, dark));  // 2 = indeterminate

            if (_workloadTree != null)
            {
                _workloadTree.StateImageList = _treeStateImages;
                _workloadTree.Invalidate();
            }
        }

        private static Bitmap CreateCheckImage(ButtonState state, bool dark)
        {
            var bitmap = new Bitmap(16, 16);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(
                    dark
                        ? ThemeManager.DarkBackAlt
                        : SystemColors.Window);

                var box = new Rectangle(2, 2, 12, 12);

                Color borderColor = dark
                    ? ThemeManager.DarkBorder
                    : Color.FromArgb(100, 100, 100);

                Color fillColor = dark
                    ? ThemeManager.DarkControl
                    : Color.White;

                Color checkColor = dark
                    ? ThemeManager.DarkFore
                    : Color.FromArgb(30, 30, 30);

                bool checkedState =
                    state == ButtonState.Checked;

                bool indeterminate =
                    state == ButtonState.Inactive;

                using (var fillBrush = new SolidBrush(fillColor))
                using (var borderPen = new Pen(borderColor))
                {
                    graphics.FillRectangle(fillBrush, box);
                    graphics.DrawRectangle(borderPen, box);
                }

                if (indeterminate)
                {
                    using (var brush = new SolidBrush(
                        dark
                            ? Color.FromArgb(160, 160, 160)
                            : Color.FromArgb(90, 90, 90)))
                    {
                        graphics.FillRectangle(
                            brush,
                            new Rectangle(5, 7, 6, 2));
                    }
                }
                else if (checkedState)
                {
                    using (var pen = new Pen(checkColor, 2f))
                    {
                        pen.StartCap =
                            System.Drawing.Drawing2D.LineCap.Round;

                        pen.EndCap =
                            System.Drawing.Drawing2D.LineCap.Round;

                        graphics.DrawLines(
                            pen,
                            new[]
                            {
                        new Point(4, 8),
                        new Point(7, 11),
                        new Point(12, 5)
                            });
                    }
                }
            }

            return bitmap;
        }

        private void WorkloadTree_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            // Use the TreeView's own hit-test rather than approximating pixel
            // offsets by hand - this is the officially supported way to know
            // whether the click landed on the state-image glyph.
            var hit = _workloadTree.HitTest(e.Location);
            if (hit.Node == null)
                return;

            _workloadTree.SelectedNode = hit.Node;

            if ((hit.Location & TreeViewHitTestLocations.StateImage) == TreeViewHitTestLocations.StateImage)
                ToggleNode(hit.Node);
        }

        private void ToggleNode(TreeNode node)
        {
            if (node.Tag is Workload workload)
            {
                // Checking/unchecking the workload only ever changes the
                // workload's OWN explicit flag. We never touch children's
                // IsSelfSelected here - that exactly matches the original
                // project: Required/Recommended/Optional components become
                // (visually) selected only because their IsSelected getter
                // now evaluates "implied by workload", and Independent
                // components are NEVER implied - only direct clicks select them.
                workload.IsSelfSelected = !workload.IsSelfSelected;
                RefreshNodeAndChildren(node);
            }
            else if (node.Tag is Component component)
            {
                // Matches the original's IsSelectable gate: a component that
                // is only selected because the workload implies it cannot be
                // toggled directly (it's effectively disabled).
                if (!component.IsSelectable)
                    return;

                component.IsSelfSelected = !component.IsSelfSelected;
                RefreshNodeState(node);
                RefreshNodeState(node.Parent);
            }

            RegenerateCli();
        }

        private void RefreshAllTreeStates()
        {
            foreach (TreeNode node in _workloadTree.Nodes)
                RefreshNodeAndChildren(node);
        }

        private void RefreshNodeAndChildren(TreeNode node)
        {
            foreach (TreeNode child in node.Nodes)
                RefreshNodeState(child);

            RefreshNodeState(node);
        }

        private void RefreshNodeState(TreeNode node)
        {
            if (node == null)
                return;

            if (node.Tag is Workload workload)
            {
                node.StateImageIndex = StateToImageIndex(GetWorkloadState(workload));
            }
            else if (node.Tag is Component component)
            {
                node.StateImageIndex = StateToImageIndex(component.IsSelected ? CheckState.Checked : CheckState.Unchecked);
                node.ForeColor = component.IsSelectable ? SystemColors.ControlText : SystemColors.GrayText;
            }
        }

        /// <summary>
        /// A workload is fully checked only when the user explicitly checked
        /// it. It's indeterminate when it is NOT explicitly checked but at
        /// least one of its child components was individually self-selected
        /// (which can happen for any dependency kind, matching the original
        /// project's ability to --add a single component without its workload).
        /// </summary>
        private CheckState GetWorkloadState(Workload workload)
        {
            if (workload.IsSelfSelected)
                return CheckState.Checked;

            if (workload.HasAnyExplicitComponentSelection)
                return CheckState.Indeterminate;

            return CheckState.Unchecked;
        }

        private static int StateToImageIndex(CheckState state)
        {
            switch (state)
            {
                case CheckState.Checked: return 1;
                case CheckState.Indeterminate: return 2;
                default: return 0;
            }
        }

        // ============================================================
        // CLI
        // ============================================================

        private void RegenerateCli()
        {
            if (!(_editionCombo?.SelectedItem is VsEdition edition))
                return;

            var exeName = edition.Name.Replace(' ', '_') + ".exe";
            var parts = new List<string> { exeName };

            if (!string.IsNullOrWhiteSpace(SharedFolderPath))
                parts.Add($"--layout \"{SharedFolderPath}\"");

            var addIds = new List<string>();

            bool anyExplicitWorkload = _currentWorkloads.Any(w => w.IsSelfSelected);
            bool anyExplicitComponent = _currentWorkloads.SelectMany(w => w.Components).Any(c => c.IsSelfSelected);

            // Just like the original utility: if nothing has been explicitly
            // selected, we deliberately omit --add entirely. The Visual
            // Studio bootstrapper then falls back to its full default
            // layout (== "all workload packages will be installed"),
            // matching the note shown above the tree.
            if (anyExplicitWorkload || anyExplicitComponent)
            {
                foreach (var workload in _currentWorkloads)
                {
                    if (workload.IsSelfSelected && !string.IsNullOrWhiteSpace(workload.Id))
                        addIds.Add(workload.Id);

                    foreach (var component in workload.Components)
                    {
                        if (component.IsSelfSelected && !string.IsNullOrWhiteSpace(component.Id))
                            addIds.Add(component.Id);
                    }
                }
            }

            foreach (var id in addIds.Distinct(StringComparer.OrdinalIgnoreCase))
                parts.Add("--add " + id);

            if (_recommendedCheck.Checked)
                parts.Add("--includeRecommended");
            if (_optionalCheck.Checked)
                parts.Add("--includeOptional");

            parts.Add("--lang " + (_languageCombo.SelectedItem as string ?? "en-US"));

            _cliPreview.Text = string.Join(" ", parts);
        }

        // ============================================================
        // DOWNLOAD
        // ============================================================

        private async Task DownloadAndRunAsync()
        {
            if (!(_editionCombo.SelectedItem is VsEdition edition))
            {
                MessageBox.Show("Select an edition first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(SharedFolderPath))
            {
                MessageBox.Show("Select a destination folder first.");
                return;
            }

            SaveSettings();

            var setupDir = Directory.CreateDirectory(Path.Combine(SharedFolderPath, "Setup"));
            var exePath = Path.Combine(setupDir.FullName, edition.Name.Replace(' ', '_') + ".exe");
            var batPath = Path.Combine(setupDir.FullName, "CliCommand.bat");

            Cursor = Cursors.WaitCursor;
            _downloadButton.Enabled = false;
            try
            {
                var bytes = await _http.GetByteArrayAsync(edition.SetupUri);
                File.WriteAllBytes(exePath, bytes);
                File.WriteAllText(batPath, _cliPreview.Text, Encoding.ASCII);

                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    WorkingDirectory = setupDir.FullName,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error occured: " + ex.GetType(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                _downloadButton.Enabled = true;
            }
        }

        // ============================================================
        // CLEANUP TAB
        // ============================================================

        private TabPage BuildCleanupTab()
        {
            var page = new TabPage("Cleanup");

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // folder row
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // note
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // list
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons

            // ---------------- FOLDER ----------------

            var topRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };

            var folderButton = new Button { Text = "Select offline layout folder...", AutoSize = true, Margin = new Padding(3) };
            folderButton.Click += (s, e) => PickCleanupFolder();
            topRow.Controls.Add(folderButton);

            _cleanupFolderBox = new TextBox { Width = 550, ReadOnly = false, Margin = new Padding(3, 6, 3, 3) };
            _cleanupFolderBox.TextChanged += FolderBox_TextChanged;
            topRow.Controls.Add(_cleanupFolderBox);

            layout.Controls.Add(topRow, 0, 0);

            var noteLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(950, 0),
                ForeColor = SystemColors.ControlDarkDark,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Italic),
                Margin = new Padding(3, 0, 3, 6),
                Text = "Note: If no checkbox below is selected, all listed old-version folders will be deleted " +
                       "(same as the original tool, which has no per-item selection at all).",
            };
            layout.Controls.Add(noteLabel, 0, 1);

            // ---------------- OLD MODULES ----------------

            _oldModulesList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                HideSelection = false,
            };
            _oldModulesList.Columns.Add("Module", 500);
            _oldModulesList.Columns.Add("Version", 180);
            layout.Controls.Add(_oldModulesList, 0, 2);

            // ---------------- BUTTONS ----------------

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };

            _deleteOldButton = new Button { Text = "Delete old versions", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
            _deleteOldButton.Click += (s, e) => DeleteOldVersions();
            buttons.Controls.Add(_deleteOldButton);

            _officialCleanButton = new Button { Text = "Run Visual Studio --clean", AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
            _officialCleanButton.Click += (s, e) => RunOfficialCleanup();
            buttons.Controls.Add(_officialCleanButton);

            layout.Controls.Add(buttons, 0, 3);

            page.Controls.Add(layout);
            return page;
        }

        private void PickCleanupFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Select the offline setup layout folder to clean" })
            {
                if (Directory.Exists(SharedFolderPath))
                    dialog.SelectedPath = SharedFolderPath;

                if (dialog.ShowDialog() == DialogResult.OK)
                    SetSharedFolder(dialog.SelectedPath, reloadCleanup: true, save: true);
            }
        }

        private void RefreshCleanupListIfPossible()
        {
            if (!_cleanupTabActivated)
                return;

            if (_oldModulesList == null)
                return;

            if (string.IsNullOrWhiteSpace(SharedFolderPath) ||
                !Directory.Exists(SharedFolderPath))
            {
                _oldModulesList.Items.Clear();
                _oldModules.Clear();
                return;
            }

            LoadCleanupList(SharedFolderPath);
        }

        private void LoadCleanupList(string folder)
        {
            _oldModulesList.Items.Clear();
            _oldModules = CleanupHelper.FindOldVersionFolders(folder);

            foreach (var module in _oldModules)
            {
                var item = new ListViewItem(module.Name) { Checked = true, Tag = module };
                item.SubItems.Add(module.Version);
                _oldModulesList.Items.Add(item);
            }

            if (_oldModules.Count == 0)
            {
                MessageBox.Show(
                    "No old-version folders were found in the selected offline layout.",
                    "Cleanup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void DeleteOldVersions()
        {
            var checkedItems = _oldModulesList.Items.Cast<ListViewItem>().Where(i => i.Checked).ToList();

            // Mirrors the original tool's behavior (and the note above the
            // list): with no per-item selection UI at all in the original,
            // it always deletes the entire discovered list. We keep the
            // checkboxes as a convenience, but if the user has unchecked
            // everything we fall back to "delete all" rather than doing nothing.
            var toDelete = (checkedItems.Count > 0
                    ? checkedItems
                    : _oldModulesList.Items.Cast<ListViewItem>())
                .Select(i => i.Tag as VsModule)
                .Where(m => m != null)
                .ToList();

            if (toDelete.Count == 0)
            {
                MessageBox.Show("Old version folder does not exist.");
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete {toDelete.Count} folder(s)? This cannot be undone.",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                Cursor = Cursors.WaitCursor;
                CleanupHelper.DeleteFolders(toDelete);
                LoadCleanupList(SharedFolderPath);

                MessageBox.Show("Operation successful.", "Cleanup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error occured: " + ex.GetType(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        // ============================================================
        // OFFICIAL VISUAL STUDIO --clean
        // ============================================================

        private void RunOfficialCleanup()
        {
            if (string.IsNullOrWhiteSpace(SharedFolderPath))
            {
                MessageBox.Show("Select an offline layout folder first.");
                return;
            }

            if (!Directory.Exists(SharedFolderPath))
            {
                MessageBox.Show("The selected offline layout folder does not exist.");
                return;
            }

            var catalogPath = Path.Combine(SharedFolderPath, "Catalog.json");
            if (!File.Exists(catalogPath))
            {
                MessageBox.Show("Catalog.json was not found in the selected offline layout.",
                    "Cleanup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var command = $"vs_setup.exe --layout \"{SharedFolderPath}\" --clean \"{catalogPath}\"";
            var batPath = Path.Combine(SharedFolderPath, "CleanupCommand.bat");

            try
            {
                File.WriteAllText(batPath, command, Encoding.ASCII);

                var confirm = MessageBox.Show(
                    "A CleanupCommand.bat file will be created in the layout folder and executed.\n\n" +
                    "This uses Visual Studio's --clean mechanism.",
                    "Run Visual Studio --clean", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (confirm != DialogResult.Yes)
                    return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    WorkingDirectory = SharedFolderPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error occured: " + ex.GetType(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // FORM LIFETIME
        // ============================================================

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            base.OnFormClosing(e);
        }
    }
}
