// Copyright (c) 2026 MEHDIMYADI (https://github.com/MEHDIMYADI/)
// Licensed under the MIT License. See LICENSE file in the project root.
//

using System;
using System.Collections.Generic;
using System.Linq;

namespace VSOfflineTool
{
    internal enum ComponentDependency
    {
        Independent = 0,
        Required = 1,
        Recommended = 2,
        Optional = 3
    }

    internal class VsEdition
    {
        public string Name { get; set; }
        public string SetupUri { get; set; }
        public string WorkloadMarkdownUri { get; set; }

        public override string ToString() => Name;
    }

    internal class Workload
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        // Explicit selection made by the user (checking the workload's own box).
        public bool IsSelfSelected { get; set; }

        public List<Component> Components { get; } = new List<Component>();

        // Kept for compatibility with earlier code / the original naming.
        public bool IsSelected
        {
            get => IsSelfSelected;
            set => IsSelfSelected = value;
        }

        // True when at least one child component was manually self-selected
        // while the workload itself is NOT checked. Drives the indeterminate
        // (partial) tri-state glyph on the workload node.
        public bool HasAnyExplicitComponentSelection =>
            Components.Any(c => c.IsSelfSelected);

        public override string ToString() => Name;
    }

    internal class Component
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }

        public ComponentDependency Dependency { get; set; } = ComponentDependency.Independent;

        public Workload ParentWorkload { get; set; }

        // User explicitly clicked this component's own checkbox.
        public bool IsSelfSelected { get; set; }

        /// <summary>
        /// Mirrors the original project's Component.IsSelectedInWorkload():
        /// whether this component is implicitly included because its parent
        /// workload is selected, based on its dependency kind.
        /// Independent components are NEVER implied by the workload - they
        /// only ever come from direct user selection.
        /// </summary>
        public bool IsImpliedByWorkload
        {
            get
            {
                if (ParentWorkload == null)
                    return false;

                switch (Dependency)
                {
                    case ComponentDependency.Required:
                        return ParentWorkload.IsSelected;

                    case ComponentDependency.Recommended:
                        return ParentWorkload.IsSelected && ComponentSettings.IsRecommended;

                    case ComponentDependency.Optional:
                        return ParentWorkload.IsSelected && ComponentSettings.IsOptional;

                    case ComponentDependency.Independent:
                    default:
                        return false;
                }
            }
        }

        // Effective selection = explicit choice OR implied by the workload.
        public bool IsSelected => IsSelfSelected || IsImpliedByWorkload;

        /// <summary>
        /// Same formula as the original project: !(!IsSelfSelected &amp; IsSelectedInWorkload()).
        /// A component becomes non-interactive (greyed out) only when it is
        /// selected purely because the workload implies it, and the user has
        /// not explicitly picked it themselves - matching "you can't uncheck
        /// something that's automatically included."
        /// </summary>
        public bool IsSelectable => !(!IsSelfSelected && IsImpliedByWorkload);

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

        public string FullName
        {
            get
            {
                var dependencyText = Dependency switch
                {
                    ComponentDependency.Required => "Required",
                    ComponentDependency.Recommended => "Recommended",
                    ComponentDependency.Optional => "Optional",
                    _ => "Independent"
                };

                return $"{DisplayName} - {dependencyText}";
            }
        }

        public override string ToString() => DisplayName;
    }

    internal static class ComponentSettings
    {
        public static bool IsRecommended { get; set; }
        public static bool IsOptional { get; set; }
        public static string Language { get; set; } = "en-US";
    }

    internal class VsModule
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string FullPath { get; set; }

        public bool IsArchive => Name.Equals("Archive", StringComparison.OrdinalIgnoreCase);

        public override string ToString() => Name;
    }
}
