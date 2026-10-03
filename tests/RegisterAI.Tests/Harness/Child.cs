// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;

namespace RegisterAI.Tests.Harness;

/// <summary>What a child process did.</summary>
/// <param name="ExitCode">Its exit code, or -1 when it was killed for running too long.</param>
/// <param name="OutputBytes">Everything it wrote to stdout, as bytes.</param>
/// <param name="Error">Everything it wrote to stderr, decoded as UTF-8.</param>
/// <param name="TimedOut">Whether it ran past its budget and was killed.</param>
internal sealed record ChildResult(int ExitCode, byte[] OutputBytes, string Error, bool TimedOut)
{
    /// <summary>stdout decoded as UTF-8, which fails on bytes that are not UTF-8.</summary>
    public string Output => Child.Utf8.GetString(OutputBytes);
}

/// <summary>Starts a program the way the suite always does: no window, UTF-8, a budget.</summary>
internal static class Child
{
    /// <summary>Strict UTF-8 with no byte order mark.</summary>
    public static UTF8Encoding Utf8 { get; } = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Runs a program to completion, or kills it when the budget runs out.</summary>
    /// <param name="executable">The program, absolute or found on PATH.</param>
    /// <param name="arguments">Its arguments, passed one by one.</param>
    /// <param name="budget">How long it may run.</param>
    /// <param name="workingDirectory">Where it runs, or the repository root.</param>
    /// <param name="environment">Variables to set, or to remove when the value is null.</param>
    /// <param name="holdInputOpen">Whether stdin stays open until it exits, which a program that reads stdin would wait on.</param>
    /// <returns>What it did.</returns>
    public static async Task<ChildResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan budget,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool holdInputOpen = false)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = workingDirectory ?? RepositoryTree.Root,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                _ = start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"'{executable}' did not start.");

        if (!holdInputOpen)
        {
            process.StandardInput.Close();
        }

        var reading = ReadAllAsync(process.StandardOutput.BaseStream);
        var error = process.StandardError.ReadToEndAsync();
        var timedOut = false;

        using (var limit = new CancellationTokenSource(budget))
        {
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                process.Kill();
                await process.WaitForExitAsync();
            }
        }

        if (holdInputOpen)
        {
            process.StandardInput.Close();
        }

        // A grandchild that kept the pipes open would hold these forever; the
        // budget bounds them too.
        var bytes = await reading.WaitAsync(budget);
        var said = await error.WaitAsync(budget);

        return new ChildResult(timedOut ? -1 : process.ExitCode, bytes, said, timedOut);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        return buffer.ToArray();
    }
}
