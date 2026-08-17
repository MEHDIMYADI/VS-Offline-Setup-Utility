// Copyright (c) 2026 MEHDIMYADI (https://github.com/MEHDIMYADI/)
// Licensed under the MIT License. See LICENSE file in the project root.
//

using System;
using System.IO;
using System.Text;

namespace VSOfflineTool
{
    internal sealed class AppSettings
    {
        public string OfflineLayoutPath { get; set; } = "";
        public string SelectedEdition { get; set; } = "";
        public string SelectedLanguage { get; set; } = "en-US";
        public bool IncludeRecommended { get; set; }
        public bool IncludeOptional { get; set; }
    }

    /// <summary>
    /// Loads/saves AppSettings as a plain "Key=Value" text file.
    /// NOTE: this intentionally does NOT use System.Text.Json - that type is
    /// not part of the in-box .NET Framework 4.8.1 BCL and would silently
    /// require the System.Text.Json NuGet package, which defeats the whole
    /// "zero NuGet dependencies" goal of this project.
    /// </summary>
    internal static class SettingsStore
    {
        private static readonly string SettingsFile =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");

        public static AppSettings Load()
        {
            var settings = new AppSettings();

            try
            {
                if (!File.Exists(SettingsFile))
                    return settings;

                foreach (var line in File.ReadAllLines(SettingsFile))
                {
                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                        continue;

                    var key = line.Substring(0, separatorIndex).Trim();
                    var value = line.Substring(separatorIndex + 1).Trim();

                    switch (key)
                    {
                        case nameof(AppSettings.OfflineLayoutPath):
                            settings.OfflineLayoutPath = value;
                            break;
                        case nameof(AppSettings.SelectedEdition):
                            settings.SelectedEdition = value;
                            break;
                        case nameof(AppSettings.SelectedLanguage):
                            settings.SelectedLanguage = string.IsNullOrEmpty(value) ? "en-US" : value;
                            break;
                        case nameof(AppSettings.IncludeRecommended):
                            settings.IncludeRecommended = value == "1";
                            break;
                        case nameof(AppSettings.IncludeOptional):
                            settings.IncludeOptional = value == "1";
                            break;
                    }
                }
            }
            catch
            {
                // Corrupt or unreadable settings file - fall back to defaults.
                return new AppSettings();
            }

            return settings;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{nameof(AppSettings.OfflineLayoutPath)}={settings.OfflineLayoutPath}");
                sb.AppendLine($"{nameof(AppSettings.SelectedEdition)}={settings.SelectedEdition}");
                sb.AppendLine($"{nameof(AppSettings.SelectedLanguage)}={settings.SelectedLanguage}");
                sb.AppendLine($"{nameof(AppSettings.IncludeRecommended)}={(settings.IncludeRecommended ? "1" : "0")}");
                sb.AppendLine($"{nameof(AppSettings.IncludeOptional)}={(settings.IncludeOptional ? "1" : "0")}");

                File.WriteAllText(SettingsFile, sb.ToString());
            }
            catch
            {
                // Settings persistence must never prevent the application from running.
            }
        }
    }
}
