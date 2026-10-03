// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

namespace RegisterAI.Tests.Harness;

/// <summary>The repository this suite was built from, read as files.</summary>
internal static class RepositoryTree
{
    /// <summary>The folder holding <c>RegisterAI.slnx</c>.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>
    /// Every file a commit made now would hold: tracked files, and untracked files
    /// that no ignore rule covers. Relative, with forward slashes.
    /// </summary>
    /// <returns>The files that exist on disk.</returns>
    public static async Task<IReadOnlyList<string>> FilesAsync()
    {
        var listed = await Child.RunAsync(
            "git",
            ["-C", Root, "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
            TimeSpan.FromSeconds(60));

        if (listed.ExitCode is not 0)
        {
            throw new InvalidOperationException($"git ls-files exited {listed.ExitCode}: {listed.Error}");
        }

        return [.. listed.Output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .Where(file => File.Exists(FullPath(file)))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>A repository-relative path made absolute.</summary>
    /// <param name="relative">The path, with forward slashes.</param>
    /// <returns>The absolute path.</returns>
    public static string FullPath(string relative) =>
        Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A file's text, or <see langword="null"/> when it holds a NUL byte and so is binary.</summary>
    /// <param name="relative">The path, with forward slashes.</param>
    /// <returns>The text.</returns>
    public static async Task<string?> ReadTextAsync(string relative)
    {
        var bytes = await File.ReadAllBytesAsync(FullPath(relative));

        return bytes.Contains((byte)0) ? null : Child.Utf8.GetString(bytes);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RegisterAI.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No folder above '{AppContext.BaseDirectory}' holds RegisterAI.slnx.");
    }
}
