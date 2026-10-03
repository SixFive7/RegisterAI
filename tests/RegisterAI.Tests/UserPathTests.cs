// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using Microsoft.Win32;
using RegisterAI.Tests.Harness;

namespace RegisterAI.Tests;

/// <summary>
/// <c>path add</c> and <c>path remove</c>, and what status reports about the user PATH,
/// in process against a PATH kept in memory or under a scratch registry key. Nothing
/// here reads or writes the person's own PATH.
/// </summary>
internal sealed class UserPathTests
{
    /// <summary>Two entries a person already had, as a user PATH carries them.</summary>
    private const string Theirs = @"C:\Tools\bin;%USERPROFILE%\bin";

    /// <summary>A folder that is not on any PATH a test builds.</summary>
    private const string Folder = @"C:\Apps\Demo";

    /// <summary>
    /// path add appends the folder after a separator, keeps the value's kind, announces
    /// the change once and reads it back; a second add finds it there and writes nothing.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AnAddAppendsTheFolderOnceKeepsTheKindAndAnnouncesIt()
    {
        var store = new ScratchUserPath { Value = new UserPathValue(Theirs, RegistryValueKind.ExpandString) };

        var added = UserPath.Add(store, Folder, dryRun: false, NoVariables);

        await Assert.That(added.Action).IsEqualTo(Change.Added);
        await Assert.That(added.Announced).IsTrue();
        await Assert.That(added.Error).IsNull();
        await Assert.That(added.ExitCode).IsEqualTo(0);
        await Assert.That(store.Value).IsEqualTo(new UserPathValue(Theirs + ";" + Folder, RegistryValueKind.ExpandString));
        await Assert.That(store.Writes).IsEqualTo(1);
        await Assert.That(store.Announcements).IsEqualTo(1);

        // Again, and in another spelling of the same folder: there already, so nothing.
        foreach (var spelling in new[] { Folder, @"c:\apps\demo\", @" C:\Apps\Demo " })
        {
            var again = UserPath.Add(store, spelling, dryRun: false, NoVariables);

            await Assert.That(again.Action).IsEqualTo(Change.None);
            await Assert.That(again.Announced).IsNull();
        }

        await Assert.That(store.Writes).IsEqualTo(1);
        await Assert.That(store.Announcements).IsEqualTo(1);
    }

    /// <summary>
    /// An add and the remove after it give back the value as it was, byte for byte: a
    /// value ending in a separator, a plain string, an empty value and no value at all.
    /// A value made from nothing is the kind Windows gives a user PATH.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AnAddAndItsRemoveLeaveThePathByteForByteAsTheyFoundIt()
    {
        foreach (var before in new UserPathValue?[]
        {
            new(@"C:\Tools\bin;", RegistryValueKind.ExpandString),
            new(@"C:\Tools\bin", RegistryValueKind.String),
            new(string.Empty, RegistryValueKind.ExpandString),
            null,
        })
        {
            var store = new ScratchUserPath { Value = before };

            await Assert.That(UserPath.Add(store, Folder, dryRun: false, NoVariables).Action).IsEqualTo(Change.Added);
            await Assert.That(UserPath.Segments(store.Value!.Text)).Contains(Folder);
            await Assert.That(store.Value!.Kind).IsEqualTo(before?.Kind ?? RegistryValueKind.ExpandString);

            await Assert.That(UserPath.Remove(store, Folder, dryRun: false, NoVariables).Action).IsEqualTo(Change.Removed);
            await Assert.That(store.Value).IsEqualTo(before);
        }
    }

    /// <summary>
    /// path remove takes off every entry naming exactly that folder, case and a trailing
    /// separator aside, and nothing else: not another spelling, not a folder inside it,
    /// not a folder whose name starts the same way.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ARemoveTakesOffExactlyThatFolderAndNothingElse()
    {
        string[] others = [@"%LOCALAPPDATA%\Demo", @"C:\Apps\Demo\bin", @"C:\Apps\Demo2", @"C:\Tools\Demo"];
        var store = new ScratchUserPath
        {
            Value = new UserPathValue(string.Join(';', [others[0], @"c:\apps\demo\", others[1], others[2], Folder, others[3]]), RegistryValueKind.ExpandString),
        };

        var removed = UserPath.Remove(store, Folder, dryRun: false, NoVariables);

        await Assert.That(removed.Action).IsEqualTo(Change.Removed);
        await Assert.That(removed.Announced).IsTrue();
        await Assert.That(store.Value!.Text).IsEqualTo(string.Join(';', others));

        // Nothing left of it: a second remove writes nothing and announces nothing.
        var again = UserPath.Remove(store, Folder, dryRun: false, NoVariables);

        await Assert.That(again.Action).IsEqualTo(Change.None);
        await Assert.That(store.Writes).IsEqualTo(1);
        await Assert.That(store.Announcements).IsEqualTo(1);

        // A PATH that does not exist at all holds nothing to remove.
        var none = new ScratchUserPath();

        await Assert.That(UserPath.Remove(none, Folder, dryRun: false, NoVariables).Action).IsEqualTo(Change.None);
        await Assert.That(none.Writes).IsEqualTo(0);
    }

    /// <summary>A dry run decides what an add or a remove would do, and writes and announces nothing.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ADryRunDecidesAndWritesNothing()
    {
        var store = new ScratchUserPath { Value = new UserPathValue(Theirs, RegistryValueKind.ExpandString) };

        var add = UserPath.Add(store, Folder, dryRun: true, NoVariables);
        var remove = UserPath.Remove(store, @"C:\Tools\bin", dryRun: true, NoVariables);
        var nothing = UserPath.Remove(store, Folder, dryRun: true, NoVariables);

        await Assert.That(add.Action).IsEqualTo(Change.Added);
        await Assert.That(remove.Action).IsEqualTo(Change.Removed);
        await Assert.That(nothing.Action).IsEqualTo(Change.None);
        await Assert.That(add.Announced).IsNull();
        await Assert.That(remove.Announced).IsNull();
        await Assert.That(nothing.Announced).IsNull();
        await Assert.That(store.Writes).IsEqualTo(0);
        await Assert.That(store.Announcements).IsEqualTo(0);
        await Assert.That(store.Value!.Text).IsEqualTo(Theirs);
    }

    /// <summary>
    /// A write that does not read back as written is a failure with exit code 1, and is
    /// not announced: the change counts only when the PATH reads as asked.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AWriteThatDoesNotReadBackIsAFailure()
    {
        var store = new ScratchUserPath { Value = new UserPathValue(Theirs, RegistryValueKind.ExpandString), LosesWrites = true };

        var add = UserPath.Add(store, Folder, dryRun: false, NoVariables);
        var remove = UserPath.Remove(store, @"C:\Tools\bin", dryRun: false, NoVariables);

        foreach (var failed in new[] { add, remove })
        {
            await Assert.That(failed.Action).IsEqualTo(Change.Failed);
            await Assert.That(failed.ExitCode).IsEqualTo(1);
            await Assert.That(failed.Error!).Contains("read back");
        }

        await Assert.That(store.Announcements).IsEqualTo(0);
    }

    /// <summary>
    /// The registry store keeps a value's kind and hands its text back unexpanded, over a
    /// scratch key under <c>HKEY_CURRENT_USER\Software</c> that the test deletes.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheRegistryStoreKeepsTheKindAndTheUnexpandedText()
    {
        var subKey = $@"Software\RegisterAI.Tests\UserPath-{Guid.NewGuid():N}";
        var store = new RegistryUserPathStore(Registry.CurrentUser, subKey, announce: false);

        try
        {
            await Assert.That(store.Where).IsEqualTo($@"HKEY_CURRENT_USER\{subKey}\Path");

            foreach (var before in new[]
            {
                new UserPathValue(@"%SystemRoot%\x;C:\Tools\bin", RegistryValueKind.ExpandString),
                new UserPathValue(@"C:\Tools\bin;", RegistryValueKind.String),
            })
            {
                store.Write(before);

                await Assert.That(store.Read()).IsEqualTo(before);

                var added = UserPath.Add(store, Folder, dryRun: false, NoVariables);

                await Assert.That(added.Action).IsEqualTo(Change.Added);
                await Assert.That(added.Announced).IsFalse();
                await Assert.That(store.Read()).IsEqualTo(before with { Text = before.Text + ";" + Folder });

                _ = UserPath.Remove(store, Folder, dryRun: false, NoVariables);

                await Assert.That(store.Read()).IsEqualTo(before);
            }

            store.Delete();

            _ = UserPath.Add(store, Folder, dryRun: false, NoVariables);

            await Assert.That(store.Read()).IsEqualTo(new UserPathValue(Folder, RegistryValueKind.ExpandString));

            _ = UserPath.Remove(store, Folder, dryRun: false, NoVariables);

            await Assert.That(store.Read()).IsNull();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
    }

    /// <summary>The real machine's store is the user's own PATH, and only that.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheRealMachinesStoreIsTheUsersOwnPath() =>
        await Assert.That(Machine.Real().UserPath.Where).IsEqualTo(@"HKEY_CURRENT_USER\Environment\Path");

    /// <summary>
    /// The broadcast's call into Windows binds and marshals: sent to a window handle that
    /// names no window, it returns not sent, with no exception and no message delivered
    /// anywhere. The broadcast itself is never sent from a test.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheBroadcastCallBindsWithoutBroadcastingAnything() =>
        await Assert.That(EnvironmentBroadcast.Send(0, 0)).IsFalse();

    /// <summary>
    /// The dead entries are those naming a folder that does not exist, after a variable
    /// is expanded in an expandable value. An entry that is not a full path, a network
    /// path and an empty entry are not judged, and each dead entry is named once, as
    /// stored.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheDeadEntriesAreTheFoldersThatDoNotExist()
    {
        using var scratch = Scratch.Create("dead");
        var here = Directory.CreateDirectory(scratch.In("here")).FullName;
        var gone = scratch.In("gone");
        var alsoGone = scratch.In("also-gone");
        string? variable(string name) => name is "DEMO_ROOT" ? scratch.Folder : null;

        var text = string.Join(';', [here, gone, @"%DEMO_ROOT%\here", @"%DEMO_ROOT%\gone", "relative", @"\\server\share\bin", string.Empty, gone.ToUpperInvariant() + @"\", " " + alsoGone + " ", "%NOT_SET%\\bin"]);

        var dead = UserPath.Dead(new UserPathValue(text, RegistryValueKind.ExpandString), variable);

        await Assert.That(dead).IsEquivalentTo([gone, @"%DEMO_ROOT%\gone", alsoGone]);

        // In a plain string nothing is expanded, so an entry spelled with a variable is
        // not a full path and is not judged, while a full path in it still is.
        var plain = UserPath.Dead(new UserPathValue(@"%DEMO_ROOT%\gone;" + gone, RegistryValueKind.String), variable);

        await Assert.That(plain).IsEquivalentTo([gone]);
        await Assert.That(UserPath.Dead(null, variable)).IsEmpty();
    }

    /// <summary>
    /// status reports the user PATH's dead entries, each with the line that removes it,
    /// and removes none of them itself.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task StatusReportsTheDeadEntriesAndRemovesNone()
    {
        using var bench = Bench.Create("status-dead");
        var gone = bench.Scratch.In("gone");

        bench.UserPath.Value = new UserPathValue(string.Join(';', [bench.Clients, gone]), RegistryValueKind.ExpandString);

        var result = await bench.RunAsync("status", "--name", "demo");

        await Assert.That(result.Path).IsNotNull();
        await Assert.That(result.Path!.Where).IsEqualTo(bench.UserPath.Where);
        await Assert.That(result.Path!.Dead).IsEquivalentTo([gone]);
        await Assert.That(result.Path!.Action).IsNull();
        await Assert.That(UserPath.RemoveCommand(gone)).IsEqualTo("registerai path remove '" + gone + "'");
        await Assert.That(bench.UserPath.Writes).IsEqualTo(0);

        // register and unregister report nothing about the user PATH.
        var register = await bench.RunAsync("register", "--name", "demo", "--client", "claude-code", "--scope", "user", "--", bench.Server);

        await Assert.That(register.Path).IsNull();
    }

    /// <summary>
    /// The path verbs through the command line: the folder, the action, the dead entries
    /// left, and the exit code, against the scratch store.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ThePathVerbsRunFromTheCommandLineAgainstTheMachinesStore()
    {
        using var bench = Bench.Create("path-verbs");
        var gone = bench.Scratch.In("gone");

        bench.UserPath.Value = new UserPathValue(gone, RegistryValueKind.ExpandString);

        var added = PathVerbs.Run(Bench.Parse("path", "add", bench.Root), bench.Machine);

        await Assert.That(added.Action).IsEqualTo(Change.Added);
        await Assert.That(added.Folder).IsEqualTo(bench.Root);
        await Assert.That(added.Dead).IsEquivalentTo([gone]);
        await Assert.That(bench.UserPath.Value!.Text).IsEqualTo(gone + ";" + bench.Root);

        var removed = PathVerbs.Run(Bench.Parse("path", "remove", gone), bench.Machine);

        await Assert.That(removed.Action).IsEqualTo(Change.Removed);
        await Assert.That(removed.Dead).IsEmpty();
        await Assert.That(bench.UserPath.Value!.Text).IsEqualTo(bench.Root);

        var dry = PathVerbs.Run(Bench.Parse("path", "remove", bench.Root, "--dry-run"), bench.Machine);

        await Assert.That(dry.Action).IsEqualTo(Change.Removed);
        await Assert.That(bench.UserPath.Value!.Text).IsEqualTo(bench.Root);
    }

    /// <summary>
    /// The session guard tells a moved PATH from an unmoved one, and its reading of the
    /// real PATH is the kind, the length and a hash, never the text.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheGuardTellsAMovedPathFromAnUnmovedOne()
    {
        const string Reading = "ExpandString length=3 sha256=00";

        await Assert.That(UserPathGuard.Difference(Reading, Reading)).IsNull();
        await Assert.That(UserPathGuard.Difference("absent", Reading)!).Contains("changed during this run");
        await Assert.That(UserPathGuard.Difference(Reading, Reading.Replace("3", "4", StringComparison.Ordinal))!).Contains(Reading);

        var real = UserPathGuard.Reading();

        await Assert.That(real is "absent" || real.Contains(" sha256=", StringComparison.Ordinal)).IsTrue();
        await Assert.That(real).DoesNotContain(@":\");
    }

    private static string? NoVariables(string name) => null;
}
