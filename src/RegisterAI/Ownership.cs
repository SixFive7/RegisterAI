// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text;

namespace RegisterAI;

/// <summary>
/// Whose an entry is. One rule for both clients, because two rules are two answers to
/// "may this be deleted", and the cost of their disagreeing is somebody else's entry.
/// </summary>
/// <remarks>
/// <para>
/// An entry is the caller's own when it names exactly the caller's command, or when
/// its command resolves to a file under one of the caller's owned roots. Every other
/// entry is foreign, and no verb changes it. With neither a command nor an owned root
/// every entry is foreign, which is the reading that touches nothing.
/// </para>
/// <para>
/// How a command becomes a file is each client's own: Claude Code expands
/// <c>${NAME}</c> and <c>${NAME:-default}</c>, and Codex expands nothing. A bare file
/// name is looked up on the PATH a newly started program gets, the way both clients
/// start one.
/// </para>
/// </remarks>
internal static class Ownership
{
    /// <summary>Judges an entry against what the caller asked about.</summary>
    /// <param name="entry">The entry, or null when there is none.</param>
    /// <param name="request">The caller's command and owned roots.</param>
    /// <param name="resolvesTo">The file the entry's command resolves to, or null.</param>
    /// <returns>Absent, ours, ours-stale or foreign.</returns>
    public static State Classify(Entry? entry, Request request, string? resolvesTo)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (entry is null)
        {
            return State.Absent;
        }

        if (!entry.IsStdio || entry.Command is not { Length: > 0 } command)
        {
            return State.Foreign;
        }

        var namesCommand = NamesCommand(entry, request);
        var underRoot = resolvesTo is not null && request.OwnedRoots.Any(root => IsUnder(resolvesTo, root));

        if (!namesCommand && !underRoot)
        {
            return State.Foreign;
        }

        // Own, and it names a file that is gone, or a file other than the one asked
        // for: an older install's path, or another file of the same product.
        if (resolvesTo is null || !File.Exists(resolvesTo) || (request.Command is not null && !namesCommand))
        {
            return State.OursStale;
        }

        return State.Ours;
    }

    /// <summary>Whether an entry names exactly the caller's command, case aside.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="request">The request.</param>
    /// <returns>Whether it does.</returns>
    public static bool NamesCommand(Entry? entry, Request request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return entry?.Command is { } command && request.Command is { } wanted && string.Equals(command, wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The file a client would start for a command.</summary>
    /// <param name="command">The command, as the entry spells it.</param>
    /// <param name="client">The client, whose rules turn it into a file.</param>
    /// <param name="machine">Where variables and the PATH are read.</param>
    /// <returns>A full path, or null when the command names no file this tool can find.</returns>
    public static string? Resolve(string? command, ClientId client, Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (command is not { Length: > 0 })
        {
            return null;
        }

        var text = client is ClientId.ClaudeCode ? Expand(command, machine.Variable) : command;

        if (IsBareName(text))
        {
            return Search(text, machine.NewProgramPath());
        }

        try
        {
            return Path.IsPathFullyQualified(text) ? Path.GetFullPath(text) : null;
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Expands <c>${NAME}</c> and <c>${NAME:-default}</c> the way Claude Code does. A
    /// variable that is not set, without a default, stays as written.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="variable">Reads a variable.</param>
    /// <returns>The expanded text.</returns>
    public static string Expand(string value, Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(variable);

        if (!value.Contains("${", StringComparison.Ordinal))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        var index = 0;

        while (index < value.Length)
        {
            var open = value.IndexOf("${", index, StringComparison.Ordinal);
            var close = open < 0 ? -1 : value.IndexOf('}', open + 2);

            if (open < 0 || close < 0)
            {
                _ = result.Append(value, index, value.Length - index);
                break;
            }

            _ = result.Append(value, index, open - index);

            var inner = value[(open + 2)..close];
            var marker = inner.IndexOf(":-", StringComparison.Ordinal);
            var name = marker < 0 ? inner : inner[..marker];
            var found = variable(name);

            _ = result.Append(found switch
            {
                { Length: > 0 } => found,
                _ when marker >= 0 => inner[(marker + 2)..],
                null => value[open..(close + 1)],
                _ => found,
            });

            index = close + 1;
        }

        return result.ToString();
    }

    /// <summary>Whether a command is a file name with no folder, which a client finds through PATH.</summary>
    /// <param name="command">The command.</param>
    /// <returns>Whether it names no folder.</returns>
    public static bool IsBareName(string command) =>
        command.Length > 0 && command.IndexOfAny(['\\', '/', ':']) < 0;

    /// <summary>Whether a path is inside a root folder.</summary>
    /// <param name="path">A full path.</param>
    /// <param name="root">A full path to a folder.</param>
    /// <returns>Whether the path is under it.</returns>
    public static bool IsUnder(string path, string root)
    {
        try
        {
            var prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + '\\';

            return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// The first folder holding the name, or the name with <c>.exe</c> when it has no
    /// extension, which is what starting it would add.
    /// </summary>
    private static string? Search(string name, IReadOnlyList<string> folders)
    {
        string[] names = Path.HasExtension(name) ? [name] : [name, name + ".exe"];

        foreach (var folder in folders)
        {
            foreach (var candidate in names)
            {
                try
                {
                    var path = Path.Combine(folder.Trim().Trim('"'), candidate);

                    if (Path.IsPathFullyQualified(path) && File.Exists(path))
                    {
                        return Path.GetFullPath(path);
                    }
                }
                catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // A PATH entry that is not a path holds nothing.
                }
            }
        }

        return null;
    }
}
