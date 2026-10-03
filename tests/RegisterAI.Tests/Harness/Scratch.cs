// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Globalization;

namespace RegisterAI.Tests.Harness;

/// <summary>
/// A folder under the repository's <c>.work\tests</c>, removed when the test is done.
/// </summary>
/// <remarks>
/// Nothing a test writes lands anywhere else: not the profile, not the temp
/// folder, and never a client's real configuration.
/// </remarks>
internal sealed class Scratch : IDisposable
{
    private Scratch(string folder) => Folder = folder;

    /// <summary>The folder, which exists and is empty when it is handed out.</summary>
    public string Folder { get; }

    /// <summary>Creates a fresh folder.</summary>
    /// <param name="label">A word that says which test made it.</param>
    /// <returns>The scratch folder.</returns>
    public static Scratch Create(string label)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"{label}-{Guid.NewGuid():N}")[..(label.Length + 9)];
        var folder = Path.Combine(RepositoryTree.Root, ".work", "tests", name);

        _ = Directory.CreateDirectory(folder);

        return new Scratch(folder);
    }

    /// <summary>A path inside the folder.</summary>
    /// <param name="parts">The path's parts below the folder.</param>
    /// <returns>The absolute path.</returns>
    public string In(params string[] parts) => Path.Combine([Folder, .. parts]);

    /// <inheritdoc/>
    public void Dispose() => Remove(new DirectoryInfo(Folder));

    /// <summary>
    /// Removes a tree one entry at a time, deepest first, and unlinks a junction or
    /// a directory link without entering it.
    /// </summary>
    private static void Remove(DirectoryInfo directory)
    {
        if (!directory.Exists)
        {
            return;
        }

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child && !child.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Remove(child);
                continue;
            }

            // Git marks its object files read-only.
            entry.Attributes &= ~FileAttributes.ReadOnly;
            entry.Delete();
        }

        directory.Delete();
    }
}
