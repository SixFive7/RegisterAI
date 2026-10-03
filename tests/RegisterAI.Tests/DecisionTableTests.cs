// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using RegisterAI.Tests.Harness;

namespace RegisterAI.Tests;

/// <summary>
/// The state-by-verb table as one test, the published words, and the exit-code order.
/// </summary>
internal sealed class DecisionTableTests
{
    /// <summary>
    /// Every state against every verb and flag that changes the answer: the action and
    /// the exit code that follow.
    /// </summary>
    /// <returns>The rows.</returns>
    public static IEnumerable<Func<(Verb Verb, State State, string Flags, bool NamesCommand, Change Action, int ExitCode)>> Table()
    {
        // status reads only; its exit code follows the state.
        yield return () => (Verb.Status, State.Absent, "", false, Change.None, 0);
        yield return () => (Verb.Status, State.Ours, "", true, Change.None, 0);
        yield return () => (Verb.Status, State.OursStale, "", false, Change.None, 0);
        yield return () => (Verb.Status, State.Foreign, "", false, Change.None, 0);
        yield return () => (Verb.Status, State.Unreadable, "", false, Change.None, 4);
        yield return () => (Verb.Status, State.Unknown, "", false, Change.None, 5);

        yield return () => (Verb.Register, State.Absent, "", false, Change.Added, 0);
        yield return () => (Verb.Register, State.Ours, "", true, Change.None, 0);
        yield return () => (Verb.Register, State.Ours, "replace", true, Change.Replaced, 0);
        yield return () => (Verb.Register, State.OursStale, "", true, Change.None, 0);
        yield return () => (Verb.Register, State.OursStale, "replace", true, Change.Replaced, 0);
        yield return () => (Verb.Register, State.OursStale, "", false, Change.Replaced, 0);
        yield return () => (Verb.Register, State.Foreign, "", false, Change.RefusedForeign, 3);
        yield return () => (Verb.Register, State.Foreign, "replace", false, Change.RefusedForeign, 3);
        yield return () => (Verb.Register, State.Foreign, "take-over", false, Change.Replaced, 0);
        yield return () => (Verb.Register, State.Unreadable, "take-over", false, Change.RefusedUnreadable, 4);
        yield return () => (Verb.Register, State.Unknown, "", false, Change.ClientNotFound, 5);

        yield return () => (Verb.Unregister, State.Absent, "", false, Change.None, 0);
        yield return () => (Verb.Unregister, State.Ours, "", true, Change.Removed, 0);
        yield return () => (Verb.Unregister, State.OursStale, "", false, Change.Removed, 0);
        yield return () => (Verb.Unregister, State.Foreign, "", false, Change.RefusedForeign, 3);
        yield return () => (Verb.Unregister, State.Unreadable, "", false, Change.RefusedUnreadable, 4);
        yield return () => (Verb.Unregister, State.Unknown, "", false, Change.ClientNotFound, 5);
    }

    /// <summary>The state-by-verb table.</summary>
    /// <param name="row">One state, one verb, its flags, and what must follow.</param>
    /// <returns>The test.</returns>
    [Test]
    [MethodDataSource(nameof(Table))]
    public async Task TheStateByVerbTable((Verb Verb, State State, string Flags, bool NamesCommand, Change Action, int ExitCode) row)
    {
        var request = new Request
        {
            Verb = row.Verb,
            Name = "demo",
            Clients = [ClientId.ClaudeCode],
            Replace = row.Flags is "replace",
            TakeOver = row.Flags is "take-over",
        };

        var action = row.Verb is Verb.Status ? Change.None : Engine.Decide(request, row.State, row.NamesCommand);
        var view = new EntryView(row.State, null, [], [], null);
        var result = new ClientResult { Client = ClientId.ClaudeCode, Scope = Scope.User, Before = view, Action = action, After = view };

        await Assert.That(action).IsEqualTo(row.Action);
        await Assert.That(Engine.ExitCode(request, [result])).IsEqualTo(row.ExitCode);
    }

    /// <summary>
    /// The published words, exactly. A caller matches on them, so changing one is a
    /// schema change and has to show up as a red test first.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ThePublishedWordsAreFixed()
    {
        await Assert.That(string.Join(' ', Vocabulary.States.Select(state => state.Word)))
            .IsEqualTo("absent ours ours-stale foreign unreadable unknown");
        await Assert.That(string.Join(' ', Vocabulary.Actions.Select(action => action.Word)))
            .IsEqualTo("none added replaced removed refused-foreign refused-unreadable client-not-found failed");
        await Assert.That(string.Join(' ', Vocabulary.Advice.Select(advice => advice.Code)))
            .IsEqualTo("claude-restart-session claude-approve-project codex-new-thread codex-trust-project codex-restart-for-path path-missing");
        await Assert.That(string.Join(' ', Vocabulary.ExitCodes.Select(code => code.Code + "=" + code.Name)))
            .IsEqualTo("0=done 1=failed 2=usage 3=foreign 4=unreadable 5=client-not-found");
        await Assert.That(string.Join(' ', Vocabulary.ExitCodeOrder)).IsEqualTo("1 4 3 5 0");
        await Assert.That(Describe.Schema()).Contains("\"schema\": { \"const\": 1 }");
        await Assert.That(string.Join(", ", CommandLine.Verbs.Select(verb => verb.Name)))
            .IsEqualTo("status, register, unregister, path add, path remove, describe, license, help");

        // Every enum member has its word, and no word is used twice.
        await Assert.That(Vocabulary.States.Select(state => state.State).Distinct().Count()).IsEqualTo(Enum.GetValues<State>().Length);
        await Assert.That(Vocabulary.Actions.Select(action => action.Change).Distinct().Count()).IsEqualTo(Enum.GetValues<Change>().Length);
    }

    /// <summary>
    /// With several clients the code is the first of 1, 4, 3, 5 and 0 that applies, and
    /// with --client all a missing client counts only when every client is missing.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task SeveralClientsTakeTheFirstCodeThatAppliesAndAllForgivesOneMissingClient()
    {
        static ClientResult of(Change change)
        {
            var view = new EntryView(State.Absent, null, [], [], null);

            return new ClientResult { Client = ClientId.ClaudeCode, Scope = Scope.User, Before = view, Action = change, After = view };
        }

        var named = Bench.Parse("register", "--name", "demo", "--client", "claude-code", "--client", "codex", "--scope", "user", "--", "x.exe");
        var all = named with { AllClients = true };

        await Assert.That(Engine.ExitCode(named, [of(Change.RefusedForeign), of(Change.Failed)])).IsEqualTo(1);
        await Assert.That(Engine.ExitCode(named, [of(Change.RefusedForeign), of(Change.RefusedUnreadable)])).IsEqualTo(4);
        await Assert.That(Engine.ExitCode(named, [of(Change.ClientNotFound), of(Change.RefusedForeign)])).IsEqualTo(3);
        await Assert.That(Engine.ExitCode(named, [of(Change.Added), of(Change.ClientNotFound)])).IsEqualTo(5);
        await Assert.That(Engine.ExitCode(all, [of(Change.Added), of(Change.ClientNotFound)])).IsEqualTo(0);
        await Assert.That(Engine.ExitCode(all, [of(Change.ClientNotFound), of(Change.ClientNotFound)])).IsEqualTo(5);
        await Assert.That(Engine.ExitCode(all, [of(Change.None), of(Change.Removed)])).IsEqualTo(0);
    }
}
