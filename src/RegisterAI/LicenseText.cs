// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

namespace RegisterAI;

/// <summary>
/// The repository's LICENSE file, compiled into the executable, because a licence file
/// beside it would be a support file.
/// </summary>
internal static class LicenseText
{
    /// <summary>The licence terms.</summary>
    /// <returns>The text, as the repository holds it.</returns>
    public static string Read()
    {
        using var stream = typeof(LicenseText).Assembly.GetManifestResourceStream("LICENSE")
            ?? throw new InvalidOperationException("The licence text was not compiled into this build.");
        using var reader = new StreamReader(stream, Terminal.Utf8);

        return reader.ReadToEnd();
    }
}
