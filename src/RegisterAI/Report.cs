// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace RegisterAI;

/// <summary>The documents status, register, unregister and a usage error write.</summary>
internal static class Report
{
    /// <summary>A command line that was not understood. Nothing was run.</summary>
    /// <param name="terminal">Where it goes.</param>
    /// <param name="verb">The verb, when one was recognised.</param>
    /// <param name="error">What was wrong, and what to run instead.</param>
    public static void WriteUsage(Terminal terminal, string? verb, string error) =>
        Json.Write(terminal, writer =>
        {
            writer.WriteStartObject();
            writer.WriteHeader(verb);
            writer.WriteBoolean("dryRun", false);
            writer.WriteNumber("exitCode", 2);
            writer.WriteString("error", error);
            writer.WriteNull("server");
            writer.WriteStartArray("results");
            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    /// <summary>A run that stopped before any client was asked.</summary>
    /// <param name="terminal">Where it goes.</param>
    /// <param name="request">What was asked.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="error">Why it stopped.</param>
    public static void WriteStopped(Terminal terminal, Request request, int exitCode, string error) =>
        Json.Write(terminal, writer =>
        {
            ArgumentNullException.ThrowIfNull(request);

            writer.WriteStartObject();
            writer.WriteHeader(Name(request.Verb));
            writer.WriteBoolean("dryRun", request.DryRun);
            writer.WriteNumber("exitCode", exitCode);
            writer.WriteString("error", error);
            WriteServer(writer, request);
            writer.WriteStartArray("results");
            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    /// <summary>A run's results.</summary>
    /// <param name="terminal">Where it goes.</param>
    /// <param name="request">What was asked.</param>
    /// <param name="result">What was found and done.</param>
    public static void Write(Terminal terminal, Request request, EngineResult result) =>
        Json.Write(terminal, writer =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(result);

            writer.WriteStartObject();
            writer.WriteHeader(Name(request.Verb));
            writer.WriteBoolean("dryRun", request.DryRun);
            writer.WriteNumber("exitCode", result.ExitCode);
            writer.WriteNull("error");
            WriteServer(writer, request);
            writer.WriteStartArray("results");

            foreach (var item in result.Results)
            {
                writer.WriteStartObject();
                writer.WriteString("client", item.Client.Info().Word);
                writer.WriteString("scope", item.Scope.Word());
                writer.WriteNullableString("project", item.Project);
                writer.WriteNullableString("clientPath", item.ClientPath);
                writer.WriteNullableString("config", item.Config);

                if (request.Verb is Verb.Status)
                {
                    WriteEntryFields(writer, item.Before);
                }
                else
                {
                    writer.WriteStartObject("before");
                    WriteEntryFields(writer, item.Before);
                    writer.WriteEndObject();
                    writer.WriteString("action", item.Action.Word());
                    writer.WriteStartObject("after");
                    WriteEntryFields(writer, item.After);
                    writer.WriteEndObject();
                    writer.WriteStartArray("ran");

                    foreach (var ran in item.Ran)
                    {
                        writer.WriteStartObject();
                        writer.WriteStrings("argv", ran.Argv);

                        if (ran.ExitCode is { } code)
                        {
                            writer.WriteNumber("exitCode", code);
                        }
                        else
                        {
                            writer.WriteNull("exitCode");
                        }

                        writer.WriteBoolean("timedOut", ran.TimedOut);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteNullableString("said", item.Said);
                }

                writer.WriteStartArray("advice");

                foreach (var advice in item.Advice)
                {
                    writer.WriteStartObject();
                    writer.WriteString("code", advice.Code);
                    writer.WriteString("text", advice.Text);
                    writer.WriteNullableString("command", advice.Command);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();

                if (request.Verb is not Verb.Status)
                {
                    writer.WriteNullableString("manual", item.Manual);
                }

                writer.WriteNullableString("error", item.Error);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    /// <summary>The server block: what arrived, with environment values left out.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="request">What was asked.</param>
    public static void WriteServer(Utf8JsonWriter writer, Request request)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);

        writer.WriteStartObject("server");
        writer.WriteString("name", request.Name);
        writer.WriteNullableString("command", request.Command);
        writer.WriteStrings("args", request.Arguments);
        writer.WriteStrings("env", request.Environment.Select(pair => pair.Key));
        writer.WriteEndObject();
    }

    private static void WriteEntryFields(Utf8JsonWriter writer, EntryView entry)
    {
        writer.WriteString("state", entry.State.Word());
        writer.WriteNullableString("command", entry.Command);
        writer.WriteStrings("args", entry.Arguments);
        writer.WriteStrings("env", entry.EnvironmentNames);
        writer.WriteNullableString("resolvesTo", entry.ResolvesTo);
    }

    /// <summary>A verb's word.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>The word.</returns>
    public static string Name(Verb verb) => CommandLine.Verbs.Single(spec => spec.Verb == verb).Name;
}
