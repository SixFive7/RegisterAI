// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Diagnostics;

namespace RegisterAI;

/// <summary>What one client invocation did.</summary>
/// <param name="ExitCode">The exit code, or null when it did not run to an exit.</param>
/// <param name="Output">Both streams, interleaved as read, trimmed.</param>
/// <param name="TimedOut">Whether it ran past its budget and was stopped.</param>
/// <param name="Failure">Why it could not be started, when that is what happened.</param>
internal sealed record RunOutcome(int? ExitCode, string Output, bool TimedOut, string? Failure)
{
    /// <summary>Whether it ran to completion and exited zero.</summary>
    public bool Succeeded => !TimedOut && Failure is null && ExitCode is 0;

    /// <summary>One clause saying how it ended, for an error sentence.</summary>
    public string Ending => this switch
    {
        { TimedOut: true } => "did not finish in time and was stopped",
        { Failure: { } failure } => "could not be started: " + failure,
        _ => "exited " + ExitCode,
    };
}

/// <summary>Starts a client and waits for it within a budget.</summary>
internal interface IRunner
{
    /// <summary>Runs a client to completion, or stops it when the budget runs out.</summary>
    /// <param name="executable">The client, a full path.</param>
    /// <param name="arguments">Its arguments, passed one by one.</param>
    /// <param name="workingDirectory">Where it runs. For Claude Code's project scope this decides which folder is written.</param>
    /// <param name="environment">Variables to set on top of this process's own. For Codex's project scope this decides which configuration is written.</param>
    /// <param name="budget">How long it may run.</param>
    /// <returns>What it did. Never throws for a client that fails.</returns>
    Task<RunOutcome> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string> environment, TimeSpan budget);
}

/// <summary>
/// The real runner: the client is started directly and never through a shell, so a
/// path with a space, an ampersand or a percent sign reaches it as one argument and
/// unchanged.
/// </summary>
internal sealed class ProcessRunner : IRunner
{
    /// <inheritdoc/>
    public async Task<RunOutcome> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,

            // Without these a non-ASCII path in the client's answer is decoded with
            // the console code page and reads as somebody else's path.
            StandardOutputEncoding = Terminal.Utf8,
            StandardErrorEncoding = Terminal.Utf8,
            WorkingDirectory = workingDirectory,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = new Process { StartInfo = start };

        try
        {
            _ = process.Start();
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
        {
            return new RunOutcome(null, string.Empty, false, failure.Message);
        }

        // A client that asks a question gets end of input at once.
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var timedOut = false;

        using (var limit = new CancellationTokenSource(budget))
        {
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                Stop(process);
            }
        }

        var said = await Collect(output, error).ConfigureAwait(false);

        return timedOut
            ? new RunOutcome(null, said, true, null)
            : new RunOutcome(process.ExitCode, said, false, null);
    }

    /// <summary>
    /// What the two streams held. A child of the client that kept the pipes open would
    /// hold them forever, so the wait is bounded.
    /// </summary>
    private static async Task<string> Collect(Task<string> output, Task<string> error)
    {
        try
        {
            var both = await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            return string.Join('\n', both.Where(text => text.Trim().Length > 0).Select(text => text.Trim()));
        }
        catch (TimeoutException)
        {
            return string.Empty;
        }
    }

    /// <summary>Stops the process this runner started, and nothing else.</summary>
    private static void Stop(Process process)
    {
        try
        {
            process.Kill();
            process.WaitForExit();
        }
        catch (Exception failure) when (failure is InvalidOperationException or Win32Exception)
        {
            // It exited between the timeout and the kill.
        }
    }
}
