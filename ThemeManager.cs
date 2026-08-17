// Copyright (c) 2026 MEHDIMYADI (https://github.com/MEHDIMYADI/)
// Licensed under the MIT License. See LICENSE file in the project root.
//

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VSOfflineTool
{
    /// <summary>
    /// Detects the current Windows light/dark app theme and applies matching
    /// colors to a WinForms control tree.
    ///
    /// Also raises ThemeChanged whenever the Windows theme changes while
    /// the application is running.
    ///
    /// No NuGet packages are required.
    /// </summary>
    internal static class ThemeManager
    {
        public static event Action ThemeChanged;

        private static bool _subscribed;

        // Keep a real delegate reference so we can unsubscribe correctly.
        private static UserPreferenceChangedEventHandler _userPreferenceChangedHandler;

        // ============================================================
        // WINDOWS THEME LISTENER
        // ============================================================

        public static void StartListening()
        {
            if (_subscribed)
                return;

            _subscribed = true;

            _userPreferenceChangedHandler = OnUserPreferenceChanged;
            SystemEvents.UserPreferenceChanged += _userPreferenceChangedHandler;
        }

        public static void StopListening()
        {
            if (!_subscribed)
                return;

            _subscribed = false;

            if (_userPreferenceChangedHandler != null)
            {
                SystemEvents.UserPreferenceChanged -= _userPreferenceChangedHandler;
                _userPreferenceChangedHandler = null;
            }
        }

        private static void OnUserPreferenceChanged(
            object sender,
            UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General ||
                e.Category == UserPreferenceCategory.Color)
            {
                ThemeChanged?.Invoke();
            }
        }

        // ============================================================
        // DETECT WINDOWS THEME
        // ============================================================

        /// <summary>
        /// Reads:
        /// HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
        /// AppsUseLightTheme
        ///
        /// 0 = Dark
        /// 1 = Light
        /// </summary>
        public static bool IsDarkTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AppsUseLightTheme");

                        if (value is int intValue)
                            return intValue == 0;

                        if (value is long longValue)
                            return longValue == 0;
                    }
                }
            }
            catch
            {
                // Ignore registry errors.
            }

            // Safe fallback.
            return false;
        }

        // ============================================================
        // LIGHT THEME COLORS
        // ============================================================

        public static readonly Color LightBack =
            SystemColors.Control;

        public static readonly Color LightBackAlt =
            SystemColors.Window;

        public static readonly Color LightFore =
            SystemColors.ControlText;

        public static readonly Color LightBorder =
            SystemColors.ControlDark;

        public static readonly Color LightControl =
            SystemColors.Control;

        public static readonly Color LightSelected =
            SystemColors.Highlight;

        public static readonly Color LightSelectedFore =
            SystemColors.HighlightText;

        // ============================================================
        // DARK THEME COLORS
        // ============================================================

        public static readonly Color DarkBack =
            Color.FromArgb(32, 32, 32);

        public static readonly Color DarkBackAlt =
            Color.FromArgb(24, 24, 24);

        public static readonly Color DarkControl =
            Color.FromArgb(45, 45, 48);

        public static readonly Color DarkControlHover =
            Color.FromArgb(55, 55, 58);

        public static readonly Color DarkFore =
            Color.FromArgb(241, 241, 241);

        public static readonly Color DarkForeSecondary =
            Color.FromArgb(190, 190, 190);

        public static readonly Color DarkBorder =
            Color.FromArgb(70, 70, 70);

        public static readonly Color DarkSelected =
            Color.FromArgb(62, 62, 66);

        public static readonly Color DarkSelectedFore =
            Color.White;

        // ============================================================
        // APPLY THEME
        // ============================================================

        /// <summary>
        /// Recursively applies the selected theme to a control and all
        /// descendant controls.
        /// </summary>
        public static void Apply(Control root, bool dark)
        {
            if (root == null)
                return;

            ApplyToControl(root, dark);

            foreach (Control child in root.Controls)
                Apply(child, dark);

            if (root is Form form && form.IsHandleCreated)
                ApplyDarkTitleBar(form.Handle, dark);
        }

        // ============================================================
        // APPLY TO INDIVIDUAL CONTROLS
        // ============================================================

        private static void ApplyToControl(Control control, bool dark)
        {
            if (control == null)
                return;

            switch (control)
            {
                // ----------------------------------------------------
                // TREE VIEW
                // ----------------------------------------------------

                case TreeView tree:
                    tree.BackColor = dark
                        ? DarkBackAlt
                        : LightBackAlt;

                    tree.ForeColor = dark
                        ? DarkFore
                        : LightFore;

                    tree.LineColor = dark
                        ? DarkBorder
                        : SystemColors.ControlDark;

                    tree.HideSelection = false;

                    ApplyNativeDarkMode(tree, dark);
                    break;

                // ----------------------------------------------------
                // LIST VIEW
                // ----------------------------------------------------

                case ListView listView:
                    listView.BackColor = dark
                        ? DarkBackAlt
                        : LightBackAlt;

                    listView.ForeColor = dark
                        ? DarkFore
                        : LightFore;

                    listView.BorderStyle = BorderStyle.FixedSingle;

                    ApplyNativeDarkMode(listView, dark);
                    break;

                // ----------------------------------------------------
                // TEXT BOX
                // ----------------------------------------------------

                case TextBox textBox:
                    textBox.BackColor = dark
                        ? DarkBackAlt
                        : LightBackAlt;

                    textBox.ForeColor = dark
                        ? DarkFore
                        : LightFore;

                    textBox.BorderStyle = BorderStyle.FixedSingle;

                    ApplyNativeDarkMode(textBox, dark);
                    break;

                // ----------------------------------------------------
                // COMBO BOX
                // ----------------------------------------------------

                case ComboBox comboBox:
                    comboBox.BackColor = dark
                        ? DarkControl
                        : LightBackAlt;

                    comboBox.ForeColor = dark
                        ? DarkFore
                        : LightFore;

                    comboBox.FlatStyle = FlatStyle.Flat;

                    // Required for our custom dark/light drawing.
                    comboBox.DrawMode = DrawMode.OwnerDrawFixed;

                    comboBox.DrawItem -= ComboBox_DrawItem;
                    comboBox.DrawItem += ComboBox_DrawItem;

                    comboBox.Tag = dark;

                    ApplyNativeDarkMode(comboBox, dark);

                    comboBox.Invalidate();
                    break;

                // ----------------------------------------------------
                // BUTTON
                // ----------------------------------------------------

                case Button button:
                    button.BackColor = dark
                        ? DarkControl
                        : LightControl;

                    button.ForeColor = dark
                        ? DarkFore
                        : LightFore;

                    button.FlatStyle = FlatStyle.Flat;

                    button.FlatAppearance.BorderColor = dark
                        ? DarkBorder
                        : LightBorder;

                    button.FlatAppearance.MouseOverBackColor = dark
                        ? DarkControlHover
                        : SystemColors.ControlLight;

                    button.FlatAppearance.MouseDownBackColor = dark
                        ? DarkSelected
                        : SystemColors.ControlDark;

                    break;

                // ----------------------------------------------------
                // CHECK BOX
                // ----------------------------------------------------

                case CheckBox checkBox:
                    checkBox.ForeColor = dark
                        ? DarkFore
                        : LightFore;

                    checkBox.BackColor = Color.Transparent;

                    checkBox.FlatStyle = FlatStyle.Flat;

                    checkBox.FlatAppearance.BorderColor = dark
                        ? DarkBorder
                        : LightBorder;

                    checkBox.FlatAppearance.MouseOverBackColor = dark
                        ? DarkControl
                        : SystemColors.ControlLight;

                    checkBox.FlatAppearance.MouseDownBackColor = dark
                        ? DarkSelected
                        : SystemColors.ControlDark;

                    break;

                // ----------------------------------------------------
                // TAB CONTROL
                // ----------------------------------------------------

                case TabControl tabControl:
                    tabControl.BackColor = dark
                        ? DarkBack
                        : LightBack;

                    tabControl.ForeColor = dark
                        ? DarkFore
                        : LightFore;

                    tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;

                    tabControl.DrawItem -= TabControl_DrawItem;
                    tabControl.DrawItem += TabControl_DrawItem;

                    tabControl.Tag = dark;

                    tabControl.Invalidate();

                    ApplyNativeDarkMode(tabControl, dark);
                    break;

                // ----------------------------------------------------
                // TAB PAGE
                // ----------------------------------------------------

                case TabPage tabPage:
                    tabPage.BackColor = dark
                        ? DarkBack
                        : LightBack;

                    tabPage.ForeColor = dark
                        ? DarkFore
                        : LightFore;

                    break;

                // ----------------------------------------------------
                // LABEL
                // ----------------------------------------------------

                case Label label:
                    label.BackColor = Color.Transparent;

                    // Preserve labels which have intentionally been given
                    // another color such as muted/gray text.
                    if (label.ForeColor == LightFore ||
                        label.ForeColor == DarkFore ||
                        label.ForeColor == SystemColors.ControlText)
                    {
                        label.ForeColor = dark
                            ? DarkFore
                            : LightFore;
                    }

                    break;

                // ----------------------------------------------------
                // FORM
                // ----------------------------------------------------

                case Form form:
                    form.BackColor = dark
                        ? DarkBack
                        : LightBack;

                    form.ForeColor = dark
                        ? DarkFore
                        : LightFore;

                    if (form.IsHandleCreated)
                        ApplyDarkTitleBar(form.Handle, dark);

                    break;

                // ----------------------------------------------------
                // DEFAULT
                // ----------------------------------------------------

                default:
                    control.BackColor = dark
                        ? DarkBack
                        : LightBack;

                    control.ForeColor = dark
                        ? DarkFore
                        : LightFore;

                    break;
            }
        }

        // ============================================================
        // COMBO BOX DRAWING
        // ============================================================

        private static void ComboBox_DrawItem(
            object sender,
            DrawItemEventArgs e)
        {
            var comboBox = sender as ComboBox;

            if (comboBox == null)
                return;

            bool dark = comboBox.Tag is bool b && b;

            if (e.Index < 0)
                return;

            bool selected =
                (e.State & DrawItemState.Selected) ==
                DrawItemState.Selected;

            Color backColor;
            Color foreColor;

            if (dark)
            {
                backColor = selected
                    ? DarkSelected
                    : DarkControl;

                foreColor = DarkFore;
            }
            else
            {
                backColor = selected
                    ? SystemColors.Highlight
                    : LightBackAlt;

                foreColor = selected
                    ? SystemColors.HighlightText
                    : LightFore;
            }

            using (var brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(
                    brush,
                    e.Bounds);
            }

            string text = comboBox.GetItemText(
                comboBox.Items[e.Index]);

            TextRenderer.DrawText(
                e.Graphics,
                text,
                comboBox.Font,
                e.Bounds,
                foreColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);

            e.DrawFocusRectangle();
        }

        // ============================================================
        // TAB CONTROL DRAWING
        // ============================================================

        private static void TabControl_DrawItem(
            object sender,
            DrawItemEventArgs e)
        {
            var tabControl = (TabControl)sender;

            bool dark =
                tabControl.Tag is bool b && b;

            if (e.Index < 0 ||
                e.Index >= tabControl.TabPages.Count)
                return;

            var tabRect =
                tabControl.GetTabRect(e.Index);

            bool selected =
                e.Index == tabControl.SelectedIndex;

            Color backColor;
            Color foreColor;

            if (dark)
            {
                backColor = selected
                    ? DarkControl
                    : DarkBack;

                foreColor = DarkFore;
            }
            else
            {
                backColor = selected
                    ? SystemColors.ControlLightLight
                    : SystemColors.Control;

                foreColor = LightFore;
            }

            using (var backBrush =
                new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(
                    backBrush,
                    tabRect);
            }

            TextRenderer.DrawText(
                e.Graphics,
                tabControl.TabPages[e.Index].Text,
                tabControl.Font,
                tabRect,
                foreColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
        }

        // ============================================================
        // WINDOWS NATIVE DARK MODE
        // ============================================================

        [DllImport(
            "dwmapi.dll",
            PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int value,
            int size);

        [DllImport(
            "uxtheme.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern int SetWindowTheme(
            IntPtr hwnd,
            string pszSubAppName,
            string pszSubIdList);

        // Windows 10 20H1 / Windows 11
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_NEW = 20;

        // Older Windows 10 builds
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        /// <summary>
        /// Applies Windows native dark mode to controls which expose
        /// a native HWND. This is best-effort and harmless on unsupported
        /// Windows versions.
        /// </summary>
        private static void ApplyNativeDarkMode(
            Control control,
            bool dark)
        {
            if (control == null)
                return;

            try
            {
                if (!control.IsHandleCreated)
                    return;

                IntPtr hwnd = control.Handle;

                // Enable Windows native dark theme for common controls.
                SetWindowTheme(
                    hwnd,
                    dark ? "DarkMode_Explorer" : null,
                    null);

                int useDark = dark ? 1 : 0;

                int result = DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_USE_IMMERSIVE_DARK_MODE_NEW,
                    ref useDark,
                    sizeof(int));

                if (result != 0)
                {
                    DwmSetWindowAttribute(
                        hwnd,
                        DWMWA_USE_IMMERSIVE_DARK_MODE_OLD,
                        ref useDark,
                        sizeof(int));
                }

                control.Invalidate();
                control.Update();
            }
            catch
            {
                // Native dark mode is best-effort.
            }
        }

        // ============================================================
        // FORM TITLE BAR
        // ============================================================

        private static void ApplyDarkTitleBar(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero)
                return;

            try
            {
                // Windows 10/11 immersive dark title bar
                int useDark = dark ? 1 : 0;

                int result = DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_USE_IMMERSIVE_DARK_MODE_NEW,
                    ref useDark,
                    sizeof(int));

                if (result != 0)
                {
                    DwmSetWindowAttribute(
                        hwnd,
                        DWMWA_USE_IMMERSIVE_DARK_MODE_OLD,
                        ref useDark,
                        sizeof(int));
                }

                // Explicitly set caption background/text colors.
                // This fixes cases where Windows keeps the title bar white.
                int captionColor = dark
                    ? ColorTranslator.ToWin32(Color.FromArgb(32, 32, 32))
                    : ColorTranslator.ToWin32(SystemColors.Control);

                int textColor = dark
                    ? ColorTranslator.ToWin32(Color.FromArgb(241, 241, 241))
                    : ColorTranslator.ToWin32(SystemColors.ControlText);

                DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_CAPTION_COLOR,
                    ref captionColor,
                    sizeof(int));

                DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_TEXT_COLOR,
                    ref textColor,
                    sizeof(int));
            }
            catch
            {
                // Best effort only.
            }
        }
    }
}