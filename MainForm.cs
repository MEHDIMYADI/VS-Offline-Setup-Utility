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

        // ---------------- Layout Analyzer ----------------

        private readonly LayoutAnalyzer _layoutAnalyzer = new LayoutAnalyzer();
        private Button _previewButton;
        private Label _previewLabel;
        private TextBox _previewOutput;

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
        private TextBox _cleanupOutput;

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
                Height = 30,
                Text = "VS Offline Setup Utility — No warranty provided. Not affiliated with Microsoft or any third party. No user data is collected.",
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 8f),
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

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(8)
            };

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // options
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // folder
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // note
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38)); // workloads
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 18)); // installer command
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 24)); // update preview

            // =========================
            // INSTALLATION OPTIONS
            // =========================

            var optionsGroup = new GroupBox
            {
                Text = "Installation options",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(10, 6, 10, 10),
                Margin = new Padding(0, 0, 0, 6)
            };

            var optionsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight
            };

            _editionCombo = new ComboBox
            {
                Width = 250,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(3)
            };

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

            optionsFlow.Controls.Add(Labeled("Edition:", _editionCombo));


            _languageCombo = new ComboBox
            {
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(3)
            };

            _languageCombo.Items.AddRange(new object[]
            {
                "cs-CZ", "de-DE", "en-US", "es-ES", "fr-FR",
                "it-IT", "ja-JP", "ko-KR", "pl-PL", "pt-BR",
                "ru-RU", "tr-TR", "zh-CN", "zh-TW"
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

            optionsFlow.Controls.Add(Labeled("Language:", _languageCombo));


            _recommendedCheck = new CheckBox
            {
                Text = "Include recommended",
                AutoSize = true,
                Margin = new Padding(18, 26, 3, 3)
            };

            _recommendedCheck.CheckedChanged += (s, e) =>
            {
                ComponentSettings.IsRecommended = _recommendedCheck.Checked;

                if (_loadingSettings)
                    return;

                SaveSettings();
                RefreshAllTreeStates();
                RegenerateCli();
            };

            optionsFlow.Controls.Add(_recommendedCheck);


            _optionalCheck = new CheckBox
            {
                Text = "Include optional",
                AutoSize = true,
                Margin = new Padding(12, 26, 3, 3)
            };

            _optionalCheck.CheckedChanged += (s, e) =>
            {
                ComponentSettings.IsOptional = _optionalCheck.Checked;

                if (_loadingSettings)
                    return;

                SaveSettings();
                RefreshAllTreeStates();
                RegenerateCli();
            };

            optionsFlow.Controls.Add(_optionalCheck);

            optionsGroup.Controls.Add(optionsFlow);
            layout.Controls.Add(optionsGroup, 0, 0);


            // =========================
            // DESTINATION FOLDER
            // =========================

            var folderGroup = new GroupBox
            {
                Text = "Destination folder",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(10, 6, 10, 10),
                Margin = new Padding(0, 0, 0, 6)
            };

            var folderFlow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1
            };

            folderFlow.ColumnStyles.Add(
                new ColumnStyle(SizeType.AutoSize));

            folderFlow.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));


            var folderButton = new Button
            {
                Text = "Select folder...",
                AutoSize = true,
                Margin = new Padding(3)
            };

            folderButton.Click += (s, e) => PickDownloadFolder();

            folderFlow.Controls.Add(folderButton, 0, 0);


            _folderBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 4, 3, 3)
            };

            _folderBox.TextChanged += FolderBox_TextChanged;

            folderFlow.Controls.Add(_folderBox, 1, 0);

            folderGroup.Controls.Add(folderFlow);
            layout.Controls.Add(folderGroup, 0, 1);


            // =========================
            // NOTE
            // =========================

            var noteLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(950, 0),
                ForeColor = SystemColors.ControlDarkDark,
                Font = new Font(
                    "Segoe UI",
                    8.25f,
                    FontStyle.Italic),
                Margin = new Padding(3, 4, 3, 6),

                Text =
                    "Note: If no checkbox below is selected, all workload packages will be installed " +
                    "(the CLI command is generated without any --add switch, so the installer falls back " +
                    "to its full default layout). Check individual workloads/components to customize the selection."
            };

            layout.Controls.Add(noteLabel, 0, 2);


            // =========================
            // WORKLOADS
            // =========================

            var workloadGroup = new GroupBox
            {
                Text = "Workloads",
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 10, 6, 6),
                Margin = new Padding(0, 0, 0, 6)
            };

            _workloadTree = new TreeView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = false,
                StateImageList = _treeStateImages,
                HideSelection = false,
                ShowNodeToolTips = true,
                FullRowSelect = false
            };

            _workloadTree.MouseDown += WorkloadTree_MouseDown;

            workloadGroup.Controls.Add(_workloadTree);
            layout.Controls.Add(workloadGroup, 0, 3);


            // =========================
            // INSTALLER COMMAND
            // =========================

            var cliGroup = new GroupBox
            {
                Text = "Installer command",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 12, 8, 8),
                Margin = new Padding(0, 0, 0, 6)
            };

            var cliLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };

            cliLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            cliLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100));


            // =========================
            // DOWNLOAD BUTTON
            // =========================

            _downloadButton = new Button
            {
                Text = "Download setup && run",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 0, 3, 6)
            };

            _downloadButton.Click += async (s, e) =>
                await DownloadAndRunAsync();

            cliLayout.Controls.Add(_downloadButton,0,0);


            // =========================
            // COMMAND PREVIEW
            // =========================

            _cliPreview = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9),
                Margin = new Padding(3)
            };

            cliLayout.Controls.Add(_cliPreview,0,1);

            cliGroup.Controls.Add(cliLayout);

            layout.Controls.Add(cliGroup,0,4);

            // =========================
            // UPDATE PREVIEW
            // =========================

            var previewGroup = new GroupBox
            {
                Text = "Update preview",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 12, 8, 8),
                Margin = new Padding(0)
            };

            var previewLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };

            previewLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            previewLayout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            previewLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100));


            _previewButton = new Button
            {
                Text = "Preview Update",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3)
            };

            _previewButton.Click += async (s, e) =>
                await PreviewLayoutAsync();

            previewLayout.Controls.Add(_previewButton,0,0);


            _previewLabel = new Label
            {
                AutoSize = true,
                Text = "Update preview: not calculated",
                Margin = new Padding(3, 6, 3, 3)
            };

            previewLayout.Controls.Add(
                _previewLabel,
                0,
                1);

            _previewOutput = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 8.5f),
                Margin = new Padding(3)
            };

            previewLayout.Controls.Add(_previewOutput,0,2);

            previewGroup.Controls.Add(previewLayout);

            layout.Controls.Add(previewGroup,0,5);

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
            using var dialog = new FolderBrowserDialog { Description = "Select the offline setup layout folder" };
            if (Directory.Exists(SharedFolderPath))
                dialog.SelectedPath = SharedFolderPath;

            if (dialog.ShowDialog() == DialogResult.OK)
                SetSharedFolder(dialog.SelectedPath, reloadCleanup: true, save: true);
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
                    using var brush = new SolidBrush(
                        dark
                            ? Color.FromArgb(160, 160, 160)
                            : Color.FromArgb(90, 90, 90));
                    graphics.FillRectangle(
                        brush,
                        new Rectangle(5, 7, 6, 2));
                }
                else if (checkedState)
                {
                    using var pen = new Pen(checkColor, 2f);
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
            return state switch
            {
                CheckState.Checked => 1,
                CheckState.Indeterminate => 2,
                _ => 0,
            };
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

            var setupDir = Directory.CreateDirectory(
                Path.Combine(SharedFolderPath, "Setup"));

            var exePath = Path.Combine(
                setupDir.FullName,
                edition.Name.Replace(' ', '_') + ".exe");

            Cursor = Cursors.WaitCursor;
            _downloadButton.Enabled = false;

            try
            {
                var bytes = await _http.GetByteArrayAsync(edition.SetupUri);
                File.WriteAllBytes(exePath, bytes);

                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = _cliPreview.Text.Substring(
                        _cliPreview.Text.IndexOf(' ') + 1),
                    WorkingDirectory = setupDir.FullName,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error occured: " + ex.GetType(),
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                _downloadButton.Enabled = true;
            }
        }

        // ============================================================
        // Preview Layout
        // ============================================================

        private async Task PreviewLayoutAsync()
        {
            if (_previewButton == null)
                return;

            _previewButton.Enabled = false;
            _previewButton.Text = "Please wait...";

            try
            {
                string layoutFolder = _folderBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(layoutFolder))
                {
                    MessageBox.Show(
                        this,
                        "Please select the layout folder first.",
                        "Layout Preview",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }

                if (!Directory.Exists(layoutFolder))
                {
                    MessageBox.Show(
                        this,
                        "The selected layout folder does not exist.",
                        "Layout Preview",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }

                if (!(_editionCombo.SelectedItem is VsEdition edition))
                    return;

                if (string.IsNullOrWhiteSpace(edition.ProductId))
                    return;

                // Microsoft Minimal Layout Tool does not officially support
                // Visual Studio Community for this preview operation.
                if (edition.Name.IndexOf(
                    "Community",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MessageBox.Show(
                        this,
                        "Update preview is not officially supported for " +
                        "Visual Studio Community by Microsoft's Minimal Layout Tool.",
                        "Layout Preview",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    return;
                }

                // ------------------------------------------------------------
                // STEP 1 - Read current layout version
                // ------------------------------------------------------------

                _previewOutput.Clear();

                _previewLabel.Text =
                    "Please wait... (Reading current layout version)";

                await Task.Yield();

                if (!TryReadLayoutVersion(
                    layoutFolder,
                    out string baseVersion))
                {
                    MessageBox.Show(
                        this,
                        "Could not determine the current Visual Studio " +
                        "version from this layout.",
                        "Layout Preview",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }

                // ------------------------------------------------------------
                // STEP 2 - Check latest Visual Studio version
                // ------------------------------------------------------------

                _previewLabel.Text =
                    "Please wait... (Checking latest Visual Studio version)";

                await Task.Yield();

                string targetVersion;

                try
                {
                    targetVersion =
                        await GetLatestVersionAsync(edition);
                }
                catch (HttpRequestException)
                {
                    _previewLabel.Text =
                        "Preview unavailable: no internet connection.";

                    MessageBox.Show(
                        this,
                        "An internet connection is required to check " +
                        "the latest Visual Studio version.\r\n\r\n" +
                        "The existing layout and normal download features " +
                        "are still available.",
                        "Layout Preview",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    return;
                }
                catch (TaskCanceledException)
                {
                    _previewLabel.Text =
                        "Preview cancelled or timed out.";

                    return;
                }
                catch (Exception ex)
                {
                    _previewLabel.Text =
                        "Could not determine latest version.";

                    MessageBox.Show(
                        this,
                        "Could not determine the latest Visual Studio version.\r\n\r\n" +
                        ex.Message,
                        "Layout Preview",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    return;
                }

                // ------------------------------------------------------------
                // STEP 3 - Already up to date?
                // ------------------------------------------------------------

                if (string.Equals(
                    baseVersion,
                    targetVersion,
                    StringComparison.OrdinalIgnoreCase))
                {
                    _previewLabel.Text =
                        $"Already up to date ({targetVersion}).";

                    _previewOutput.Text =
                        $"Current version : {baseVersion}\r\n" +
                        $"Target version  : {targetVersion}\r\n\r\n" +
                        "No update is required.";

                    return;
                }

                // ------------------------------------------------------------
                // STEP 4 - Collect selected components
                // ------------------------------------------------------------

                _previewLabel.Text =
                    "Preparing MinimalLayout preview...";

                await Task.Yield();

                // ------------------------------------------------------------
                // STEP 5 - Open a normal, visible Command Prompt window
                // ------------------------------------------------------------

                _previewLabel.Text =
                    $"Opening Command Prompt: {baseVersion} → {targetVersion}";

                string[] selectedComponents =
                    GetSelectedComponentIds();

                string exeCommand =
                    _layoutAnalyzer.BuildPreviewCommandLine(
                        layoutFolder,
                        edition.ProductId,
                        baseVersion,
                        targetVersion,
                        GetSelectedLanguage(),
                        selectedComponents,
                        _recommendedCheck.Checked,
                        _optionalCheck.Checked,
                        out string error,
                        out string downloadUrl);

                if (exeCommand == null)
                {
                    _previewLabel.Text =
                        "Preview unavailable: Minimal Layout Tool is not installed.";

                    var result = MessageBox.Show(
                        this,
                        error +
                        "\r\n\r\nWould you like to download the Minimal Layout Tool now?",
                        "Minimal Layout Tool",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes &&
                        !string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = downloadUrl,
                            UseShellExecute = true
                        });
                    }

                    return;
                }

                _previewOutput.AppendText(
                    "A Command Prompt window will open to run MinimalLayout.exe.\r\n" +
                    "Read the summary (packages / download size) there, then close the window " +
                    "or press a key when it says \"Press any key to continue\".\r\n\r\n" +
                    "Command:\r\n" + exeCommand + "\r\n");

                string cmdArgs =
                    "/k title Visual Studio Layout Update Preview " +
                    "& echo Current version : " + baseVersion +
                    " & echo Target version  : " + targetVersion +
                    " & echo. & " +
                    exeCommand +
                    " & echo. & echo Preview finished. Exit code: %ERRORLEVEL% & pause";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    WorkingDirectory = layoutFolder,
                    UseShellExecute = true,
                });

                _previewLabel.Text =
                    "Preview running in Command Prompt window.";
            }
            catch (Exception ex)
            {
                _previewLabel.Text =
                    "Preview failed.";

                MessageBox.Show(
                    this,
                    "An unexpected error occurred during the update preview.\r\n\r\n" +
                    ex.Message,
                    "Layout Preview",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _previewButton.Enabled = true;
                _previewButton.Text = "Preview Update";
            }
        }

        private bool TryReadLayoutVersion(string layoutFolder, out string version)
        {
            version = null;

            string channelManifest =
                Path.Combine(
                    layoutFolder,
                    "ChannelManifest.json");

            if (!File.Exists(channelManifest))
                return false;

            try
            {
                string json =
                    File.ReadAllText(channelManifest);

                var match =
                    System.Text.RegularExpressions.Regex.Match(
                        json,
                        @"""productDisplayVersion""\s*:\s*""([^""]+)""",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (!match.Success)
                    return false;

                version =
                    match.Groups[1].Value.Trim();

                return !string.IsNullOrWhiteSpace(version);
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> GetLatestVersionAsync(VsEdition edition)
        {
            if (string.IsNullOrWhiteSpace(
                edition.ChannelUri))
            {
                throw new InvalidOperationException(
                    "Visual Studio channel URI is not configured.");
            }

            string json =
                await _http.GetStringAsync(
                    edition.ChannelUri);

            var match =
                System.Text.RegularExpressions.Regex.Match(
                    json,
                    @"""productDisplayVersion""\s*:\s*""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                throw new InvalidOperationException(
                    "Could not find productDisplayVersion " +
                    "in the Visual Studio channel manifest.");
            }

            return match.Groups[1].Value.Trim();
        }

        private string[] GetSelectedComponentIds()
        {
            bool anyExplicitWorkload =
                _currentWorkloads.Any(w => w.IsSelfSelected);

            bool anyExplicitComponent =
                _currentWorkloads
                    .SelectMany(w => w.Components)
                    .Any(c => c.IsSelfSelected);

            if (!anyExplicitWorkload && !anyExplicitComponent)
                return new string[0];

            var ids = new List<string>();

            foreach (var workload in _currentWorkloads)
            {
                if (workload.IsSelfSelected &&
                    !string.IsNullOrWhiteSpace(workload.Id))
                {
                    ids.Add(workload.Id);
                }

                foreach (var component in workload.Components)
                {
                    if (component.IsSelfSelected &&
                        !string.IsNullOrWhiteSpace(component.Id))
                    {
                        ids.Add(component.Id);
                    }
                }
            }

            return ids
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private string GetSelectedLanguage()
        {
            return _languageCombo.SelectedItem == null
                ? "en-US"
                : _languageCombo.SelectedItem.ToString();
        }

        // ============================================================
        // CLEANUP TAB
        // ============================================================

        private TabPage BuildCleanupTab()
        {
            var page = new TabPage("Cleanup");

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(8)
            };

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // folder
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // note
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); // old modules
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // buttons
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); // output

            // =========================
            // FOLDER
            // =========================

            var folderGroup = new GroupBox
            {
                Text = "Offline layout folder",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(10, 6, 10, 10),
                Margin = new Padding(0, 0, 0, 6)
            };

            var topRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2
            };

            topRow.ColumnStyles.Add(
                new ColumnStyle(SizeType.AutoSize));

            topRow.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));


            var folderButton = new Button
            {
                Text = "Select offline layout folder...",
                AutoSize = true,
                Margin = new Padding(3)
            };

            folderButton.Click += (s, e) =>
                PickCleanupFolder();

            topRow.Controls.Add(folderButton,0,0);


            _cleanupFolderBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 4, 3, 3)
            };

            _cleanupFolderBox.TextChanged += FolderBox_TextChanged;

            topRow.Controls.Add(
                _cleanupFolderBox,
                1,
                0);

            folderGroup.Controls.Add(topRow);

            layout.Controls.Add(folderGroup,0,0);


            // =========================
            // NOTE
            // =========================

            var noteLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(950, 0),
                ForeColor = SystemColors.ControlDarkDark,
                Font = new Font(
                    "Segoe UI",
                    8.25f,
                    FontStyle.Italic),
                Margin = new Padding(3, 4, 3, 6),

                Text =
                    "Note: If no checkbox below is selected, all listed old-version folders will be deleted " +
                    "(same as the original tool, which has no per-item selection at all)."
            };

            layout.Controls.Add(noteLabel,0,1);


            // =========================
            // OLD MODULES
            // =========================

            var modulesGroup = new GroupBox
            {
                Text = "Old versions",
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 10, 6, 6),
                Margin = new Padding(0, 0, 0, 6)
            };

            _oldModulesList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                HideSelection = false
            };

            _oldModulesList.Columns.Add(
                "Module",
                500);

            _oldModulesList.Columns.Add(
                "Version",
                180);

            modulesGroup.Controls.Add(
                _oldModulesList);

            layout.Controls.Add(modulesGroup,0,2);


            // =========================
            // BUTTONS
            // =========================

            var buttonsGroup = new GroupBox
            {
                Text = "Cleanup actions",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8, 6, 8, 8),
                Margin = new Padding(0)
            };

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                WrapContents = false
            };


            _deleteOldButton = new Button
            {
                Text = "Delete old versions",
                AutoSize = true,
                Margin = new Padding(3)
            };

            _deleteOldButton.Click += (s, e) =>
                DeleteOldVersions();

            buttons.Controls.Add(
                _deleteOldButton);


            _officialCleanButton = new Button
            {
                Text = "Run Visual Studio --clean",
                AutoSize = true,
                Margin = new Padding(12, 3, 3, 3)
            };

            _officialCleanButton.Click += (s, e) =>
                RunOfficialCleanup();

            buttons.Controls.Add(
                _officialCleanButton);


            buttonsGroup.Controls.Add(buttons);

            layout.Controls.Add(buttonsGroup,0,3);

            // =========================
            // CLEANUP OUTPUT
            // =========================

            var outputGroup = new GroupBox
            {
                Text = "Cleanup output",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 10, 8, 8),
                Margin = new Padding(0, 6, 0, 0)
            };

            _cleanupOutput = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9),
                Margin = new Padding(3)
            };

            outputGroup.Controls.Add(_cleanupOutput);

            layout.Controls.Add(outputGroup,0,4);


            page.Controls.Add(layout);

            return page;
        }

        private void AppendCleanupLog(string message)
        {
            if (_cleanupOutput == null)
                return;

            if (_cleanupOutput.InvokeRequired)
            {
                _cleanupOutput.BeginInvoke(
                    new Action(() => AppendCleanupLog(message)));
                return;
            }

            _cleanupOutput.AppendText(
                $"[{DateTime.Now:HH:mm:ss}] {message}\r\n");

            _cleanupOutput.SelectionStart = _cleanupOutput.TextLength;
            _cleanupOutput.ScrollToCaret();
        }

        private void ClearCleanupLog()
        {
            _cleanupOutput?.Clear();
        }

        private void PickCleanupFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = "Select the offline setup layout folder to clean" };
            if (Directory.Exists(SharedFolderPath))
                dialog.SelectedPath = SharedFolderPath;

            if (dialog.ShowDialog() == DialogResult.OK)
                SetSharedFolder(dialog.SelectedPath, reloadCleanup: true, save: true);
        }

        private void RefreshCleanupListIfPossible()
        {
            if (!_cleanupTabActivated)
                return;

            if (_oldModulesList == null)
                return;

            if (string.IsNullOrWhiteSpace(SharedFolderPath))
            {
                _oldModulesList.Items.Clear();
                _oldModules.Clear();

                AppendCleanupLog(
                    "No offline layout folder is selected.");

                return;
            }

            if (!Directory.Exists(SharedFolderPath))
            {
                _oldModulesList.Items.Clear();
                _oldModules.Clear();

                AppendCleanupLog(
                    $"Offline layout folder does not exist: {SharedFolderPath}");

                return;
            }

            LoadCleanupList(SharedFolderPath);
        }

        private void LoadCleanupList(string folder)
        {
            _oldModulesList.Items.Clear();

            try
            {
                _oldModules = CleanupHelper.FindOldVersionFolders(folder);

                foreach (var module in _oldModules)
                {
                    var item = new ListViewItem(module.Name)
                    {
                        Checked = true,
                        Tag = module
                    };

                    item.SubItems.Add(module.Version);
                    _oldModulesList.Items.Add(item);
                }

                if (_oldModules.Count == 0)
                {
                    AppendCleanupLog(
                        "No old-version folders were found in the selected offline layout.");

                    return;
                }

                AppendCleanupLog(
                    $"Found {_oldModules.Count} old-version folder(s).");

                foreach (var module in _oldModules)
                {
                    AppendCleanupLog(
                        $"  {module.Name}  |  Version: {module.Version}");
                }
            }
            catch (Exception ex)
            {
                _oldModules.Clear();

                AppendCleanupLog(
                    $"Failed to scan cleanup folder: {ex.GetType().Name}");

                AppendCleanupLog(
                    $"Details: {ex.Message}");
            }
        }

        private void DeleteOldVersions()
        {
            ClearCleanupLog();

            AppendCleanupLog("Starting old-version cleanup...");
            AppendCleanupLog($"Layout folder: {SharedFolderPath}");

            var checkedItems =
                _oldModulesList.Items
                    .Cast<ListViewItem>()
                    .Where(i => i.Checked)
                    .ToList();

            // If nothing is checked, delete all discovered modules.
            var toDelete =
                (checkedItems.Count > 0
                    ? checkedItems
                    : _oldModulesList.Items.Cast<ListViewItem>())
                .Select(i => i.Tag as VsModule)
                .Where(m => m != null)
                .ToList();

            if (toDelete.Count == 0)
            {
                AppendCleanupLog(
                    "Nothing to delete. No old-version folders were found.");

                return;
            }

            AppendCleanupLog(
                $"Selected {toDelete.Count} folder(s) for deletion.");

            foreach (var module in toDelete)
            {
                AppendCleanupLog(
                    $"  Delete: {module.Name} | Version: {module.Version}");
            }

            // This is intentionally the only confirmation dialog.
            var confirm = MessageBox.Show(
                $"Delete {toDelete.Count} folder(s)? This cannot be undone.",
                "Confirm",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
            {
                AppendCleanupLog("Operation cancelled by user.");
                return;
            }

            AppendCleanupLog("User confirmed deletion.");

            try
            {
                Cursor = Cursors.WaitCursor;
                _deleteOldButton.Enabled = false;

                AppendCleanupLog("Deleting old-version folders...");

                CleanupHelper.DeleteFolders(toDelete);

                AppendCleanupLog(
                    $"Successfully deleted {toDelete.Count} folder(s).");

                AppendCleanupLog("Refreshing old-version list...");

                LoadCleanupList(SharedFolderPath);

                AppendCleanupLog("Cleanup operation completed successfully.");
            }
            catch (Exception ex)
            {
                AppendCleanupLog(
                    $"Cleanup failed: {ex.GetType().Name}");

                AppendCleanupLog(
                    $"Details: {ex.Message}");
            }
            finally
            {
                Cursor = Cursors.Default;
                _deleteOldButton.Enabled = true;
            }
        }

        // ============================================================
        // OFFICIAL VISUAL STUDIO --clean
        // ============================================================

        private async void RunOfficialCleanup()
        {
            ClearCleanupLog();

            AppendCleanupLog("Starting Visual Studio --clean...");
            AppendCleanupLog($"Layout folder: {SharedFolderPath}");

            if (string.IsNullOrWhiteSpace(SharedFolderPath))
            {
                AppendCleanupLog(
                    "ERROR: No offline layout folder is selected.");

                return;
            }

            if (!Directory.Exists(SharedFolderPath))
            {
                AppendCleanupLog(
                    "ERROR: The selected offline layout folder does not exist.");

                return;
            }

            var catalogPath = Path.Combine(
                SharedFolderPath,
                "Catalog.json");

            if (!File.Exists(catalogPath))
            {
                AppendCleanupLog(
                    $"ERROR: Catalog.json was not found: {catalogPath}");

                return;
            }

            var setupPath = Path.Combine(
                SharedFolderPath,
                "vs_setup.exe");

            if (!File.Exists(setupPath))
            {
                AppendCleanupLog(
                    $"ERROR: vs_setup.exe was not found: {setupPath}");

                return;
            }

            var arguments =
                $"--layout \"{SharedFolderPath}\" " +
                $"--clean \"{catalogPath}\"";

            AppendCleanupLog($"Executable: {setupPath}");
            AppendCleanupLog($"Arguments: {arguments}");

            // This is intentionally the only confirmation dialog.
            var confirm = MessageBox.Show(
                "Visual Studio --clean will now run against the selected offline layout.\n\n" +
                "No command file will be created.",
                "Run Visual Studio --clean",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (confirm != DialogResult.Yes)
            {
                AppendCleanupLog("Operation cancelled by user.");
                return;
            }

            AppendCleanupLog("User confirmed Visual Studio --clean.");
            AppendCleanupLog("Starting vs_setup.exe...");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = setupPath,
                    Arguments = arguments,
                    WorkingDirectory = SharedFolderPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process
                {
                    StartInfo = psi,
                    EnableRaisingEvents = true
                };

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        AppendCleanupLog(e.Data);
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        AppendCleanupLog("ERROR: " + e.Data);
                };

                if (!process.Start())
                {
                    AppendCleanupLog(
                        "ERROR: Failed to start vs_setup.exe.");

                    return;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                AppendCleanupLog(
                    $"Process started. PID: {process.Id}");

                await WaitForProcessExitAsync(process);

                // Ensure all asynchronous stdout/stderr events are flushed.
                process.WaitForExit();

                AppendCleanupLog(
                    $"Visual Studio --clean finished. Exit code: {process.ExitCode}");

                if (process.ExitCode == 0)
                {
                    AppendCleanupLog(
                        "Visual Studio --clean completed successfully.");
                }
                else
                {
                    AppendCleanupLog(
                        "Visual Studio --clean finished with an error.");
                }
            }
            catch (Exception ex)
            {
                AppendCleanupLog(
                    $"Failed to run Visual Studio --clean: {ex.GetType().Name}");

                AppendCleanupLog(
                    $"Details: {ex.Message}");
            }
        }

        private static Task WaitForProcessExitAsync(Process process)
        {
            var tcs = new TaskCompletionSource<object>();

            process.EnableRaisingEvents = true;

            process.Exited += (s, e) =>
            {
                tcs.TrySetResult(null);
            };

            if (process.HasExited)
                tcs.TrySetResult(null);

            return tcs.Task;
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
