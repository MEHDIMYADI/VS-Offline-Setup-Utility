// Copyright (c) 2026 MEHDIMYADI (https://github.com/MEHDIMYADI/)
// Licensed under the MIT License. See LICENSE file in the project root.
//

using System;
using System.IO;
using System.Text;

namespace VSOfflineTool
{
    internal sealed class LayoutAnalyzer
    {
        public string FindMinimalLayoutExe()
        {
            string programFilesX86 =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86);

            string candidate = Path.Combine(
                programFilesX86,
                "Microsoft Visual Studio",
                "MinimalLayout",
                "MinimalLayout.exe");

            if (File.Exists(candidate))
                return candidate;

            string programFiles =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles);

            candidate = Path.Combine(
                programFiles,
                "Microsoft Visual Studio",
                "MinimalLayout",
                "MinimalLayout.exe");

            if (File.Exists(candidate))
                return candidate;

            return null;
        }

        /// <summary>
        /// Builds the quoted "MinimalLayout.exe preview ..." command line,
        /// without running anything. The caller runs this in a normal, visible
        /// cmd.exe window so the user reads MinimalLayout's own summary
        /// directly - no output capture, no hidden console tricks.
        /// </summary>
        public string BuildPreviewCommandLine(
            string targetLocation,
            string productId,
            string baseVersion,
            string targetVersion,
            string language,
            string[] components,
            bool includeRecommended,
            bool includeOptional,
            out string error, 
            out string downloadUrl)
        {
            error = null;
            downloadUrl = null;

            string exe = FindMinimalLayoutExe();

            if (string.IsNullOrWhiteSpace(exe))
            {
                error =
                    "MinimalLayout.exe was not found.\r\n\r\n" +
                    "Install Microsoft's Visual Studio Minimal Layout Tool " +
                    "to use update preview.";

                downloadUrl = "https://aka.ms/vs/installer/minimallayout";

                return null;
            }

            var args = new StringBuilder();

            args.Append("preview ");
            args.Append("--targetLocation ").Append(Quote(targetLocation)).Append(" ");
            args.Append("--productIds ").Append(Quote(productId)).Append(" ");
            args.Append("--baseVersion ").Append(Quote(baseVersion)).Append(" ");
            args.Append("--targetVersion ").Append(Quote(targetVersion)).Append(" ");
            args.Append("--languages ").Append(Quote(language)).Append(" ");

            if (components != null)
            {
                foreach (string component in components)
                {
                    if (string.IsNullOrWhiteSpace(component))
                        continue;

                    args.Append("--add ").Append(Quote(component)).Append(" ");
                }
            }

            if (includeRecommended)
                args.Append("--includeRecommended ");

            if (includeOptional)
                args.Append("--includeOptional ");

            return Quote(exe) + " " + args.ToString().Trim();
        }

        private static string Quote(
            string value)
        {
            if (value == null)
                return "\"\"";

            return "\"" +
                   value.Replace("\"", "\\\"") +
                   "\"";
        }
    }
}