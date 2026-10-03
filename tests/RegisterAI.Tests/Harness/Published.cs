// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

namespace RegisterAI.Tests.Harness;

/// <summary>The NativeAOT executable <c>build\Publish.ps1</c> writes, which is what ships.</summary>
internal static class Published
{
    /// <summary>Where the executable is published.</summary>
    public static string Executable { get; } = Path.Combine(RepositoryTree.Root, "artifacts", "publish", "RegisterAI.exe");

    /// <summary>
    /// The executable, refused when it is missing or older than a source file,
    /// because a test of a stale binary says nothing about the code in the tree.
    /// </summary>
    /// <returns>Its path.</returns>
    public static string Require()
    {
        if (!File.Exists(Executable))
        {
            throw new InvalidOperationException(
                $"'{Executable}' does not exist. Run: pwsh build/Publish.ps1");
        }

        var built = File.GetLastWriteTimeUtc(Executable);
        var source = Path.Combine(RepositoryTree.Root, "src", "RegisterAI");
        var newer = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(source, file))
            .Where(file => File.GetLastWriteTimeUtc(file) > built)
            .ToList();

        return newer.Count is 0
            ? Executable
            : throw new InvalidOperationException(
                $"'{Executable}' is older than {newer.Count} source file(s), the first being '{newer[0]}'. Run: pwsh build/Publish.ps1");
    }

    private static bool IsBuildOutput(string source, string file)
    {
        var first = Path.GetRelativePath(source, file).Split(Path.DirectorySeparatorChar)[0];

        return first is "bin" or "obj";
    }
}
