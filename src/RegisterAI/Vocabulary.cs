// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

namespace RegisterAI;

/// <summary>What a client has under a server's name, as this tool judges it.</summary>
internal enum State
{
    /// <summary>No entry of that name.</summary>
    Absent,

    /// <summary>The caller's own entry, naming a file that exists, and naming the command when one was given.</summary>
    Ours,

    /// <summary>The caller's own entry, naming a missing file or something other than the command.</summary>
    OursStale,

    /// <summary>An entry the caller did not write.</summary>
    Foreign,

    /// <summary>The configuration could not be read.</summary>
    Unreadable,

    /// <summary>The client that answers the question was not found.</summary>
    Unknown,
}

/// <summary>What one verb did to one client's entry.</summary>
internal enum Change
{
    /// <summary>Nothing needed doing.</summary>
    None,

    /// <summary>The entry was written where there was none.</summary>
    Added,

    /// <summary>The entry was removed and written again.</summary>
    Replaced,

    /// <summary>The entry was removed.</summary>
    Removed,

    /// <summary>The entry belongs to someone else, so nothing was run.</summary>
    RefusedForeign,

    /// <summary>The configuration could not be read, so nothing was run.</summary>
    RefusedUnreadable,

    /// <summary>The client was not found, so nothing could be run.</summary>
    ClientNotFound,

    /// <summary>A client command failed or timed out, or the result could not be confirmed.</summary>
    Failed,
}

/// <summary>
/// The words this tool publishes. Every one of them is part of the output contract:
/// a caller matches on them, so a change here is a schema change.
/// </summary>
internal static class Vocabulary
{
    /// <summary>The schema number every JSON document carries.</summary>
    public const int Schema = 1;

    /// <summary>The tool's name in every document.</summary>
    public const string Tool = "registerai";

    /// <summary>Each state's word and meaning, in the order help and describe list them.</summary>
    public static IReadOnlyList<(State State, string Word, string Meaning)> States { get; } =
    [
        (State.Absent, "absent", "No entry of that name."),
        (State.Ours, "ours", "The caller's own entry, naming a file that exists (and <command>, when given)."),
        (State.OursStale, "ours-stale", "The caller's own entry, naming a missing file or something other than <command>."),
        (State.Foreign, "foreign", "An entry the caller did not write."),
        (State.Unreadable, "unreadable", "The configuration could not be read. Nothing is assumed."),
        (State.Unknown, "unknown", "The client that answers this question was not found."),
    ];

    /// <summary>Each action's word and meaning.</summary>
    public static IReadOnlyList<(Change Change, string Word, string Meaning)> Actions { get; } =
    [
        (Change.None, "none", "Nothing needed doing, so nothing was run."),
        (Change.Added, "added", "The entry was written where there was none."),
        (Change.Replaced, "replaced", "The entry was removed and written again."),
        (Change.Removed, "removed", "The entry was removed."),
        (Change.RefusedForeign, "refused-foreign", "The entry belongs to someone else. Nothing was run."),
        (Change.RefusedUnreadable, "refused-unreadable", "The configuration could not be read. Nothing was run."),
        (Change.ClientNotFound, "client-not-found", "The client's executable was not found. Nothing was run."),
        (Change.Failed, "failed", "A client command failed or timed out, or the result could not be confirmed."),
    ];

    /// <summary>Each advice code and the condition it reports.</summary>
    public static IReadOnlyList<(string Code, string Meaning)> Advice { get; } =
    [
        ("claude-restart-session", "Claude Code reads its MCP configuration when a session starts; open sessions need a restart."),
        ("claude-approve-project", "Claude Code asks each person to approve a project's server the first time a session opens there."),
        ("codex-new-thread", "Codex does not pick up a change in a thread that is already open."),
        ("codex-trust-project", "Codex reads a project's own configuration only in a project that has been trusted."),
        ("codex-restart-for-path", "Codex finds a bare command on the PATH it started with; one started before a PATH change needs a restart."),
        ("path-missing", "A bare command was found in no folder on the PATH a newly started program gets."),
    ];

    /// <summary>Each exit code, its short name and its meaning.</summary>
    public static IReadOnlyList<(int Code, string Name, string Meaning)> ExitCodes { get; } =
    [
        (0, "done", "Done, or nothing needed doing."),
        (1, "failed", "A client command failed or timed out, or the result could not be confirmed."),
        (2, "usage", "The command line was not understood. Nothing was run."),
        (3, "foreign", "Refused: the entry belongs to someone else."),
        (4, "unreadable", "Refused: a client's configuration could not be read."),
        (5, "client-not-found", "The client was not found. With --client all, only when none was found."),
    ];

    /// <summary>With several clients, the exit code is the first of these that applies.</summary>
    public static IReadOnlyList<int> ExitCodeOrder { get; } = [1, 4, 3, 5, 0];

    /// <summary>A state's published word.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The word.</returns>
    public static string Word(this State state) => States.Single(entry => entry.State == state).Word;

    /// <summary>An action's published word.</summary>
    /// <param name="change">The action.</param>
    /// <returns>The word.</returns>
    public static string Word(this Change change) => Actions.Single(entry => entry.Change == change).Word;
}
