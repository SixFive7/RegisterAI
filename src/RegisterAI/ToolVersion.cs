// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Reflection;

namespace RegisterAI;

/// <summary>The tool's version, as the build stamped it.</summary>
internal static class ToolVersion
{
    /// <summary>
    /// The informational version MinVer derived from the nearest tag, for example
    /// <c>0.1.0</c>, or <c>0.1.1-alpha.0.3</c> three commits after it.
    /// </summary>
    public static string Text { get; } =
        typeof(ToolVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}
