// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace RegisterAI;

/// <summary>
/// The contract as JSON, for a program or an agent: the verbs and options from the
/// table the parser reads, the clients, the published words, the exit codes and the
/// schema of every other document.
/// </summary>
internal static class Describe
{
    /// <summary>
    /// The JSON Schema of the documents status, register and unregister write, and of a
    /// usage error. The quoted placeholders are replaced with the published words, so
    /// the schema cannot list a word the tool does not use.
    /// </summary>
    private const string OutputSchema =
        """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "title": "RegisterAI status, register and unregister documents, and usage errors",
          "type": "object",
          "required": ["tool", "version", "schema", "verb", "dryRun", "exitCode", "error", "server", "results"],
          "properties": {
            "tool": { "const": "registerai" },
            "version": { "type": "string" },
            "schema": { "const": 1 },
            "verb": { "type": ["string", "null"], "description": "null when a usage error named no verb." },
            "dryRun": { "type": "boolean" },
            "exitCode": { "enum": "@EXITCODES@" },
            "error": { "type": ["string", "null"], "description": "Why the whole run failed, or null." },
            "server": {
              "oneOf": [
                { "type": "null" },
                {
                  "type": "object",
                  "required": ["name", "command", "args", "env"],
                  "properties": {
                    "name": { "type": "string" },
                    "command": { "type": ["string", "null"], "description": "The command after --, exactly as it arrived." },
                    "args": { "type": "array", "items": { "type": "string" } },
                    "env": { "type": "array", "items": { "type": "string" }, "description": "The names given with --env. Values are never echoed." }
                  }
                }
              ]
            },
            "results": {
              "type": "array",
              "description": "One per client, claude-code first. status writes statusResult, register and unregister write changeResult.",
              "items": { "oneOf": [{ "$ref": "#/$defs/statusResult" }, { "$ref": "#/$defs/changeResult" }] }
            }
          },
          "$defs": {
            "entry": {
              "type": "object",
              "required": ["state", "command", "args", "env", "resolvesTo"],
              "properties": {
                "state": { "enum": "@STATES@" },
                "command": { "type": ["string", "null"] },
                "args": { "type": "array", "items": { "type": "string" } },
                "env": { "type": "array", "items": { "type": "string" }, "description": "Variable names only." },
                "resolvesTo": { "type": ["string", "null"], "description": "The file the command names after the client's own expansion and a PATH search for a bare name." }
              }
            },
            "advice": {
              "type": "object",
              "required": ["code", "text", "command"],
              "properties": {
                "code": { "enum": "@ADVICE@" },
                "text": { "type": "string" },
                "command": { "type": ["string", "null"], "description": "A command a person can run about it, or null." }
              }
            },
            "ran": {
              "type": "object",
              "required": ["argv", "exitCode", "timedOut"],
              "properties": {
                "argv": { "type": "array", "items": { "type": "string" }, "description": "The client and its arguments, with --env values replaced by <redacted>." },
                "exitCode": { "type": ["integer", "null"] },
                "timedOut": { "type": "boolean" }
              }
            },
            "place": {
              "type": "object",
              "required": ["client", "scope", "project", "clientPath", "config"],
              "properties": {
                "client": { "enum": "@CLIENTS@" },
                "scope": { "enum": ["user", "project"] },
                "project": { "type": ["string", "null"] },
                "clientPath": { "type": ["string", "null"], "description": "The client executable used, or null when none was found." },
                "config": { "type": ["string", "null"], "description": "The file the entry lives in." }
              }
            },
            "statusResult": {
              "allOf": [{ "$ref": "#/$defs/place" }],
              "type": "object",
              "required": ["state", "command", "args", "env", "resolvesTo", "advice", "error"],
              "properties": {
                "state": { "enum": "@STATES@" },
                "command": { "type": ["string", "null"] },
                "args": { "type": "array", "items": { "type": "string" } },
                "env": { "type": "array", "items": { "type": "string" } },
                "resolvesTo": { "type": ["string", "null"] },
                "advice": { "type": "array", "items": { "$ref": "#/$defs/advice" } },
                "error": { "type": ["string", "null"] }
              }
            },
            "changeResult": {
              "allOf": [{ "$ref": "#/$defs/place" }],
              "type": "object",
              "required": ["before", "action", "after", "ran", "said", "advice", "manual", "error"],
              "properties": {
                "before": { "$ref": "#/$defs/entry" },
                "action": { "enum": "@ACTIONS@" },
                "after": { "$ref": "#/$defs/entry", "description": "Read back after a write; predicted in a dry run." },
                "ran": { "type": "array", "items": { "$ref": "#/$defs/ran" } },
                "said": { "type": ["string", "null"], "description": "What the client printed while writing, with --env values redacted." },
                "advice": { "type": "array", "items": { "$ref": "#/$defs/advice" } },
                "manual": { "type": ["string", "null"], "description": "The PowerShell line that makes the same change by hand, with --env values as <value>." },
                "error": { "type": ["string", "null"] }
              }
            }
          }
        }
        """;

    /// <summary>The schema with the published words in place.</summary>
    /// <returns>The JSON text.</returns>
    public static string Schema() =>
        OutputSchema
            .Replace("\"@EXITCODES@\"", "[" + string.Join(", ", Vocabulary.ExitCodes.Select(code => code.Code)) + "]", StringComparison.Ordinal)
            .Replace("\"@STATES@\"", Array(Vocabulary.States.Select(state => state.Word)), StringComparison.Ordinal)
            .Replace("\"@ACTIONS@\"", Array(Vocabulary.Actions.Select(action => action.Word)), StringComparison.Ordinal)
            .Replace("\"@ADVICE@\"", Array(Vocabulary.Advice.Select(advice => advice.Code)), StringComparison.Ordinal)
            .Replace("\"@CLIENTS@\"", Array(Clients.All.Select(client => client.Word)), StringComparison.Ordinal);

    /// <summary>Writes the describe document.</summary>
    /// <param name="terminal">Where it goes.</param>
    public static void Write(Terminal terminal) =>
        Json.Write(terminal, writer =>
        {
            writer.WriteStartObject();
            writer.WriteHeader("describe");
            writer.WriteString("summary", "Registers a local MCP server with coding agents by running each agent's own command. It never edits an agent's configuration file, never prompts, and never reads stdin.");

            writer.WriteStartArray("verbs");
            foreach (var verb in CommandLine.Verbs)
            {
                WriteVerb(writer, verb);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("clients");
            foreach (var client in Clients.All)
            {
                writer.WriteStartObject();
                writer.WriteString("id", client.Word);
                writer.WriteString("name", client.Name);
                writer.WriteString("executable", client.Executable);
                writer.WriteStrings("scopes", ["user", "project"]);
                writer.WriteString("writes", Clients.Writes);
                writer.WriteString("reads", client.Reads);
                writer.WriteStrings("expandsVariables", client.ExpandsVariables);
                writer.WriteStartObject("config");
                writer.WriteString("user", client.UserConfig);
                writer.WriteString("project", client.ProjectConfig);
                writer.WriteEndObject();
                writer.WriteStrings("search", client.Search);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            WriteWords(writer, "states", Vocabulary.States.Select(state => (state.Word, state.Meaning)));
            WriteWords(writer, "actions", Vocabulary.Actions.Select(action => (action.Word, action.Meaning)));
            WriteWords(writer, "advice", Vocabulary.Advice.Select(advice => (advice.Code, advice.Meaning)));

            writer.WriteStartArray("exitCodes");
            foreach (var (code, name, meaning) in Vocabulary.ExitCodes)
            {
                writer.WriteStartObject();
                writer.WriteNumber("code", code);
                writer.WriteString("name", name);
                writer.WriteString("meaning", meaning);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("exitCodeOrder");
            foreach (var code in Vocabulary.ExitCodeOrder)
            {
                writer.WriteNumberValue(code);
            }

            writer.WriteEndArray();

            writer.WritePropertyName("output");
            using (var schema = JsonDocument.Parse(Schema()))
            {
                schema.RootElement.WriteTo(writer);
            }

            writer.WriteEndObject();
        });

    private static void WriteVerb(Utf8JsonWriter writer, VerbSpec verb)
    {
        writer.WriteStartObject();
        writer.WriteString("name", verb.Name);
        writer.WriteBoolean("writes", verb.Writes);
        writer.WriteString("summary", verb.Summary);
        writer.WriteString("usage", ("registerai " + verb.Name + " " + verb.Usage).TrimEnd());

        writer.WriteStartArray("options");
        foreach (var option in CommandLine.Options.Where(option => verb.Required.Contains(option.Name) || verb.Optional.Contains(option.Name)))
        {
            writer.WriteStartObject();
            writer.WriteString("name", option.Name);
            writer.WriteNullableString("value", option.Value);
            writer.WriteBoolean("required", verb.Required.Contains(option.Name));
            writer.WriteNullableString("requiredWhen", option.Name is "--project" ? "--scope project" : null);
            writer.WriteBoolean("repeatable", option.Repeatable);

            if (option.Values is { } values)
            {
                writer.WriteStrings("values", values);
            }
            else
            {
                writer.WriteNull("values");
            }

            writer.WriteString("summary", option.Summary);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteString("tail", verb.Tail switch
        {
            Tail.Required => "required",
            Tail.Optional => "optional",
            _ => "none",
        });

        writer.WriteStartArray("examples");
        foreach (var example in verb.Examples)
        {
            writer.WriteStartArray();
            foreach (var argument in example)
            {
                writer.WriteStringValue(argument);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteWords(Utf8JsonWriter writer, string name, IEnumerable<(string Word, string Meaning)> words)
    {
        writer.WriteStartArray(name);

        foreach (var (word, meaning) in words)
        {
            writer.WriteStartObject();
            writer.WriteString("name", word);
            writer.WriteString("meaning", meaning);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    // The published words are lower-case letters, digits and hyphens, so quoting them
    // needs no escaping.
    private static string Array(IEnumerable<string> words) =>
        "[" + string.Join(", ", words.Select(word => "\"" + word + "\"")) + "]";
}
