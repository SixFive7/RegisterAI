// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace RegisterAI;

/// <summary>A PATH value as the registry holds it: the text unexpanded, and its kind.</summary>
/// <param name="Text">The value, exactly as stored.</param>
/// <param name="Kind"><see cref="RegistryValueKind.ExpandString"/> or <see cref="RegistryValueKind.String"/>.</param>
internal sealed record UserPathValue(string Text, RegistryValueKind Kind);

/// <summary>Where the user PATH is read and written, and how a change to it is announced.</summary>
/// <remarks>
/// A seam for one reason: the tests must never write the person's own PATH. The
/// product passes <see cref="RegistryUserPathStore.User"/> from <see cref="Machine.Real"/>,
/// and nothing else in the product passes anything.
/// </remarks>
internal interface IUserPathStore
{
    /// <summary>Where this store is, for a document.</summary>
    string Where { get; }

    /// <summary>The value, or null when there is none.</summary>
    /// <returns>The value.</returns>
    UserPathValue? Read();

    /// <summary>Writes the value, text and kind.</summary>
    /// <param name="value">The value.</param>
    void Write(UserPathValue value);

    /// <summary>Removes the value.</summary>
    void Delete();

    /// <summary>Tells running programs that the environment changed.</summary>
    /// <returns>Whether the announcement was sent.</returns>
    bool Announce();
}

/// <summary>A PATH value in the registry.</summary>
/// <param name="hive">The hive.</param>
/// <param name="subKey">The key under it.</param>
/// <param name="announce">Whether a change is broadcast to every top-level window.</param>
internal sealed class RegistryUserPathStore(RegistryKey hive, string subKey, bool announce) : IUserPathStore
{
    /// <summary>The value's name.</summary>
    public const string ValueName = "Path";

    /// <summary><c>HKEY_CURRENT_USER\Environment\Path</c>, the user's own PATH, announced on change.</summary>
    public static RegistryUserPathStore User { get; } = new(Registry.CurrentUser, "Environment", announce: true);

    /// <inheritdoc/>
    public string Where => $@"{hive.Name}\{subKey}\{ValueName}";

    /// <inheritdoc/>
    public UserPathValue? Read()
    {
        using var key = hive.OpenSubKey(subKey, writable: false);

        // Unexpanded, so a write gives back exactly the references it was given.
        return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string text
            ? new UserPathValue(text, key.GetValueKind(ValueName))
            : null;
    }

    /// <inheritdoc/>
    public void Write(UserPathValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        using var key = hive.CreateSubKey(subKey, writable: true);

        key.SetValue(ValueName, value.Text, value.Kind);
    }

    /// <inheritdoc/>
    public void Delete()
    {
        using var key = hive.OpenSubKey(subKey, writable: true);

        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <inheritdoc/>
    public bool Announce() => announce && EnvironmentBroadcast.Announce();
}

/// <summary>The user PATH as a document reports it.</summary>
internal sealed record PathReport
{
    /// <summary>Where the PATH is.</summary>
    public required string Where { get; init; }

    /// <summary>The folder <c>path add</c> or <c>path remove</c> was about; null for status.</summary>
    public string? Folder { get; init; }

    /// <summary>What <c>path add</c> or <c>path remove</c> did; null for status.</summary>
    public Change? Action { get; init; }

    /// <summary>Whether the change was announced to running programs; null when nothing was written.</summary>
    public bool? Announced { get; init; }

    /// <summary>Entries naming a folder that does not exist, as stored.</summary>
    public IReadOnlyList<string> Dead { get; init; } = [];

    /// <summary>Why the PATH could not be read, written or confirmed, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The exit code a path verb ends with: 1 for a failure, else 0.</summary>
    public int ExitCode => Action is Change.Failed ? 1 : 0;
}

/// <summary>Runs <c>path add</c> and <c>path remove</c> against a machine's user PATH.</summary>
internal static class PathVerbs
{
    /// <summary>Runs one path verb.</summary>
    /// <param name="request">The verb, the folder and the dry-run flag.</param>
    /// <param name="machine">The machine, whose store is the user PATH.</param>
    /// <returns>What happened.</returns>
    public static PathReport Run(Request request, Machine machine)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(machine);

        return request.Verb is Verb.PathAdd
            ? UserPath.Add(machine.UserPath, request.Folder!, request.DryRun, machine.Variable)
            : UserPath.Remove(machine.UserPath, request.Folder!, request.DryRun, machine.Variable);
    }
}

/// <summary>
/// The user PATH: what <c>path add</c> and <c>path remove</c> do to it, and what status
/// reports about it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here runs unless a person or a program asks for it by name. RegisterAI never
/// changes the PATH as a side effect of another verb; status only reads it.
/// </para>
/// <para>
/// A folder is appended after the entries already there, and after a separator even when
/// the value already ends in one, so that removing it again gives back the value it was
/// added to, byte for byte. The value's kind is kept, and a value that did not exist is
/// created as <c>REG_EXPAND_SZ</c>, the kind Windows gives a user PATH. A remove takes off
/// every entry naming exactly that folder, compared without case and without a trailing
/// separator, and nothing spelled another way. Every write is read back before it counts,
/// and only a confirmed write is announced with <c>WM_SETTINGCHANGE</c>.
/// </para>
/// </remarks>
internal static class UserPath
{
    /// <summary>The separator Windows uses between PATH entries.</summary>
    public const char Separator = ';';

    /// <summary>Puts a folder on the user PATH, unless it is there already.</summary>
    /// <param name="store">Where the PATH is.</param>
    /// <param name="folder">The folder, a full path.</param>
    /// <param name="dryRun">Whether to decide and write nothing.</param>
    /// <param name="variable">Reads a variable, for the dead-entry check.</param>
    /// <returns>What happened. Never throws for a PATH that cannot be read or written.</returns>
    public static PathReport Add(IUserPathStore store, string folder, bool dryRun, Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(variable);

        var report = new PathReport { Where = store.Where, Folder = folder };

        try
        {
            var value = store.Read();

            if (value is not null && Segments(value.Text).Any(segment => Names(segment, folder)))
            {
                return report with { Action = Change.None, Dead = Dead(value, variable) };
            }

            var wanted = value is null
                ? new UserPathValue(folder, RegistryValueKind.ExpandString)
                : value with { Text = value.Text + Separator + folder };

            return dryRun
                ? report with { Action = Change.Added, Dead = Dead(value, variable) }
                : Commit(store, report with { Action = Change.Added }, wanted, variable);
        }
        catch (Exception failure) when (IsStoreFailure(failure))
        {
            return report with { Action = Change.Failed, Error = $"'{folder}' could not be put on the PATH at {store.Where}: {failure.Message}" };
        }
    }

    /// <summary>Takes a folder off the user PATH: every entry naming exactly it, case aside.</summary>
    /// <param name="store">Where the PATH is.</param>
    /// <param name="folder">The folder, as the entry spells it.</param>
    /// <param name="dryRun">Whether to decide and write nothing.</param>
    /// <param name="variable">Reads a variable, for the dead-entry check.</param>
    /// <returns>What happened. Never throws for a PATH that cannot be read or written.</returns>
    public static PathReport Remove(IUserPathStore store, string folder, bool dryRun, Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(variable);

        var report = new PathReport { Where = store.Where, Folder = folder };

        try
        {
            if (store.Read() is not { } value)
            {
                return report with { Action = Change.None };
            }

            var segments = Segments(value.Text);
            var kept = segments.Where(segment => !Names(segment, folder)).ToList();

            if (kept.Count == segments.Count)
            {
                return report with { Action = Change.None, Dead = Dead(value, variable) };
            }

            // A value that held this entry and nothing else was made by an add from
            // nothing, and goes the way it came. A value that is now empty text was
            // empty text with the entry appended, and stays.
            var wanted = kept.Count is 0 ? null : value with { Text = string.Join(Separator, kept) };

            return dryRun
                ? report with { Action = Change.Removed, Dead = Dead(wanted, variable) }
                : Commit(store, report with { Action = Change.Removed }, wanted, variable);
        }
        catch (Exception failure) when (IsStoreFailure(failure))
        {
            return report with { Action = Change.Failed, Error = $"'{folder}' could not be taken off the PATH at {store.Where}: {failure.Message}" };
        }
    }

    /// <summary>What status reports about the user PATH: the entries naming a folder that does not exist.</summary>
    /// <param name="store">Where the PATH is.</param>
    /// <param name="variable">Reads a variable.</param>
    /// <returns>The report. Nothing is written.</returns>
    public static PathReport Inspect(IUserPathStore store, Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(variable);

        try
        {
            return new PathReport { Where = store.Where, Dead = Dead(store.Read(), variable) };
        }
        catch (Exception failure) when (IsStoreFailure(failure))
        {
            return new PathReport { Where = store.Where, Error = $"The PATH at {store.Where} could not be read: {failure.Message}" };
        }
    }

    /// <summary>A PATH value's entries, empty ones included.</summary>
    /// <param name="text">The value.</param>
    /// <returns>The entries, in order.</returns>
    public static List<string> Segments(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return [.. text.Split(Separator)];
    }

    /// <summary>Whether a PATH entry is exactly this folder: case, surrounding spaces and a trailing separator aside.</summary>
    /// <param name="segment">One entry, as stored.</param>
    /// <param name="folder">The folder.</param>
    /// <returns>Whether it names it.</returns>
    public static bool Names(string segment, string folder)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(folder);

        var left = Trimmed(segment);

        return left.Length > 0 && string.Equals(left, Trimmed(folder), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The entries of a value that name a folder that does not exist.</summary>
    /// <param name="value">The value, or null.</param>
    /// <param name="variable">Reads a variable, for an entry spelled with <c>%NAME%</c>.</param>
    /// <returns>Each such entry once, as stored and trimmed, in order.</returns>
    /// <remarks>
    /// Only an entry that is a full path once expanded is judged. A relative entry, an
    /// entry naming a variable that is not set and a network path are not: the first two
    /// name no folder this tool can find, and asking a network share whether a folder
    /// exists can take as long as the network does. Windows expands <c>%NAME%</c> only in
    /// a <c>REG_EXPAND_SZ</c> value, so in a plain string an entry spelled with a variable
    /// is not a full path and is not judged.
    /// </remarks>
    public static List<string> Dead(UserPathValue? value, Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        var dead = new List<string>();

        if (value is null)
        {
            return dead;
        }

        foreach (var segment in Segments(value.Text))
        {
            var entry = segment.Trim();

            if (entry.Length is 0 || dead.Any(found => Names(found, entry)))
            {
                continue;
            }

            var folder = (value.Kind is RegistryValueKind.ExpandString ? Expand(entry, variable) : entry).Trim('"');

            try
            {
                if (!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal) || folder.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Directory.Exists(folder))
                {
                    dead.Add(entry);
                }
            }
            catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // An entry that is not a path names no folder to judge.
            }
        }

        return dead;
    }

    /// <summary>The line that takes an entry off the user PATH.</summary>
    /// <param name="entry">The entry, as stored.</param>
    /// <returns>The command line.</returns>
    public static string RemoveCommand(string entry) => "registerai path remove " + Shell.Quote(entry);

    /// <summary>The line that puts a folder on the user PATH.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The command line.</returns>
    public static string AddCommand(string folder) => "registerai path add " + Shell.Quote(folder);

    /// <summary>
    /// Expands <c>%NAME%</c> the way Windows expands a <c>REG_EXPAND_SZ</c> value. A
    /// variable that is not set stays as written.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="variable">Reads a variable.</param>
    /// <returns>The expanded text.</returns>
    public static string Expand(string text, Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(variable);

        var result = new StringBuilder(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            var open = text.IndexOf('%', index);
            var close = open < 0 ? -1 : text.IndexOf('%', open + 1);

            if (open < 0 || close < 0)
            {
                _ = result.Append(text, index, text.Length - index);
                break;
            }

            _ = result.Append(text, index, open - index);

            var name = text[(open + 1)..close];

            if (name.Length > 0 && variable(name) is { } found)
            {
                _ = result.Append(found);
                index = close + 1;
            }
            else
            {
                // Not a variable: the first percent sign stays, and the second may
                // open the next reference.
                _ = result.Append('%');
                index = open + 1;
            }
        }

        return result.ToString();
    }

    /// <summary>Writes the value, reads it back, and announces it only once it reads as written.</summary>
    private static PathReport Commit(IUserPathStore store, PathReport report, UserPathValue? wanted, Func<string, string?> variable)
    {
        if (wanted is null)
        {
            store.Delete();
        }
        else
        {
            store.Write(wanted);
        }

        var back = store.Read();

        if (back != wanted)
        {
            return report with
            {
                Action = Change.Failed,
                Error = $"The PATH at {store.Where} was written, but it did not read back as written: it reads {Describe(back)} where {Describe(wanted)} was written.",
                Dead = Dead(back, variable),
            };
        }

        return report with { Announced = store.Announce(), Dead = Dead(back, variable) };
    }

    private static string Describe(UserPathValue? value) =>
        value is null ? "as no value at all" : $"as {value.Kind} with {Segments(value.Text).Count} entries";

    private static string Trimmed(string path) =>
        path.Trim().Trim('"').TrimEnd('\\', '/');

    private static bool IsStoreFailure(Exception failure) =>
        failure is IOException or UnauthorizedAccessException or SecurityException;
}

/// <summary>Tells every top-level window that the user's environment changed.</summary>
/// <remarks>
/// <para>
/// Writing an environment value to the registry changes no running program. The
/// documented way to publish it is <c>WM_SETTINGCHANGE</c> with the string
/// <c>Environment</c>, which lets the shell pick the change up; a program that does not
/// act on it keeps the environment it started with, and so does everything it starts.
/// </para>
/// <para>
/// Bounded, and a hung window is skipped: <c>SMTO_ABORTIFHUNG</c> skips a window the
/// system already knows is hung, and one second is the most any other window may take.
/// Nothing waits for an answer, because there is none to read.
/// </para>
/// </remarks>
internal static partial class EnvironmentBroadcast
{
    private const nint BroadcastWindow = 0xFFFF;
    private const uint SettingChange = 0x001A;
    private const uint AbortIfHung = 0x0002;

    /// <summary>The most one window may take to handle the message, in milliseconds.</summary>
    private const uint PerWindowBound = 1000;

    /// <summary>Broadcasts the change.</summary>
    /// <returns>
    /// Whether the broadcast was sent. A broadcast that could not be made is reported as
    /// not sent and never turns a write that read back as asked into a failure.
    /// </returns>
    public static bool Announce()
    {
        try
        {
            return Send(BroadcastWindow, SettingChange);
        }
        catch (Exception failure) when (failure is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return false;
        }
    }

    /// <summary>Sends one message, bounded, with <c>Environment</c> as its text.</summary>
    /// <param name="window">The window, or the broadcast handle.</param>
    /// <param name="message">The message.</param>
    /// <returns>Whether it was sent.</returns>
    internal static bool Send(nint window, uint message) =>
        SendMessageTimeoutW(window, message, 0, "Environment", AbortIfHung, PerWindowBound, out _) != 0;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeoutW(
        nint hWnd,
        uint msg,
        nuint wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out nuint lpdwResult);
}
