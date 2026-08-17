// Copyright (c) 2026 MEHDIMYADI (https://github.com/MEHDIMYADI/)
// Licensed under the MIT License. See LICENSE file in the project root.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VSOfflineTool
{
    /// <summary>
    /// Finds module folders inside an offline setup layout that are superseded
    /// by a newer version of the same module.
    ///
    /// Modern VS offline layouts can contain names such as:
    ///
    /// Android.Manifest-10.0.100.36.1.2,version=36.1.2,machinearch=x64
    ///
    /// The version=... segment is used for version comparison.
    /// Other segments remain part of the grouping key so architecture variants
    /// are not incorrectly mixed together.
    /// </summary>
    internal static class CleanupHelper
    {
        public static List<VsModule> FindOldVersionFolders(string layoutRoot)
        {
            var result = new List<VsModule>();

            if (string.IsNullOrWhiteSpace(layoutRoot))
                return result;

            var root = new DirectoryInfo(layoutRoot);

            if (!root.Exists)
                return result;

            DirectoryInfo archive = null;

            var modules = new List<VsModule>();

            foreach (var subDirectory in root.GetDirectories())
            {
                if (subDirectory.Name.Equals("Archive", StringComparison.OrdinalIgnoreCase))
                {
                    archive = subDirectory;
                    continue;
                }

                var parsed = ParseModuleDirectory(subDirectory);

                if (parsed != null)
                    modules.Add(parsed);
            }

            foreach (var group in modules.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                var versioned = group
                    .Where(m => TryParseVersion(m.Version, out _))
                    .ToList();

                if (versioned.Count < 2)
                    continue;

                var ordered = versioned
                    .Select(m =>
                    {
                        TryParseVersion(m.Version, out var version);
                        return new { Module = m, Version = version };
                    })
                    .OrderByDescending(x => x.Version)
                    .ToList();

                // Everything except the newest valid version is old.
                result.AddRange(ordered.Skip(1).Select(x => x.Module));
            }

            if (archive != null)
            {
                result.Add(new VsModule
                {
                    Name = archive.Name,
                    Version = "",
                    FullPath = archive.FullName
                });
            }

            return result;
        }

        private static VsModule ParseModuleDirectory(DirectoryInfo directory)
        {
            var segments = directory.Name.Split(',');

            if (segments.Length < 2)
                return null;

            string version = null;
            var groupKey = segments[0];

            for (int i = 1; i < segments.Length; i++)
            {
                var segment = segments[i].Trim();

                if (version == null && segment.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
                    version = segment.Substring("version=".Length);
                else
                    groupKey += "," + segment;
            }

            if (string.IsNullOrWhiteSpace(version))
                return null;

            return new VsModule
            {
                Name = groupKey,
                Version = version,
                FullPath = directory.FullName
            };
        }

        private static bool TryParseVersion(string text, out Version version)
        {
            version = null;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Version.TryParse(text, out version);
        }

        public static void DeleteFolders(IEnumerable<VsModule> folders)
        {
            foreach (var folder in folders ?? Enumerable.Empty<VsModule>())
            {
                if (folder == null || string.IsNullOrWhiteSpace(folder.FullPath))
                    continue;

                if (!Directory.Exists(folder.FullPath))
                    continue;

                Directory.Delete(folder.FullPath, true);
            }
        }
    }
}
