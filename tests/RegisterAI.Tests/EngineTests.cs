// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using RegisterAI.Tests.Harness;

namespace RegisterAI.Tests;

/// <summary>
/// The decisions, in process: what each verb does to each state, for both clients and
/// both scopes, with the client model answering every call and every call recorded.
/// The calls a test expects to be absent are asserted absent, because "refused" and
/// "tried and failed" differ only in whether anything ran.
/// </summary>
internal sealed class EngineTests
{
    private const string RemoveAdd = "remove add";
    private const string AddList = "add list";
    private const string ListAddList = "list add list";
    private const string ListRemoveAddList = "list remove add list";
    private const string ListOnly = "list";

    /// <summary>
    /// Register is safe to repeat: the first adds, the next leave the entry alone, and
    /// only --replace rewrites it. One entry at the end, for each client.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task RepeatedRegistersLeaveExactlyOneEntryAndOnlyReplaceRewritesIt()
    {
        using var bench = Bench.Create("repeat");
        string[] register = ["register", "--name", "demo", "--client", "claude-code", "--scope", "user", "--owned-root", bench.Root, "--", bench.Server];

        await Assert.That((await bench.RunAsync(register)).Results[0].Action).IsEqualTo(Change.Added);
        await Assert.That((await bench.RunAsync(register)).Results[0].Action).IsEqualTo(Change.None);

        // Claude Code is read from its file, so a register with nothing to do runs nothing.
        await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo("add");

        await Assert.That((await bench.RunAsync(With(register, "--replace"))).Results[0].Action).IsEqualTo(Change.Replaced);
        await Assert.That(Sequence(bench.Runner.Verbs.Skip(1))).IsEqualTo(RemoveAdd);
        await Assert.That(JsonNode.Parse(await File.ReadAllTextAsync(bench.ClaudeUserFile))!["mcpServers"]!.AsObject().Count).IsEqualTo(1);

        // Codex, read through its own list command.
        bench.Runner.Calls.Clear();
        string[] codex = ["register", "--name", "demo", "--client", "codex", "--scope", "user", "--owned-root", bench.Root, "--", bench.Server];

        await Assert.That((await bench.RunAsync(codex)).Results[0].Action).IsEqualTo(Change.Added);
        await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(ListAddList);

        bench.Runner.Calls.Clear();
        await Assert.That((await bench.RunAsync(codex)).Results[0].Action).IsEqualTo(Change.None);
        await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(ListOnly);

        bench.Runner.Calls.Clear();
        await Assert.That((await bench.RunAsync(With(codex, "--replace"))).Results[0].Action).IsEqualTo(Change.Replaced);
        await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(ListRemoveAddList);
    }

    /// <summary>
    /// An own entry gone stale is rewritten; a foreign one is refused with nothing run,
    /// unless --take-over says a person asked for exactly that.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AnOwnStaleEntryIsRewrittenAndAForeignOneIsRefused()
    {
        using var bench = Bench.Create("stale");
        string[] register = ["register", "--name", "demo", "--client", "claude-code", "--scope", "user", "--owned-root", bench.Root, "--", bench.Server];

        // Own: under the root, naming a file an older version had.
        Bench.WriteClaudeEntry(bench.ClaudeUserFile, Path.Combine(bench.Root, "old", "demo-mcp.exe"));

        var rewritten = (await bench.RunAsync(register)).Results[0];

        await Assert.That(rewritten.Before.State).IsEqualTo(State.OursStale);
        await Assert.That(rewritten.Action).IsEqualTo(Change.Replaced);
        await Assert.That(rewritten.After.State).IsEqualTo(State.Ours);
        await Assert.That(rewritten.After.Command).IsEqualTo(bench.Server);
        await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(RemoveAdd);

        // Foreign: refused, the file unchanged, nothing run.
        bench.Runner.Calls.Clear();
        Bench.WriteClaudeEntry(bench.ClaudeUserFile, @"D:\Other\demo-mcp.exe");
        var untouched = await File.ReadAllTextAsync(bench.ClaudeUserFile);

        var refused = await bench.RunAsync(register);

        await Assert.That(refused.Results[0].Action).IsEqualTo(Change.RefusedForeign);
        await Assert.That(refused.ExitCode).IsEqualTo(3);
        await Assert.That(refused.Results[0].Error!).Contains(@"D:\Other\demo-mcp.exe");
        await Assert.That(await File.ReadAllTextAsync(bench.ClaudeUserFile)).IsEqualTo(untouched);
        await Assert.That(bench.Runner.Calls).IsEmpty();

        var takenOver = await bench.RunAsync(With(register, "--take-over"));

        await Assert.That(takenOver.Results[0].Action).IsEqualTo(Change.Replaced);
        await Assert.That(takenOver.Results[0].After.State).IsEqualTo(State.Ours);
    }

    /// <summary>
    /// An own entry that already names the command is left as it is, arguments a person
    /// added included. One naming another file under the root is rewritten.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AnOwnMatchingEntryIsLeftAloneArgumentsAndAll()
    {
        using var bench = Bench.Create("matching");
        string[] register = ["register", "--name", "demo", "--client", "claude-code", "--scope", "user", "--owned-root", bench.Root, "--", bench.Server];

        Bench.WriteClaudeEntry(bench.ClaudeUserFile, bench.Server, ["--added-by-a-person"]);

        var kept = (await bench.RunAsync(register)).Results[0];

        await Assert.That(kept.Before.State).IsEqualTo(State.Ours);
        await Assert.That(kept.Action).IsEqualTo(Change.None);
        await Assert.That(kept.Before.Arguments).IsEquivalentTo(["--added-by-a-person"]);
        await Assert.That(bench.Runner.Calls).IsEmpty();

        // Another file of the same product, which exists: own, and not the one asked for.
        var other = Path.Combine(bench.Root, "demo-app.exe");
        await File.WriteAllTextAsync(other, "the product's window, not its server");
        Bench.WriteClaudeEntry(bench.ClaudeUserFile, other);

        var repointed = (await bench.RunAsync(register)).Results[0];

        await Assert.That(repointed.Before.State).IsEqualTo(State.OursStale);
        await Assert.That(repointed.Action).IsEqualTo(Change.Replaced);
        await Assert.That(repointed.After.Command).IsEqualTo(bench.Server);
    }

    /// <summary>
    /// A configuration that cannot be read is never taken to hold nothing: every verb
    /// refuses with exit code 4 and runs nothing.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AnUnreadableConfigurationIsNeverReadAsNothingRegistered()
    {
        using var bench = Bench.Create("unreadable");

        foreach (var text in new[] { "{ not json", string.Empty, "  \n", "{\"mcpServers\": []}", "[1, 2]" })
        {
            await File.WriteAllTextAsync(bench.ClaudeUserFile, text);

            var status = await bench.RunAsync("status", "--name", "demo", "--client", "claude-code");
            var register = await bench.RunAsync("register", "--name", "demo", "--client", "claude-code", "--scope", "user", "--owned-root", bench.Root, "--", bench.Server);
            var unregister = await bench.RunAsync("unregister", "--name", "demo", "--client", "claude-code", "--scope", "user", "--owned-root", bench.Root);

            await Assert.That(status.Results[0].Before.State).IsEqualTo(State.Unreadable);
            await Assert.That(status.ExitCode).IsEqualTo(4);
            await Assert.That(status.Results[0].Error!).Contains("not known");
            await Assert.That(register.Results[0].Action).IsEqualTo(Change.RefusedUnreadable);
            await Assert.That(unregister.Results[0].Action).IsEqualTo(Change.RefusedUnreadable);
            await Assert.That(register.ExitCode).IsEqualTo(4);
        }

        await Assert.That(bench.Runner.Calls).IsEmpty();

        // Codex: a list that fails, and a list that is not JSON.
        foreach (var answer in new[] { new RunOutcome(1, "codex: something went wrong", false, null), new RunOutcome(0, "codex: unrecognized subcommand", false, null) })
        {
            bench.Runner.Calls.Clear();
            bench.Runner.Answer = arguments => arguments is ["mcp", "list", ..] ? answer : null;

            var refused = await bench.RunAsync("register", "--name", "demo", "--client", "codex", "--scope", "user", "--owned-root", bench.Root, "--", bench.Server);

            await Assert.That(refused.Results[0].Action).IsEqualTo(Change.RefusedUnreadable);
            await Assert.That(refused.Results[0].Error!).Contains("not known");
            await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(ListOnly);
        }
    }

    /// <summary>
    /// Neither register nor unregister touches an entry the caller did not write, for
    /// either client, and an entry with a URL or another transport is never the caller's.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task NeitherVerbTouchesAForeignEntry()
    {
        using var bench = Bench.Create("foreign");
        var register = new[] { "register", "--name", "demo", "--scope", "user", "--owned-root", bench.Root, "--", bench.Server };
        var unregister = new[] { "unregister", "--name", "demo", "--scope", "user", "--owned-root", bench.Root };

        Bench.WriteClaudeEntry(bench.ClaudeUserFile, @"D:\Other\demo-mcp.exe");
        bench.WriteCodexEntry(bench.Variables["CODEX_HOME"]!, @"D:\Other\demo-mcp.exe");

        foreach (var command in new[] { register, unregister })
        {
            bench.Runner.Calls.Clear();

            var result = await bench.RunAsync([.. command[..1], "--client", "all", .. command[1..]]);

            await Assert.That(result.Results.Select(item => item.Action)).IsEquivalentTo([Change.RefusedForeign, Change.RefusedForeign]);
            await Assert.That(result.ExitCode).IsEqualTo(3);

            // The one call is Codex's list: a read.
            await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(ListOnly);
        }

        // An entry that is not a local command, under the caller's own name.
        foreach (var extra in new[] { new JsonObject { ["url"] = "https://example.com/mcp" }, new JsonObject { ["type"] = "http" } })
        {
            Bench.WriteClaudeEntry(bench.ClaudeUserFile, bench.Server, extra: extra);

            var status = await bench.RunAsync("status", "--name", "demo", "--client", "claude-code", "--owned-root", bench.Root, "--", bench.Server);

            await Assert.That(status.Results[0].Before.State).IsEqualTo(State.Foreign);
        }

        // With no owned root and no command, every entry is foreign.
        Bench.WriteClaudeEntry(bench.ClaudeUserFile, bench.Server);

        var blind = await bench.RunAsync("unregister", "--name", "demo", "--client", "claude-code", "--scope", "user");

        await Assert.That(blind.Results[0].Action).IsEqualTo(Change.RefusedForeign);
    }

    /// <summary>
    /// The reader tells every state apart: no file, no servers, no entry, own, own and
    /// gone, own and something else, foreign, and both shapes of Codex's list.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheReaderTellsEveryStateApart()
    {
        using var bench = Bench.Create("reader");
        var file = bench.ClaudeUserFile;
        var request = Bench.Parse("status", "--name", "demo", "--owned-root", bench.Root, "--", bench.Server);

        State stateOf(Reading reading) =>
            Ownership.Classify(reading.Entry, request, Ownership.Resolve(reading.Entry?.Command, ClientId.ClaudeCode, bench.Machine));

        await Assert.That(stateOf(Entries.FromClaudeFile(file, "demo"))).IsEqualTo(State.Absent);

        await File.WriteAllTextAsync(file, "{\"projects\": {}}");
        await Assert.That(stateOf(Entries.FromClaudeFile(file, "demo"))).IsEqualTo(State.Absent);

        Bench.WriteClaudeEntry(file, bench.Server);
        await Assert.That(stateOf(Entries.FromClaudeFile(file, "something-else"))).IsEqualTo(State.Absent);
        await Assert.That(stateOf(Entries.FromClaudeFile(file, "demo"))).IsEqualTo(State.Ours);

        Bench.WriteClaudeEntry(file, Path.Combine(bench.Root, "gone.exe"));
        await Assert.That(stateOf(Entries.FromClaudeFile(file, "demo"))).IsEqualTo(State.OursStale);

        Bench.WriteClaudeEntry(file, @"D:\Other\demo-mcp.exe");
        await Assert.That(stateOf(Entries.FromClaudeFile(file, "demo"))).IsEqualTo(State.Foreign);

        // Codex: a bare array, the same inside a wrapper, another server's entry, and words.
        var listed = "[{\"name\":\"demo\",\"enabled\":true,\"transport\":{\"type\":\"stdio\",\"command\":" + System.Text.Json.JsonSerializer.Serialize(bench.Server) + ",\"args\":[\"--stdio\"],\"env\":{\"A\":\"1\"}}}]";

        var bare = Entries.FromCodexList(listed, "demo");
        var wrapped = Entries.FromCodexList("{\"servers\":" + listed + "}", "demo");

        await Assert.That(bare.Entry!.Command).IsEqualTo(bench.Server);
        await Assert.That(bare.Entry.Arguments).IsEquivalentTo(["--stdio"]);
        await Assert.That(bare.Entry.EnvironmentNames).IsEquivalentTo(["A"]);
        await Assert.That(wrapped.Entry!.Command).IsEqualTo(bench.Server);
        await Assert.That(Entries.FromCodexList(listed, "other").Entry).IsNull();
        await Assert.That(Entries.FromCodexList("not json", "demo").Unreadable).IsNotNull();
        await Assert.That(Entries.FromCodexList("{\"something\": 1}", "demo").Unreadable).IsNotNull();
    }

    /// <summary>
    /// Claude Code expands ${NAME} and ${NAME:-default} and leaves an unset name as
    /// written; Codex expands nothing, so the same spelling is the caller's own in one
    /// client and foreign in the other.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ClaudeCodeExpandsBracedVariablesAndCodexExpandsNothing()
    {
        using var bench = Bench.Create("expand");
        bench.Variables["DEMO_ROOT"] = bench.Root;
        bench.Variables["EMPTY"] = string.Empty;

        string? variable(string name) => bench.Variables.GetValueOrDefault(name);

        await Assert.That(Ownership.Expand("${DEMO_ROOT}/demo-mcp.exe", variable)).IsEqualTo(bench.Root + "/demo-mcp.exe");
        await Assert.That(Ownership.Expand("${NO_SUCH_NAME}/x", variable)).IsEqualTo("${NO_SUCH_NAME}/x");
        await Assert.That(Ownership.Expand("${NO_SUCH_NAME:-fallback}/x", variable)).IsEqualTo("fallback/x");
        await Assert.That(Ownership.Expand("${EMPTY:-fallback}/x", variable)).IsEqualTo("fallback/x");
        await Assert.That(Ownership.Expand("no variable", variable)).IsEqualTo("no variable");

        const string Spelled = "${DEMO_ROOT}/demo-mcp.exe";
        var request = Bench.Parse("status", "--name", "demo", "--owned-root", bench.Root);
        var entry = new Entry(Spelled, [], [], IsStdio: true);

        await Assert.That(Ownership.Classify(entry, request, Ownership.Resolve(Spelled, ClientId.ClaudeCode, bench.Machine))).IsEqualTo(State.Ours);
        await Assert.That(Ownership.Classify(entry, request, Ownership.Resolve(Spelled, ClientId.Codex, bench.Machine))).IsEqualTo(State.Foreign);

        // Named exactly, the spelling is the caller's own in Codex too, and stale there,
        // because Codex cannot start it.
        var named = Bench.Parse("status", "--name", "demo", "--", Spelled);

        await Assert.That(Ownership.Classify(entry, named, Ownership.Resolve(Spelled, ClientId.Codex, bench.Machine))).IsEqualTo(State.OursStale);
    }

    /// <summary>
    /// Unregister removes an own entry; over nothing it runs nothing and reports none,
    /// for both clients, even though Codex exits 0 when asked to remove nothing.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task UnregisterRemovesAnOwnEntryAndAnAbsentOneRunsNothing()
    {
        using var bench = Bench.Create("unregister");

        _ = await bench.RunAsync("register", "--name", "demo", "--client", "all", "--scope", "user", "--owned-root", bench.Root, "--", bench.Server);
        bench.Runner.Calls.Clear();

        var removed = await bench.RunAsync("unregister", "--name", "demo", "--client", "all", "--scope", "user", "--owned-root", bench.Root);

        await Assert.That(removed.Results.Select(item => item.Action)).IsEquivalentTo([Change.Removed, Change.Removed]);
        await Assert.That(removed.Results.Select(item => item.After.State)).IsEquivalentTo([State.Absent, State.Absent]);
        await Assert.That(removed.ExitCode).IsEqualTo(0);

        bench.Runner.Calls.Clear();
        var again = await bench.RunAsync("unregister", "--name", "demo", "--client", "all", "--scope", "user", "--owned-root", bench.Root);

        await Assert.That(again.Results.Select(item => item.Action)).IsEquivalentTo([Change.None, Change.None]);
        await Assert.That(again.ExitCode).IsEqualTo(0);
        await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(ListOnly);
    }

    /// <summary>
    /// With no client anywhere the tool says where it looked and what to run, and runs
    /// nothing. Claude Code's state is still read from its file; Codex's is unknown.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AMachineWithNoClientIsReportedAndNothingRuns()
    {
        using var bench = Bench.Create("noclient");
        bench.Variables["PATH"] = string.Empty;

        var register = await bench.RunAsync("register", "--name", "demo", "--client", "all", "--scope", "user", "--owned-root", bench.Root, "--", bench.Server);

        await Assert.That(register.Results.Select(item => item.Action)).IsEquivalentTo([Change.ClientNotFound, Change.ClientNotFound]);
        await Assert.That(register.ExitCode).IsEqualTo(5);
        await Assert.That(register.Results[0].Before.State).IsEqualTo(State.Absent);
        await Assert.That(register.Results[1].Before.State).IsEqualTo(State.Unknown);
        await Assert.That(register.Results[0].Manual!).Contains("claude mcp add demo --scope user --");
        await Assert.That(register.Results[1].Error!).Contains("chrome-native-hosts-v2.json");
        await Assert.That(bench.Runner.Calls).IsEmpty();

        // status answers for Claude Code from its file; with all, a missing client is
        // exit 5 only when every client is missing.
        var all = await bench.RunAsync("status", "--name", "demo");
        var codex = await bench.RunAsync("status", "--name", "demo", "--client", "codex");

        await Assert.That(all.ExitCode).IsEqualTo(0);
        await Assert.That(codex.ExitCode).IsEqualTo(5);
        await Assert.That(codex.Results[0].Error!).Contains("not known");

        // A named executable that is not there is reported as such.
        var named = await bench.RunAsync("register", "--name", "demo", "--client", "codex", "--scope", "user", "--client-exe", "codex=" + bench.Scratch.In("missing", "codex.exe"), "--", bench.Server);

        await Assert.That(named.Results[0].Error!).Contains("--client-exe names");
    }

    /// <summary>
    /// A client that refuses, hangs, cannot be started or throws is a failure in its own
    /// words with the line to run by hand, and so is a success that does not read back.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AClientThatFailsIsReportedInItsOwnWordsWithTheManualLine()
    {
        using var bench = Bench.Create("failing");
        string[] register = ["register", "--name", "demo", "--client", "claude-code", "--scope", "user", "--owned-root", bench.Root, "--", bench.Server];

        bench.Runner.Answer = arguments => arguments is ["mcp", "add", ..] ? new RunOutcome(2, "some other failure", false, null) : null;
        var refused = await bench.RunAsync(register);

        await Assert.That(refused.Results[0].Action).IsEqualTo(Change.Failed);
        await Assert.That(refused.ExitCode).IsEqualTo(1);
        await Assert.That(refused.Results[0].Error!).Contains("exited 2");
        await Assert.That(refused.Results[0].Said).IsEqualTo("some other failure");
        await Assert.That(refused.Results[0].Manual!).Contains("mcp add demo --scope user");

        bench.Runner.Answer = arguments => arguments is ["mcp", "add", ..] ? new RunOutcome(null, string.Empty, true, null) : null;
        await Assert.That((await bench.RunAsync(register)).Results[0].Error!).Contains("did not finish in time");

        bench.Runner.Answer = arguments => arguments is ["mcp", "add", ..] ? new RunOutcome(null, string.Empty, false, "Access is denied") : null;
        await Assert.That((await bench.RunAsync(register)).Results[0].Error!).Contains("Access is denied");

        // Exit 0 and nothing written: the read-back is what decides.
        bench.Runner.Answer = arguments => arguments is ["mcp", "add", ..] ? new RunOutcome(0, "Added", false, null) : null;
        var unconfirmed = await bench.RunAsync(register);

        await Assert.That(unconfirmed.Results[0].Action).IsEqualTo(Change.Failed);
        await Assert.That(unconfirmed.Results[0].Error!).Contains("reads back");

        bench.Runner.Answer = null;
        bench.Runner.Throws = true;
        var thrown = await bench.RunAsync(register);

        await Assert.That(thrown.Results[0].Action).IsEqualTo(Change.Failed);
        await Assert.That(thrown.Results[0].Error!).Contains("could not be started");
    }

    /// <summary>Each client's own commands, with the separator, and no scope flag for Codex.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheCommandsAreEachClientsOwnAndTheSeparatorIsThere()
    {
        var request = Bench.Parse("register", "--name", "demo", "--client", "all", "--scope", "user", "--env", "A=1", "--", "-dash.exe", "--stdio");

        await Assert.That(Sequence(ClientRules.AddArguments(ClientId.ClaudeCode, Scope.User, request)))
            .IsEqualTo("mcp add demo --scope user --env A=1 -- -dash.exe --stdio");
        await Assert.That(Sequence(ClientRules.AddArguments(ClientId.Codex, Scope.Project, request)))
            .IsEqualTo("mcp add demo --env A=1 -- -dash.exe --stdio");
        await Assert.That(Sequence(ClientRules.RemoveArguments(ClientId.ClaudeCode, Scope.Project, "demo"))).IsEqualTo("mcp remove demo --scope project");
        await Assert.That(Sequence(ClientRules.RemoveArguments(ClientId.Codex, Scope.Project, "demo"))).IsEqualTo("mcp remove demo");
        await Assert.That(Sequence(ClientRules.ListArguments())).IsEqualTo("mcp list --json");
    }

    /// <summary>
    /// The project lever is the client's: Claude Code runs in the project, Codex runs with
    /// CODEX_HOME moved and nothing else.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AProjectEntryMovesTheClientsOwnLeverAndNothingElse()
    {
        using var bench = Bench.Create("lever");

        var codex = ClientRules.Environment(ClientId.Codex, Scope.Project, bench.Project);

        await Assert.That(codex.Count).IsEqualTo(1);
        await Assert.That(codex["CODEX_HOME"]).IsEqualTo(Path.Combine(bench.Project, ".codex"));
        await Assert.That(ClientRules.Environment(ClientId.ClaudeCode, Scope.Project, bench.Project).Count).IsEqualTo(0);
        await Assert.That(ClientRules.Environment(ClientId.Codex, Scope.User, null).Count).IsEqualTo(0);
        await Assert.That(ClientRules.WorkingDirectory(ClientId.ClaudeCode, Scope.Project, bench.Project, bench.Machine)).IsEqualTo(bench.Project);
        await Assert.That(ClientRules.WorkingDirectory(ClientId.Codex, Scope.Project, bench.Project, bench.Machine)).IsEqualTo(bench.Profile);
        await Assert.That(ClientRules.WorkingDirectory(ClientId.ClaudeCode, Scope.User, null, bench.Machine)).IsEqualTo(bench.Profile);
    }

    /// <summary>
    /// A Codex project register creates the project's home, writes there and never in the
    /// user's home, and every call it makes carries the lever.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ACodexProjectRegisterWritesTheProjectHomeAndNeverTheUsers()
    {
        using var bench = Bench.Create("codexproject");
        var home = Path.Combine(bench.Project, ".codex");
        string[] register = ["register", "--name", "demo", "--client", "codex", "--scope", "project", "--project", bench.Project, "--owned-root", bench.Root, "--", bench.Server];

        await Assert.That(Directory.Exists(home)).IsFalse();

        var first = await bench.RunAsync(register);

        // A project with no home holds no entry and Codex refuses to be asked about one,
        // so the first call is the write.
        await Assert.That(first.Results[0].Action).IsEqualTo(Change.Added);
        await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(AddList);
        await Assert.That(Directory.Exists(home)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(bench.Variables["CODEX_HOME"]!, "fake-codex-servers.json"))).IsFalse();

        var replaced = await bench.RunAsync(With(register, "--replace"));

        await Assert.That(replaced.Results[0].Action).IsEqualTo(Change.Replaced);
        await Assert.That(bench.Runner.Calls.All(call => call.Environment.GetValueOrDefault("CODEX_HOME") == home)).IsTrue();
        await Assert.That(bench.Runner.Calls.Any(call => call.Arguments.Contains("--scope"))).IsFalse();
        await Assert.That(first.Results[0].Advice.Select(advice => advice.Code)).IsEquivalentTo(["codex-new-thread", "codex-trust-project"]);
    }

    /// <summary>
    /// A Claude Code project register over an own stale entry removes it first, and both
    /// calls run in the project.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ARegisterOverAnOwnStaleProjectEntryRemovesItFirstAndRunsInTheProject()
    {
        using var bench = Bench.Create("claudeproject");
        var file = Path.Combine(bench.Project, ".mcp.json");

        Bench.WriteClaudeEntry(file, Path.Combine(bench.Root, "old.exe"));

        var result = await bench.RunAsync("register", "--name", "demo", "--client", "claude-code", "--scope", "project", "--project", bench.Project, "--owned-root", bench.Root, "--", bench.Server);

        await Assert.That(result.Results[0].Action).IsEqualTo(Change.Replaced);
        await Assert.That(result.Results[0].Config).IsEqualTo(file);
        await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(RemoveAdd);
        await Assert.That(bench.Runner.Calls.All(call => call.Directory == bench.Project && call.Arguments.Contains("project"))).IsTrue();
        await Assert.That(result.Results[0].Advice.Select(advice => advice.Code)).IsEquivalentTo(["claude-restart-session", "claude-approve-project"]);
        await Assert.That(result.Results[0].Manual!).StartsWith("Set-Location -LiteralPath ");
    }

    /// <summary>A project unregister refuses a foreign entry and runs nothing over none.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AProjectUnregisterRefusesAForeignEntryAndRunsNothingOverNone()
    {
        using var bench = Bench.Create("projectunregister");
        var home = Directory.CreateDirectory(Path.Combine(bench.Project, ".codex")).FullName;
        string[] unregister = ["unregister", "--name", "demo", "--client", "codex", "--scope", "project", "--project", bench.Project, "--owned-root", bench.Root];

        bench.WriteCodexEntry(home, @"D:\Other\demo-mcp.exe");

        var refused = await bench.RunAsync(unregister);

        await Assert.That(refused.Results[0].Action).IsEqualTo(Change.RefusedForeign);
        await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(ListOnly);

        File.Delete(Path.Combine(home, "fake-codex-servers.json"));
        bench.Runner.Calls.Clear();

        var nothing = await bench.RunAsync(unregister);

        await Assert.That(nothing.Results[0].Action).IsEqualTo(Change.None);
        await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(ListOnly);
    }

    /// <summary>
    /// The empty folders a Codex run leaves in a project's home are removed, innermost
    /// first and only while empty; a folder with somebody's file in it stays.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheResidueOfACodexProjectRunIsRemovedOnlyWhileEmpty()
    {
        using var bench = Bench.Create("residue");
        var home = Path.Combine(bench.Project, ".codex");
        var tmp = Path.Combine(home, "tmp");
        string[] register = ["register", "--name", "demo", "--client", "codex", "--scope", "project", "--project", bench.Project, "--owned-root", bench.Root, "--", bench.Server];

        _ = await bench.RunAsync(register);

        // The model leaves tmp\arg0 behind on every call, as the client was measured to.
        await Assert.That(Directory.Exists(tmp)).IsFalse();
        await Assert.That(Directory.Exists(home)).IsTrue();

        var keep = Path.Combine(tmp, "somebody-elses.txt");
        _ = Directory.CreateDirectory(tmp);
        await File.WriteAllTextAsync(keep, "not ours");

        _ = await bench.RunAsync(With(register, "--replace"));

        await Assert.That(Directory.Exists(Path.Combine(tmp, "arg0"))).IsFalse();
        await Assert.That(File.Exists(keep)).IsTrue();
    }

    /// <summary>
    /// The search finds a named executable first, then PATH, then the profile's
    /// .local\bin, then Codex's desktop manifest and npm layout; the refusal names every
    /// place it looked.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheSearchOrderHoldsAndTheRefusalNamesEveryPlace()
    {
        using var bench = Bench.Create("search");
        var plain = Bench.Parse("status", "--name", "demo");

        await Assert.That(ClientRules.Locate(ClientId.Codex, plain, bench.Machine)).IsEqualTo(Path.Combine(bench.Clients, "codex.exe"));

        var named = Path.Combine(bench.Scratch.In("named"), "codex.exe");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(named)!);
        await File.WriteAllTextAsync(named, "named");

        await Assert.That(ClientRules.Locate(ClientId.Codex, Bench.Parse("status", "--name", "demo", "--client-exe", "codex=" + named), bench.Machine)).IsEqualTo(named);

        bench.Variables["PATH"] = string.Empty;

        var localBin = Directory.CreateDirectory(Path.Combine(bench.Profile, ".local", "bin")).FullName;
        await File.WriteAllTextAsync(Path.Combine(localBin, "claude.exe"), "fallback");
        await Assert.That(ClientRules.Locate(ClientId.ClaudeCode, plain, bench.Machine)).IsEqualTo(Path.Combine(localBin, "claude.exe"));

        // The desktop app names its CLI in a manifest, and puts it nowhere on PATH.
        var desktop = Path.Combine(bench.Profile, "desktop", "codex.exe");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(desktop)!);
        await File.WriteAllTextAsync(desktop, "desktop");
        var manifest = Path.Combine(bench.Machine.LocalAppData, "OpenAI", "Codex", "chrome-native-hosts-v2.json");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        await File.WriteAllTextAsync(manifest, new JsonObject { ["entries"] = new JsonArray(new JsonObject { ["paths"] = new JsonObject { ["codexCliPath"] = desktop } }) }.ToJsonString());

        await Assert.That(ClientRules.Locate(ClientId.Codex, plain, bench.Machine)).IsEqualTo(desktop);

        File.Delete(manifest);
        var npm = Path.Combine(bench.Machine.AppData, "npm", "node_modules", "@openai", "codex", "bin", "codex.exe");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(npm)!);
        await File.WriteAllTextAsync(npm, "npm");

        await Assert.That(ClientRules.Locate(ClientId.Codex, plain, bench.Machine)).IsEqualTo(npm);

        File.Delete(npm);
        var refusal = ClientRules.NotFound(ClientId.Codex, plain, bench.Machine);

        await Assert.That(ClientRules.Locate(ClientId.Codex, plain, bench.Machine)).IsNull();
        await Assert.That(refusal).Contains("codex.exe was not found on PATH");
        await Assert.That(refusal).Contains(localBin);
        await Assert.That(refusal).Contains(manifest);
        await Assert.That(refusal).Contains(Path.Combine(bench.Machine.AppData, "npm", "node_modules", "@openai", "codex"));
        await Assert.That(refusal).Contains("--client-exe codex=<path>");
    }

    /// <summary>Both clients, always in the same order, whatever order they were asked in.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task BothClientsAreListedInAFixedOrderWithDistinctIds()
    {
        await Assert.That(Sequence(Clients.All.Select(client => client.Word))).IsEqualTo("claude-code codex");
        await Assert.That(Sequence(Bench.Parse("status", "--name", "demo", "--client", "codex", "--client", "claude-code").Clients.Select(client => client.Info().Word)))
            .IsEqualTo("claude-code codex");
        await Assert.That(Bench.Parse("status", "--name", "demo", "--client", "codex", "--client", "codex").Clients).IsEquivalentTo([ClientId.Codex]);
    }

    /// <summary>
    /// A bare command is looked up on the PATH a new program gets: found, it is reported
    /// with where, and Codex is advised to restart; found nowhere, the path-missing advice
    /// says so and names 'registerai path add' and 'registerai path remove', with no
    /// command to run because no folder was named.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ABareCommandIsFoundOnTheNewProgramPathOrReportedMissing()
    {
        using var bench = Bench.Create("bare");
        var first = Directory.CreateDirectory(bench.Scratch.In("path-a")).FullName;
        var second = Directory.CreateDirectory(bench.Scratch.In("path-b")).FullName;

        bench.NewProgramPath.AddRange([first, second]);
        await File.WriteAllTextAsync(Path.Combine(second, "demo-mcp.exe"), "server");

        await Assert.That(Ownership.Resolve("demo-mcp.exe", ClientId.Codex, bench.Machine)).IsEqualTo(Path.Combine(second, "demo-mcp.exe"));
        await Assert.That(Ownership.Resolve("demo-mcp", ClientId.Codex, bench.Machine)).IsEqualTo(Path.Combine(second, "demo-mcp.exe"));
        await Assert.That(Ownership.Resolve("no-such-server.exe", ClientId.Codex, bench.Machine)).IsNull();
        await Assert.That(Ownership.IsBareName("demo-mcp.exe")).IsTrue();
        await Assert.That(Ownership.IsBareName(@"C:\Apps\demo-mcp.exe")).IsFalse();

        var found = await bench.RunAsync("register", "--name", "demo", "--client", "codex", "--scope", "project", "--project", bench.Project, "--", "demo-mcp.exe");

        await Assert.That(found.Results[0].After.ResolvesTo).IsEqualTo(Path.Combine(second, "demo-mcp.exe"));
        await Assert.That(found.Results[0].Advice.Select(advice => advice.Code)).Contains("codex-restart-for-path");

        var missing = await bench.RunAsync("status", "--name", "demo", "--client", "codex", "--scope", "project", "--project", bench.Project, "--", "demo-mcp.exe");

        await Assert.That(missing.Results[0].Before.State).IsEqualTo(State.Ours);

        File.Delete(Path.Combine(second, "demo-mcp.exe"));
        var gone = await bench.RunAsync("status", "--name", "demo", "--client", "codex", "--scope", "project", "--project", bench.Project, "--", "demo-mcp.exe");
        var advice = gone.Results[0].Advice.Single();

        await Assert.That(gone.Results[0].Before.State).IsEqualTo(State.OursStale);
        await Assert.That(advice.Code).IsEqualTo("path-missing");
        await Assert.That(advice.Command).IsNull();
        await Assert.That(advice.Text).Contains("registerai path add <folder>");
        await Assert.That(advice.Text).Contains("registerai path remove <folder>");
        await Assert.That(advice.Text).DoesNotContain("rundll32");
    }

    /// <summary>
    /// With --path-folder, the path-missing advice is about that folder: when it is not on
    /// the PATH a new program gets, the advice's command is 'registerai path add' for
    /// exactly that folder, whether or not the bare name is found somewhere else; when it
    /// is on it, there is no path-missing advice.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ThePathMissingAdviceNamesTheAddCommandForTheFolderItWasGiven()
    {
        using var bench = Bench.Create("path-folder");
        var needed = Directory.CreateDirectory(bench.Scratch.In("current")).FullName;
        var elsewhere = Directory.CreateDirectory(bench.Scratch.In("elsewhere")).FullName;

        await File.WriteAllTextAsync(Path.Combine(needed, "demo-mcp.exe"), "server");
        await File.WriteAllTextAsync(Path.Combine(elsewhere, "demo-mcp.exe"), "another product's server");

        string[] register = ["register", "--name", "demo", "--client", "codex", "--scope", "project", "--project", bench.Project, "--path-folder", needed, "--", "demo-mcp.exe"];
        string[] status = ["status", "--name", "demo", "--client", "codex", "--scope", "project", "--project", bench.Project, "--path-folder", needed, "--", "demo-mcp.exe"];

        // The register that writes the entry says it already.
        var registered = (await bench.RunAsync(register)).Results[0];

        await Assert.That(registered.Action).IsEqualTo(Change.Added);
        await Assert.That(registered.Advice.Single(item => item.Code is "path-missing").Command).IsEqualTo("registerai path add '" + needed + "'");

        // Not on the PATH at all.
        var missing = (await bench.RunAsync(status)).Results[0].Advice.Single(item => item.Code is "path-missing");

        await Assert.That(missing.Command).IsEqualTo("registerai path add '" + needed + "'");
        await Assert.That(missing.Text).Contains(needed);
        await Assert.That(missing.Text).Contains("registerai path remove");

        // The name is found, but in another folder: the folder asked about is still missing.
        bench.NewProgramPath.Add(elsewhere);

        var shadowed = (await bench.RunAsync(status)).Results[0].Advice;

        await Assert.That(shadowed.Single(item => item.Code is "path-missing").Command).IsEqualTo("registerai path add '" + needed + "'");

        // On the PATH, spelled with a trailing separator: no path-missing advice.
        bench.NewProgramPath.Add(needed + @"\");

        var present = (await bench.RunAsync(status)).Results[0].Advice;

        await Assert.That(present.Select(item => item.Code)).DoesNotContain("path-missing");

        // A relative folder is a usage error, never a guess.
        await Assert.That(CommandLine.Parse(["status", "--name", "demo", "--path-folder", "current"]).Outcome).IsEqualTo(Outcome.Usage);
    }

    /// <summary>A dry run decides and predicts, and runs no write.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ADryRunDecidesAndRunsNoWrite()
    {
        using var bench = Bench.Create("dryrun");

        var result = await bench.RunAsync("register", "--name", "demo", "--client", "all", "--scope", "user", "--owned-root", bench.Root, "--dry-run", "--", bench.Server);

        await Assert.That(result.Results.Select(item => item.Action)).IsEquivalentTo([Change.Added, Change.Added]);
        await Assert.That(result.Results.Select(item => item.After.State)).IsEquivalentTo([State.Ours, State.Ours]);
        await Assert.That(Sequence(bench.Runner.Verbs)).IsEqualTo(ListOnly);
        await Assert.That(File.Exists(bench.ClaudeUserFile)).IsFalse();
    }

    /// <summary>A sequence as one string, so an assertion on it holds the order too.</summary>
    private static string Sequence(IEnumerable<string> items) => string.Join(' ', items);

    /// <summary>A command line with options added before its <c>--</c>, where options belong.</summary>
    private static string[] With(string[] command, params string[] options)
    {
        var separator = Array.IndexOf(command, "--");

        return [.. command[..separator], .. options, .. command[separator..]];
    }
}
