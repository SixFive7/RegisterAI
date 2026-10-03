// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text;

namespace RegisterAI;

/// <summary>
/// The two streams this tool writes, owned in one place.
/// </summary>
/// <remarks>
/// <para>
/// stdout carries exactly one document per run: a JSON document for
/// <c>status</c>, <c>register</c>, <c>unregister</c>, <c>describe</c> and every
/// usage error, and plain text for <c>help</c>, <c>license</c> and
/// <c>--version</c>. stderr carries diagnostics a person may want and a program
/// can ignore.
/// </para>
/// <para>
/// Both are written as UTF-8 without a byte order mark and with LF line ends,
/// whatever the console's code page is. <c>System.Console</c> is banned
/// everywhere else in the product (<c>BannedSymbols.txt</c>), so this is the
/// only place that can break that promise.
/// </para>
/// </remarks>
internal sealed class Terminal : IDisposable
{
    private readonly Stream _error;

    /// <summary>The process's own stdout and stderr.</summary>
    public Terminal()
    {
#pragma warning disable RS0030 // The one owner of stdout and stderr. See BannedSymbols.txt beside the project file.
        Output = Console.OpenStandardOutput();
        _error = Console.OpenStandardError();
#pragma warning restore RS0030
    }

    /// <summary>Two streams a test holds.</summary>
    /// <param name="output">Where documents go.</param>
    /// <param name="error">Where diagnostics go.</param>
    public Terminal(Stream output, Stream error)
    {
        Output = output;
        _error = error;
    }

    /// <summary>UTF-8 with no byte order mark.</summary>
    public static UTF8Encoding Utf8 { get; } = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The stream a document is written to.</summary>
    public Stream Output { get; }

    /// <summary>Writes text to stdout, LF line ends, and flushes.</summary>
    /// <param name="text">The text.</param>
    public void WriteText(string text) => Write(Output, text);

    /// <summary>Writes text to stderr, LF line ends, and flushes.</summary>
    /// <param name="text">The text.</param>
    public void WriteDiagnostic(string text) => Write(_error, text);

    /// <inheritdoc/>
    public void Dispose()
    {
        Output.Dispose();
        _error.Dispose();
    }

    private static void Write(Stream stream, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        stream.Write(Utf8.GetBytes(text.ReplaceLineEndings("\n")));
        stream.Flush();
    }
}
