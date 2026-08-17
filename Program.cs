// Copyright (c) 2026 MEHDIMYADI (https://github.com/MEHDIMYADI/)
// Licensed under the MIT License. See LICENSE file in the project root.
//

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VSOfflineTool
{
    internal static class Program
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(
            string AppID);

        [STAThread]
        private static void Main()
        {
            SetCurrentProcessExplicitAppUserModelID(
                "MEHDIMYADI.VSOfflineTool");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new MainForm());
        }
    }
}