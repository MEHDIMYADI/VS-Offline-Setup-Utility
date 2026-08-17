// Copyright (c) 2026 MEHDIMYADI (https://github.com/MEHDIMYADI/)
// Licensed under the MIT License. See LICENSE file in the project root.
//

using System.Collections.Generic;

namespace VSOfflineTool
{
    internal static class VsEditionCatalog
    {
        private const string DocsBase =
            "https://raw.githubusercontent.com/MicrosoftDocs/visualstudio-docs/master/docs/install/includes";

        public static List<VsEdition> GetAll()
        {
            return new List<VsEdition>
            {
                Edition("Visual Studio 2019 Community",    "16", "community",   "vs-2019"),
                Edition("Visual Studio 2019 Professional",  "16", "professional", "vs-2019"),
                Edition("Visual Studio 2019 Enterprise",    "16", "enterprise",   "vs-2019"),

                Edition("Visual Studio 2022 Community",    "17", "community",   "vs-2022"),
                Edition("Visual Studio 2022 Professional",  "17", "professional", "vs-2022"),
                Edition("Visual Studio 2022 Enterprise",    "17", "enterprise",   "vs-2022"),

                EditionStable("Visual Studio 2026 Community",    "community",   "vs-2026"),
                EditionStable("Visual Studio 2026 Professional",  "professional", "vs-2026"),
                EditionStable("Visual Studio 2026 Enterprise",    "enterprise",   "vs-2026")
            };
        }

        private static VsEdition Edition(string name, string channel, string edition, string docFolder)
        {
            return new VsEdition
            {
                Name = name,
                SetupUri = $"https://aka.ms/vs/{channel}/release/vs_{edition}.exe",
                WorkloadMarkdownUri = $"{DocsBase}/{docFolder}/workload-component-id-vs-{edition}.md"
            };
        }

        private static VsEdition EditionStable(string name, string edition, string docFolder)
        {
            return new VsEdition
            {
                Name = name,
                SetupUri = $"https://aka.ms/vs/stable/vs_{edition}.exe",
                WorkloadMarkdownUri = $"{DocsBase}/{docFolder}/workload-component-id-vs-{edition}.md"
            };
        }
    }
}
