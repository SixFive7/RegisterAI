// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Security.Principal;
using System.Text;
using RegisterAI.Tests.Harness;

namespace RegisterAI.Tests;

/// <summary>
/// No tracked file, commit or published executable carries a detail of the machine it
/// was built on or of the account that built it. The scan is
/// <c>build\Find-MachineDetails.ps1</c>, the same one the pre-commit hook runs.
/// </summary>
/// <remarks>
/// Every planted value below is composed at run time from this account and this
/// machine, or assembled from pieces, so this file never carries what it plants.
/// </remarks>
internal sealed class MachineDetailsTests
{
    /// <summary>Every rule the script carries, by the name it reports.</summary>
    private static readonly string[] EveryRule =
        ["profile-path", "user-name", "machine-name", "account-sid", "mail-address", "drive-path"];

    /// <summary>The files a commit made now would hold carry no machine detail.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheTreeCarriesNoMachineDetail()
    {
        var scan = await ScanAsync(RepositoryTree.Root, "-WorkingTree");

        await Assert.That(scan.Output).IsEmpty();
        await Assert.That(scan.ExitCode).IsEqualTo(0);
    }

    /// <summary>No commit, message or identity in the history carries one.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheHistoryCarriesNoMachineDetail()
    {
        var scan = await ScanAsync(RepositoryTree.Root, "-History");

        await Assert.That(scan.Output).IsEmpty();
        await Assert.That(scan.ExitCode).IsEqualTo(0);
    }

    /// <summary>
    /// The published executable carries none either, and in particular not the folder
    /// it was built in.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ThePublishedExecutableCarriesNoMachineDetail()
    {
        var executable = Published.Require();
        var scan = await ScanAsync(RepositoryTree.Root, "-Path", executable);

        await Assert.That(scan.Output).IsEmpty();
        await Assert.That(scan.ExitCode).IsEqualTo(0);

        // The control: the debug directory is there and readable, and it names the
        // debug file without the folder it was written to.
        var text = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(executable));

        await Assert.That(text).Contains("RegisterAI.pdb");
        await Assert.That(text).DoesNotContain(RepositoryTree.Root);
    }

    /// <summary>
    /// Each rule finds what it is for, in the index, the working tree, the history and
    /// a binary file, and the allowed spellings pass.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task EveryRuleFindsWhatItIsForAndLetsTheAllowedSpellingsPass()
    {
        using var scratch = Scratch.Create("details");
        var repository = scratch.In("repository");

        _ = Directory.CreateDirectory(repository);
        await GitAsync(repository, "init", "-q");

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var planted = string.Join(
            '\n',
            [
                "profile: " + profile,
                "profile as JSON: " + profile.Replace("\\", "\\\\", StringComparison.Ordinal),
                "profile with slashes: " + profile.Replace('\\', '/'),
                "user: /home/" + Environment.UserName + "/x",
                "machine: built on " + Environment.MachineName + " today",
                "account: " + WindowsIdentity.GetCurrent().User!.Value,
                "address: someone" + "@" + "contoso.com",
                "path: " + "Q" + ":\\" + "Private\\notes.txt",
            ]);

        var allowed = string.Join(
            '\n',
            [
                @"C:\Apps\Demo\demo-mcp.exe",
                @"C:\\Tools\\codex.exe",
                @"C:\Users\<you>\.claude.json",
                "D:/Other/demo-mcp.exe",
                "someone" + "@" + "example.com",
                "Copyright 2026 " + Environment.UserName + " Somebody",
            ]);

        await File.WriteAllTextAsync(Path.Combine(repository, "planted.txt"), planted + "\n");
        await File.WriteAllTextAsync(Path.Combine(repository, "allowed.txt"), allowed + "\n");

        // The working tree, before anything is staged.
        var workingTree = await ScanAsync(repository, "-WorkingTree");

        await Assert.That(workingTree.ExitCode).IsEqualTo(1);
        await Assert.That(workingTree.Output).DoesNotContain("allowed.txt");

        foreach (var rule in EveryRule)
        {
            await Assert.That(workingTree.Output).Contains($": {rule}: ");
        }

        // The index, which is what the hook reads.
        await GitAsync(repository, "add", "planted.txt", "allowed.txt");

        var index = await ScanAsync(repository);

        await Assert.That(index.ExitCode).IsEqualTo(1);
        await Assert.That(index.Output).Contains("planted.txt:1");
        await Assert.That(index.Output).DoesNotContain("allowed.txt");

        // The history: committed, then removed, so only the history still holds it.
        await GitAsync(repository, "commit", "-q", "-m", "planted");
        await GitAsync(repository, "rm", "-q", "planted.txt");
        await GitAsync(repository, "commit", "-q", "-m", "removed");

        await Assert.That((await ScanAsync(repository)).ExitCode).IsEqualTo(0);

        var history = await ScanAsync(repository, "-History");

        await Assert.That(history.ExitCode).IsEqualTo(1);
        await Assert.That(history.Output).Contains("planted.txt");

        // A binary file, with the profile path in UTF-16LE as a native string table holds it.
        var binary = Path.Combine(scratch.Folder, "planted.bin");

        await File.WriteAllBytesAsync(binary, [0, 1, 2, .. Encoding.Unicode.GetBytes(profile), 0, 0, 3]);

        var file = await ScanAsync(repository, "-Path", binary);

        await Assert.That(file.ExitCode).IsEqualTo(1);
        await Assert.That(file.Output).Contains(": profile-path: ");

        // Two shapes the published executable carries that are neither an address nor
        // a path: a dotted runtime setting after an '@', and a drive-like pair of bytes.
        var noise = Path.Combine(scratch.Folder, "noise.bin");

        await File.WriteAllBytesAsync(noise, [0, .. Encoding.ASCII.GetBytes("Y@System.GC.HeapAffinitizeRanges"), 0, .. Encoding.ASCII.GetBytes("(p:/2)"), 0]);

        var quiet = await ScanAsync(repository, "-Path", noise);

        await Assert.That(quiet.Output).IsEmpty();
        await Assert.That(quiet.ExitCode).IsEqualTo(0);
    }

    private static Task<ChildResult> ScanAsync(string repository, params string[] arguments) =>
        Child.RunAsync(
            "pwsh",
            ["-NoProfile", "-NonInteractive", "-File", Path.Combine(RepositoryTree.Root, "build", "Find-MachineDetails.ps1"), "-Repository", repository, .. arguments],
            TimeSpan.FromMinutes(2));

    /// <summary>Runs git in a scratch repository, as a fixed test identity.</summary>
    private static async Task GitAsync(string repository, params string[] arguments)
    {
        var result = await Child.RunAsync(
            "git",
            ["-C", repository, "-c", "user.name=RegisterAI Tests", "-c", "user.email=tests@example.com", .. arguments],
            TimeSpan.FromMinutes(1));

        if (result.ExitCode is not 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} exited {result.ExitCode}: {result.Error}");
        }
    }
}
