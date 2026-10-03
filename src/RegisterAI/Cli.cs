// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

namespace RegisterAI;

/// <summary>Reads the command line and writes the one document the run produces.</summary>
internal static class Cli
{
    /// <summary>Runs one command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="terminal">Where the document goes.</param>
    /// <param name="machine">The machine the clients are on.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, Terminal terminal, Machine machine)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(machine);

        var parsed = CommandLine.Parse(args);

        switch (parsed.Outcome)
        {
            case Outcome.Version:
                terminal.WriteText(ToolVersion.Text + "\n");
                return 0;

            case Outcome.Help:
                terminal.WriteText(parsed switch
                {
                    { Topics: { } topics } => string.Join('\n', topics.Select(HelpText.For)),
                    { Verb: { } verb } => HelpText.For(verb),
                    _ => HelpText.Full(),
                });
                return parsed.ExitCode;

            case Outcome.Usage:
                Report.WriteUsage(terminal, parsed.Verb?.Name, parsed.Error!);
                return 2;

            default:
                break;
        }

        var request = parsed.Request!;

        switch (request.Verb)
        {
            case Verb.Describe:
                Describe.Write(terminal);
                return 0;

            case Verb.License:
                terminal.WriteText(LicenseText.Read());
                return 0;

            case Verb.PathAdd or Verb.PathRemove:
                return RunPath(request, terminal, machine);

            default:
                break;
        }

        EngineResult result;

        try
        {
            result = await Engine.RunAsync(request, machine).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The run's boundary: a defect in this tool is reported as a document and exit code 1, never as a crash with nothing on stdout.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            terminal.WriteDiagnostic(failure + "\n");
            Report.WriteStopped(terminal, request, 1, $"RegisterAI stopped on an unexpected error: {failure.GetType().Name}: {failure.Message}");
            return 1;
        }

        Report.Write(terminal, request, result);
        return result.ExitCode;
    }

    private static int RunPath(Request request, Terminal terminal, Machine machine)
    {
        PathReport report;

        try
        {
            report = PathVerbs.Run(request, machine);
        }
#pragma warning disable CA1031 // The same boundary as the engine's: a defect is a document and exit code 1, never a crash with nothing on stdout.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            terminal.WriteDiagnostic(failure + "\n");
            Report.WriteStopped(terminal, request, 1, $"RegisterAI stopped on an unexpected error: {failure.GetType().Name}: {failure.Message}");
            return 1;
        }

        Report.WritePath(terminal, request, report);
        return report.ExitCode;
    }
}
