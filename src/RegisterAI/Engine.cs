// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;

namespace RegisterAI;

/// <summary>An entry as a result reports it.</summary>
/// <param name="State">Whose it is.</param>
/// <param name="Command">Its command.</param>
/// <param name="Arguments">Its arguments.</param>
/// <param name="EnvironmentNames">Its environment variables' names.</param>
/// <param name="ResolvesTo">The file its command resolves to.</param>
internal sealed record EntryView(State State, string? Command, IReadOnlyList<string> Arguments, IReadOnlyList<string> EnvironmentNames, string? ResolvesTo);

/// <summary>One client command that ran.</summary>
/// <param name="Argv">The client and its arguments, environment values redacted.</param>
/// <param name="ExitCode">Its exit code, or null.</param>
/// <param name="TimedOut">Whether it was stopped for running too long.</param>
internal sealed record Ran(IReadOnlyList<string> Argv, int? ExitCode, bool TimedOut);

/// <summary>A condition worth telling the caller, with a command that addresses it when there is one.</summary>
/// <param name="Code">The published code.</param>
/// <param name="Text">The sentence.</param>
/// <param name="Command">A command a person can run, or null.</param>
internal sealed record AdviceItem(string Code, string Text, string? Command);

/// <summary>What one verb found and did for one client.</summary>
internal sealed record ClientResult
{
    /// <summary>The client.</summary>
    public required ClientId Client { get; init; }

    /// <summary>The scope.</summary>
    public required Scope Scope { get; init; }

    /// <summary>The project folder, for project scope.</summary>
    public string? Project { get; init; }

    /// <summary>The client executable used, or null when none was found.</summary>
    public string? ClientPath { get; init; }

    /// <summary>The file the entry lives in.</summary>
    public string? Config { get; init; }

    /// <summary>The entry before anything ran. For status, the answer.</summary>
    public required EntryView Before { get; init; }

    /// <summary>What was done.</summary>
    public Change Action { get; init; }

    /// <summary>The entry afterwards: read back after a write, predicted in a dry run.</summary>
    public required EntryView After { get; init; }

    /// <summary>The client commands that ran, reads and writes.</summary>
    public IReadOnlyList<Ran> Ran { get; init; } = [];

    /// <summary>What the client printed while writing.</summary>
    public string? Said { get; init; }

    /// <summary>Conditions worth telling the caller.</summary>
    public IReadOnlyList<AdviceItem> Advice { get; init; } = [];

    /// <summary>The line that makes the same change by hand.</summary>
    public string? Manual { get; init; }

    /// <summary>Why the result is not what was asked for, or null.</summary>
    public string? Error { get; init; }
}

/// <summary>A whole run's results and exit code.</summary>
/// <param name="Results">One per client, in the fixed order.</param>
/// <param name="ExitCode">The exit code.</param>
internal sealed record EngineResult(IReadOnlyList<ClientResult> Results, int ExitCode);

/// <summary>
/// Reads each client's entry, decides, runs the client's own command, reads the entry
/// back, and reports. It never edits a client's file and never touches an entry that
/// is not the caller's own.
/// </summary>
/// <remarks>
/// Every write is followed by a read of the same configuration, and the write counts
/// as done only when that read shows the entry the caller asked for. A client's exit
/// code alone is not trusted: Claude Code exits 1 for every failure it has, a duplicate
/// included, and Codex exits 0 when it removes something that was not there.
/// </remarks>
internal static class Engine
{
    /// <summary>Runs status, register or unregister for every client asked about.</summary>
    /// <param name="request">What was asked.</param>
    /// <param name="machine">The machine.</param>
    /// <returns>The results and the exit code.</returns>
    public static async Task<EngineResult> RunAsync(Request request, Machine machine)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(machine);

        var clock = Stopwatch.StartNew();
        var results = new List<ClientResult>();

        foreach (var client in request.Clients)
        {
            var run = new ClientRun(client, request, machine, clock);
            results.Add(await run.ExecuteAsync().ConfigureAwait(false));
        }

        return new EngineResult(results, ExitCode(request, results));
    }

    /// <summary>
    /// The exit code: the first of 1, 4, 3, 5 and 0 that any client's result calls for,
    /// except that with <c>--client all</c> a client that was not found counts only when
    /// none was found.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="results">The results.</param>
    /// <returns>The exit code.</returns>
    public static int ExitCode(Request request, IReadOnlyList<ClientResult> results)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(results);

        var codes = results.Select(result => CodeOf(request.Verb, result)).ToList();

        foreach (var code in Vocabulary.ExitCodeOrder)
        {
            var applies = code is 5
                ? codes.Contains(5) && (!request.AllClients || codes.All(each => each is 5))
                : code is 0 || codes.Contains(code);

            if (applies)
            {
                return code;
            }
        }

        return 0;
    }

    /// <summary>What one result calls for, alone.</summary>
    /// <param name="verb">The verb.</param>
    /// <param name="result">The result.</param>
    /// <returns>1, 3, 4, 5 or 0.</returns>
    public static int CodeOf(Verb verb, ClientResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (verb is Verb.Status)
        {
            return result.Before.State switch
            {
                State.Unreadable => 4,
                State.Unknown => 5,
                _ => 0,
            };
        }

        return result.Action switch
        {
            Change.Failed => 1,
            Change.RefusedUnreadable => 4,
            Change.RefusedForeign => 3,
            Change.ClientNotFound => 5,
            _ => 0,
        };
    }

    /// <summary>
    /// What a verb does about a state: the state-by-verb table, in one place. A write
    /// that needs a client which was not found becomes <see cref="Change.ClientNotFound"/>
    /// in the caller.
    /// </summary>
    /// <param name="request">The verb and its flags.</param>
    /// <param name="state">The entry's state.</param>
    /// <param name="namesCommand">Whether the entry names exactly the caller's command.</param>
    /// <returns>The action.</returns>
    public static Change Decide(Request request, State state, bool namesCommand)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (request.Verb, state) switch
        {
            (_, State.Unknown) => Change.ClientNotFound,
            (_, State.Unreadable) => Change.RefusedUnreadable,
            (Verb.Register, State.Foreign) => request.TakeOver ? Change.Replaced : Change.RefusedForeign,
            (Verb.Register, State.Absent) => Change.Added,
            (Verb.Register, State.Ours) => request.Replace ? Change.Replaced : Change.None,

            // Own and stale: rewritten, unless it already names the command and only the
            // file is missing, which a rewrite would not change.
            (Verb.Register, State.OursStale) => namesCommand && !request.Replace ? Change.None : Change.Replaced,
            (Verb.Unregister, State.Foreign) => Change.RefusedForeign,
            (Verb.Unregister, State.Absent) => Change.None,
            (Verb.Unregister, State.Ours or State.OursStale) => Change.Removed,
            _ => Change.None,
        };
    }

    /// <summary>One client's part of a run.</summary>
    private sealed class ClientRun(ClientId client, Request request, Machine machine, Stopwatch clock)
    {
        private readonly List<Ran> _ran = [];
        private readonly List<string> _said = [];
        private readonly string? _clientPath = ClientRules.Locate(client, request, machine);
        private readonly string _config = ClientRules.ConfigFile(client, request.Scope, request.Project, machine);

        public async Task<ClientResult> ExecuteAsync()
        {
            var (reading, unknown) = await ReadAsync().ConfigureAwait(false);
            var before = View(reading, unknown);

            if (request.Verb is Verb.Status)
            {
                return Result(before, Change.None, before) with
                {
                    Advice = PathAdvice(before),
                    Error = reading.Unreadable ?? (unknown ? ClientRules.NotFound(client, request, machine, forStatus: true) : null),
                };
            }

            var change = Decide(request, before.State, Ownership.NamesCommand(reading.Entry, request));

            if ((change is Change.Added or Change.Replaced or Change.Removed) && _clientPath is null)
            {
                change = Change.ClientNotFound;
            }

            var manual = ClientRules.Manual(client, request.Scope, request, add: request.Verb is Verb.Register);

            if (change is not (Change.Added or Change.Replaced or Change.Removed))
            {
                return Result(before, change, before) with
                {
                    Manual = manual,
                    Advice = change is Change.None ? PathAdvice(before) : [],
                    Error = change switch
                    {
                        Change.RefusedForeign => Foreign(before),
                        Change.RefusedUnreadable => reading.Unreadable + " Nothing was changed.",
                        Change.ClientNotFound => ClientRules.NotFound(client, request, machine),
                        _ => null,
                    },
                };
            }

            if (request.DryRun)
            {
                var predicted = change is Change.Removed ? Empty(State.Absent) : Wanted();

                return Result(before, change, predicted) with { Manual = manual, Advice = ChangeAdvice(change, predicted) };
            }

            return await WriteAsync(before, change, manual).ConfigureAwait(false);
        }

        private async Task<ClientResult> WriteAsync(EntryView before, Change change, string manual)
        {
            var home = request.Scope is Scope.Project && client is ClientId.Codex ? ClientRules.ProjectHome(request.Project!) : null;
            var createdHome = false;

            // Codex refuses a home that does not exist, and making a folder in somebody's
            // project is a change, so it is made only for a write that needs it.
            if (home is not null && change is not Change.Removed && !Directory.Exists(home))
            {
                _ = Directory.CreateDirectory(home);
                createdHome = true;
            }

            RunOutcome? last = null;

            if (change is Change.Replaced or Change.Removed)
            {
                last = await RunAsync(ClientRules.RemoveArguments(client, request.Scope, request.Name), write: true).ConfigureAwait(false);
            }

            if (change is Change.Added or Change.Replaced)
            {
                last = await RunAsync(ClientRules.AddArguments(client, request.Scope, request), write: true).ConfigureAwait(false);
            }

            var (reading, unknown) = await ReadAsync().ConfigureAwait(false);
            var after = View(reading, unknown);
            var error = Confirm(change, last!, reading, after);

            if (error is not null && createdHome)
            {
                RemoveIfEmpty(home!);
            }

            return Result(before, error is null ? change : Change.Failed, after) with
            {
                Said = _said.Count > 0 ? Redact(string.Join('\n', _said)) : null,
                Manual = manual,
                Advice = error is null ? ChangeAdvice(change, after) : [],
                Error = error,
            };
        }

        /// <summary>Why a write is not confirmed, or null when the entry reads as asked.</summary>
        private string? Confirm(Change change, RunOutcome last, Reading reading, EntryView after)
        {
            var executable = client.Info().Executable;

            if (!last.Succeeded)
            {
                return $"{executable} {last.Ending} while {(change is Change.Removed ? "removing" : "writing")} the entry. What it printed is in 'said'.";
            }

            if (change is Change.Removed)
            {
                return after.State is State.Absent
                    ? null
                    : $"{executable} exited 0, but the entry still reads as {after.State.Word()}{(after.Command is { } left ? " naming '" + left + "'" : string.Empty)}.";
            }

            var entry = reading.Entry;
            var asked = request.Environment.Select(pair => pair.Key).Order(StringComparer.OrdinalIgnoreCase);

            if (reading.Unreadable is { } unreadable)
            {
                return $"{executable} exited 0, but the entry could not be read back: {unreadable}";
            }

            if (entry is null
                || !string.Equals(entry.Command, request.Command, StringComparison.Ordinal)
                || !entry.Arguments.SequenceEqual(request.Arguments, StringComparer.Ordinal)
                || !entry.EnvironmentNames.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(asked, StringComparer.OrdinalIgnoreCase))
            {
                return entry is null
                    ? $"{executable} exited 0, but no entry named '{request.Name}' reads back."
                    : $"{executable} exited 0, but the entry reads back as '{entry.Command}' with {entry.Arguments.Count} argument(s) and {entry.EnvironmentNames.Count} variable(s), not as written.";
            }

            return null;
        }

        private async Task<(Reading Reading, bool Unknown)> ReadAsync()
        {
            if (client is ClientId.ClaudeCode)
            {
                return (Entries.FromClaudeFile(_config, request.Name), false);
            }

            if (_clientPath is null)
            {
                return (Reading.Nothing, true);
            }

            // A project with no Codex home holds no entry, and Codex refuses to be asked
            // about a home that does not exist.
            if (request.Scope is Scope.Project && !Directory.Exists(ClientRules.ProjectHome(request.Project!)))
            {
                return (Reading.Nothing, false);
            }

            var listed = await RunAsync(ClientRules.ListArguments(), write: false).ConfigureAwait(false);

            return listed.Succeeded
                ? (Entries.FromCodexList(listed.Output, request.Name), false)
                : (new Reading(null, $"'codex mcp list --json' {listed.Ending}{(listed.Output.Length > 0 ? ", saying: " + Clip(listed.Output) : string.Empty)}, so what Codex has registered is not known."), false);
        }

        private async Task<RunOutcome> RunAsync(List<string> arguments, bool write)
        {
            var remaining = request.Timeout - clock.Elapsed;
            var argv = new List<string> { _clientPath! };

            // The value after each --env is shown by its name alone.
            for (var index = 0; index < arguments.Count; index++)
            {
                argv.Add(index > 0 && arguments[index - 1] is "--env" && arguments[index].IndexOf('=', StringComparison.Ordinal) is > 0 and var equals
                    ? arguments[index][..equals] + "=<redacted>"
                    : arguments[index]);
            }

            RunOutcome outcome;

            if (remaining <= TimeSpan.Zero)
            {
                outcome = new RunOutcome(null, string.Empty, false, string.Create(CultureInfo.InvariantCulture, $"the run's --timeout of {request.Timeout.TotalSeconds} s was spent before it started"));
            }
            else
            {
                try
                {
                    outcome = await machine.Runner.RunAsync(
                        _clientPath!,
                        arguments,
                        ClientRules.WorkingDirectory(client, request.Scope, request.Project, machine),
                        ClientRules.Environment(client, request.Scope, request.Project),
                        remaining).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    outcome = new RunOutcome(null, string.Empty, false, failure.Message);
                }
            }

            _ran.Add(new Ran(argv, outcome.ExitCode, outcome.TimedOut));

            if (write && outcome.Output.Length > 0)
            {
                _said.Add(outcome.Output);
            }

            if (client is ClientId.Codex && request.Scope is Scope.Project)
            {
                RemoveResidue(ClientRules.ProjectHome(request.Project!));
            }

            return outcome;
        }

        private EntryView View(Reading reading, bool unknown)
        {
            if (unknown)
            {
                return Empty(State.Unknown);
            }

            if (reading.Unreadable is not null)
            {
                return Empty(State.Unreadable);
            }

            var entry = reading.Entry;
            var resolved = Ownership.Resolve(entry?.Command, client, machine);

            return new EntryView(
                Ownership.Classify(entry, request, resolved),
                entry?.Command,
                entry?.Arguments ?? [],
                entry?.EnvironmentNames ?? [],
                resolved);
        }

        /// <summary>The entry the caller asked for, as a dry run predicts it.</summary>
        private EntryView Wanted()
        {
            var wanted = new Entry(request.Command, request.Arguments, [.. request.Environment.Select(pair => pair.Key)], IsStdio: true);
            var resolved = Ownership.Resolve(request.Command, client, machine);

            return new EntryView(Ownership.Classify(wanted, request, resolved), wanted.Command, wanted.Arguments, wanted.EnvironmentNames, resolved);
        }

        private static EntryView Empty(State state) => new(state, null, [], [], null);

        private ClientResult Result(EntryView before, Change change, EntryView after) => new()
        {
            Client = client,
            Scope = request.Scope,
            Project = request.Project,
            ClientPath = _clientPath,
            Config = _config,
            Before = before,
            Action = change,
            After = after,
            Ran = _ran,
        };

        private string Foreign(EntryView before) =>
            before.Command is null
                ? $"The entry named '{request.Name}' is not a local command (it has a URL, another transport or no command), so it is not the caller's. Nothing was changed."
                : $"The entry names '{before.Command}', which is not under an owned root and is not the command given. Nothing was changed.";

        private List<AdviceItem> ChangeAdvice(Change change, EntryView after)
        {
            var advice = new List<AdviceItem>();
            var written = change is not Change.Removed;

            if (client is ClientId.ClaudeCode)
            {
                advice.Add(new("claude-restart-session", "Claude Code reads its MCP configuration when a session starts. Sessions already open do not see this change until they are restarted.", null));

                if (written && request.Scope is Scope.Project)
                {
                    advice.Add(new("claude-approve-project", "Claude Code asks each person to approve this server the first time a session opens in that folder.", null));
                }
            }
            else
            {
                advice.Add(new("codex-new-thread", "Codex does not pick up this change in a thread that is already open. Start a new thread to use it.", null));

                if (written && request.Scope is Scope.Project)
                {
                    advice.Add(new("codex-trust-project", "Codex reads a project's own configuration only in a project it trusts, so this entry does nothing until Codex trusts that folder.", null));
                }
            }

            if (written)
            {
                advice.AddRange(PathAdvice(after));
            }

            return advice;
        }

        /// <summary>
        /// For a bare command: where the PATH a new program gets finds it, or that it finds
        /// it nowhere. RegisterAI never edits PATH; the command offered opens the editor
        /// Windows provides for it.
        /// </summary>
        private List<AdviceItem> PathAdvice(EntryView entry)
        {
            if (entry.Command is not { } command || !Ownership.IsBareName(client is ClientId.ClaudeCode ? Ownership.Expand(command, machine.Variable) : command))
            {
                return [];
            }

            if (entry.ResolvesTo is null)
            {
                return
                [
                    new(
                        "path-missing",
                        $"No folder on the PATH a newly started program gets holds '{command}', so {client.Info().Name} cannot start this entry. RegisterAI does not edit PATH. Put the folder that holds it on your user PATH; the command opens the Windows editor for environment variables.",
                        "rundll32.exe sysdm.cpl,EditEnvironmentVariables"),
                ];
            }

            return client is ClientId.Codex
                ? [new("codex-restart-for-path", $"'{command}' is found on PATH at '{entry.ResolvesTo}'. A Codex started before that folder was on PATH does not find it until Codex is restarted.", null)]
                : [];
        }

        /// <summary>Removes every value given with --env from a client's words. Values shorter than three characters are left, because they would match too much.</summary>
        private string Redact(string text) =>
            request.Environment
                .Where(pair => pair.Value.Length >= 3)
                .Aggregate(text, (current, pair) => current.Replace(pair.Value, "<redacted>", StringComparison.Ordinal));

        private static string Clip(string text) => text.Length <= 300 ? text : text[..300] + " (cut)";

        /// <summary>
        /// Removes the empty <c>tmp\arg0</c> folders a Codex run leaves in a project's
        /// home, innermost first and only while empty, and never above the home.
        /// </summary>
        private static void RemoveResidue(string home)
        {
            foreach (var folder in new[] { Path.Combine(home, "tmp", "arg0"), Path.Combine(home, "tmp") })
            {
                if (!RemoveIfEmpty(folder))
                {
                    return;
                }
            }
        }

        private static bool RemoveIfEmpty(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder);
                }

                return true;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // Not empty, or not this tool's to remove: left as it is.
                return false;
            }
        }
    }
}
