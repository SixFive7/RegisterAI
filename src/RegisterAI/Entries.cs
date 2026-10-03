// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace RegisterAI;

/// <summary>One server entry, as a client holds it.</summary>
/// <param name="Command">The command, or null when the entry names none.</param>
/// <param name="Arguments">Its arguments.</param>
/// <param name="EnvironmentNames">The names of its environment variables. Values are never kept.</param>
/// <param name="IsStdio">Whether it is a local command at all: an entry with a URL or another transport is not.</param>
internal sealed record Entry(string? Command, IReadOnlyList<string> Arguments, IReadOnlyList<string> EnvironmentNames, bool IsStdio);

/// <summary>What reading a client's configuration found.</summary>
/// <param name="Entry">The entry under the name, or null when there is none.</param>
/// <param name="Unreadable">Why the configuration could not be read, when it could not.</param>
internal sealed record Reading(Entry? Entry, string? Unreadable)
{
    /// <summary>Nothing under the name.</summary>
    public static Reading Nothing { get; } = new(null, null);
}

/// <summary>
/// Reads an entry the way each client keeps it, and never writes. Claude Code's
/// configuration is a JSON file this tool reads; Codex's is asked of Codex itself.
/// </summary>
/// <remarks>
/// A configuration that cannot be read is never reported as holding nothing. The two
/// are different answers and only one of them permits writing: a reader that turned
/// a locked or half-written file into "nothing registered" would let register write
/// over whatever is really there.
/// </remarks>
internal static class Entries
{
    private static readonly string[] CodexWrappers = ["servers", "mcp_servers", "mcpServers"];

    /// <summary>Reads Claude Code's entry for a name from one of its files.</summary>
    /// <param name="file">The file: a user configuration or a project's <c>.mcp.json</c>.</param>
    /// <param name="name">The server's name.</param>
    /// <returns>What the file holds.</returns>
    public static Reading FromClaudeFile(string file, string name)
    {
        string text;

        try
        {
            if (!File.Exists(file))
            {
                return Reading.Nothing;
            }

            // The client may hold the file open; sharing read, write and delete
            // keeps this reader from failing because a session is running.
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Terminal.Utf8, detectEncodingFromByteOrderMarks: true);

            text = reader.ReadToEnd();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new Reading(null, $"'{file}' could not be read ({failure.Message}), so what is registered there is not known.");
        }

        // A file that exists and is empty is what a write cut short leaves. The client
        // never writes one, so it is not taken to mean that nothing is registered.
        if (text.Trim().Length is 0)
        {
            return new Reading(null, $"'{file}' exists and is empty, so what is registered there is not known.");
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object)
            {
                return new Reading(null, $"'{file}' does not hold a JSON object, so what is registered there is not known.");
            }

            if (!root.TryGetProperty("mcpServers", out var servers))
            {
                return Reading.Nothing;
            }

            if (servers.ValueKind is not JsonValueKind.Object)
            {
                return new Reading(null, $"'mcpServers' in '{file}' is not an object, so what is registered there is not known.");
            }

            return servers.TryGetProperty(name, out var entry) ? new Reading(ClaudeEntry(entry), null) : Reading.Nothing;
        }
        catch (JsonException failure)
        {
            return new Reading(null, $"'{file}' is not readable JSON ({failure.Message}), so what is registered there is not known.");
        }
    }

    /// <summary>Reads Codex's answer to <c>mcp list --json</c>.</summary>
    /// <param name="json">What Codex printed.</param>
    /// <param name="name">The server's name.</param>
    /// <returns>What Codex holds.</returns>
    /// <remarks>
    /// A bare array is the shape measured; an object wrapping the array under
    /// <c>servers</c>, <c>mcp_servers</c> or <c>mcpServers</c> is accepted as well, so
    /// that a client release which adds a wrapper reads as a change of shape and not as
    /// an empty configuration.
    /// </remarks>
    public static Reading FromCodexList(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            JsonElement? servers = root.ValueKind is JsonValueKind.Array ? root : null;

            if (root.ValueKind is JsonValueKind.Object)
            {
                foreach (var wrapper in CodexWrappers)
                {
                    if (root.TryGetProperty(wrapper, out var wrapped) && wrapped.ValueKind is JsonValueKind.Array)
                    {
                        servers = wrapped;
                        break;
                    }
                }
            }

            if (servers is not { } list)
            {
                return new Reading(null, "'codex mcp list --json' printed JSON without a list of servers, so what Codex has registered is not known.");
            }

            foreach (var server in list.EnumerateArray())
            {
                if (server.ValueKind is JsonValueKind.Object
                    && server.TryGetProperty("name", out var listed)
                    && listed.ValueKind is JsonValueKind.String
                    && string.Equals(listed.GetString(), name, StringComparison.Ordinal))
                {
                    return new Reading(CodexEntry(server), null);
                }
            }

            return Reading.Nothing;
        }
        catch (JsonException failure)
        {
            return new Reading(null, $"'codex mcp list --json' did not print readable JSON ({failure.Message}), so what Codex has registered is not known.");
        }
    }

    private static Entry ClaudeEntry(JsonElement entry)
    {
        if (entry.ValueKind is not JsonValueKind.Object)
        {
            return new Entry(null, [], [], IsStdio: false);
        }

        // An entry with a URL, or with a transport other than stdio, is not a local
        // command, whatever its name.
        var stdio = (!entry.TryGetProperty("type", out var type) || type.ValueKind is not JsonValueKind.String || type.GetString() is "stdio")
            && !entry.TryGetProperty("url", out _);

        return new Entry(Text(entry, "command"), Strings(entry, "args"), Names(entry, "env"), stdio);
    }

    private static Entry CodexEntry(JsonElement server)
    {
        var transport = server.TryGetProperty("transport", out var nested) && nested.ValueKind is JsonValueKind.Object
            ? nested
            : server;

        var stdio = (!transport.TryGetProperty("type", out var type) || type.ValueKind is not JsonValueKind.String || type.GetString() is "stdio")
            && !transport.TryGetProperty("url", out _);

        return new Entry(Text(transport, "command"), Strings(transport, "args"), Names(transport, "env"), stdio);
    }

    private static string? Text(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static List<string> Strings(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.Array
            ? [.. value.EnumerateArray().Select(item => item.ValueKind is JsonValueKind.String ? item.GetString()! : item.GetRawText())]
            : [];

    private static List<string> Names(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.Object
            ? [.. value.EnumerateObject().Select(variable => variable.Name)]
            : [];
}
