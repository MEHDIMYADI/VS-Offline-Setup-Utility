// Copyright (c) 2026 MEHDIMYADI (https://github.com/MEHDIMYADI/)
// Licensed under the MIT License. See LICENSE file in the project root.
//

using System;
using System.Collections.Generic;
using System.Linq;

namespace VSOfflineTool
{
    /// <summary>
    /// Parses Microsoft's workload/component Markdown tables.
    ///
    /// The normal Microsoft table is:
    ///
    /// | Component ID | Component Name |
    /// | --- | --- |
    ///
    /// If a third column contains dependency information, it is also recognized.
    /// </summary>
    internal static class MarkdownWorkloadParser
    {
        public static List<Workload> Parse(string markdown)
        {
            var workloads = new List<Workload>();

            if (string.IsNullOrWhiteSpace(markdown))
                return workloads;

            var lines = markdown.Replace("\r", "").Split('\n');

            Workload current = null;
            int i = 0;

            while (i < lines.Length)
            {
                var line = lines[i].Trim();

                if (!line.StartsWith("## ", StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }

                var title = line.Substring(3).Trim();

                if (title.Equals("Get support", StringComparison.OrdinalIgnoreCase))
                    break;

                current = new Workload { Name = title };
                i++;

                // Optional metadata directly after the heading.
                while (i < lines.Length)
                {
                    var metadataLine = lines[i].Trim();

                    if (metadataLine.StartsWith("**ID:**", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Id = metadataLine.Substring("**ID:**".Length).Trim();
                        i++;
                        continue;
                    }

                    if (metadataLine.StartsWith("**Description:**", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Description = metadataLine.Substring("**Description:**".Length).Trim();
                        i++;
                        continue;
                    }

                    break;
                }

                // Find the Markdown table.
                while (i < lines.Length && !IsTableSeparator(lines[i]))
                {
                    if (lines[i].TrimStart().StartsWith("## ", StringComparison.Ordinal))
                        break;

                    i++;
                }

                if (i < lines.Length && IsTableSeparator(lines[i]))
                    i++;

                // Read table rows until next workload.
                while (i < lines.Length)
                {
                    var row = lines[i].Trim();

                    if (row.StartsWith("## ", StringComparison.Ordinal))
                        break;

                    if (row.Length == 0)
                    {
                        i++;
                        continue;
                    }

                    ParseComponentRow(current, row);
                    i++;
                }

                if (!string.IsNullOrWhiteSpace(current.Id) || current.Components.Count > 0)
                {
                    foreach (var component in current.Components)
                        component.ParentWorkload = current;

                    workloads.Add(current);
                }
            }

            return workloads;
        }

        private static bool IsTableSeparator(string line)
        {
            var trimmed = line.Trim();

            if (!trimmed.Contains("|"))
                return false;

            var cells = trimmed.Trim('|').Split('|').Select(x => x.Trim());

            return cells.Any(cell =>
                cell.Length >= 3 &&
                cell.All(c => c == '-' || c == ':' || char.IsWhiteSpace(c)));
        }

        private static void ParseComponentRow(Workload workload, string row)
        {
            if (!row.Contains("|"))
                return;

            var cells = row.Trim('|').Split('|').Select(x => CleanMarkdownCell(x.Trim())).ToArray();

            if (cells.Length < 2)
                return;

            var id = cells[0];
            var name = cells[1];

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                return;

            // Do not accidentally parse normal Markdown text as a component.
            if (id.StartsWith("#") || id.StartsWith("-") ||
                id.Equals("Component ID", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var dependency = ComponentDependency.Independent;

            if (cells.Length >= 3)
                dependency = ParseDependency(cells[2]);

            var component = new Component
            {
                Id = id,
                Name = name,
                Dependency = dependency,
                ParentWorkload = workload
            };

            workload.Components.Add(component);
        }

        private static string CleanMarkdownCell(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var result = value.Trim();

            if (result.StartsWith("[") && result.Contains("]("))
            {
                var closingBracket = result.IndexOf(']');

                if (closingBracket > 0)
                    result = result.Substring(1, closingBracket - 1);
            }

            return result;
        }

        private static ComponentDependency ParseDependency(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return ComponentDependency.Independent;

            if (text.IndexOf("required", StringComparison.OrdinalIgnoreCase) >= 0)
                return ComponentDependency.Required;

            if (text.IndexOf("recommended", StringComparison.OrdinalIgnoreCase) >= 0)
                return ComponentDependency.Recommended;

            if (text.IndexOf("optional", StringComparison.OrdinalIgnoreCase) >= 0)
                return ComponentDependency.Optional;

            return ComponentDependency.Independent;
        }
    }
}
