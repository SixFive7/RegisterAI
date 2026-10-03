// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.RegularExpressions;
using RegisterAI.Tests.Harness;

namespace RegisterAI.Tests;

/// <summary>
/// The house rules a build can hold, read from the tree as text. Each test carries a
/// positive control, because a scan that matches nothing reports a clean tree whether
/// or not the tree is clean.
/// </summary>
internal sealed partial class HouseRuleTests
{
    /// <summary>The licence identifier every source file names.</summary>
    private const string LicenseLine = "SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr";

    /// <summary>Extensions that carry the header.</summary>
    private static readonly string[] HeaderedExtensions = [".cs", ".ps1", ".csproj", ".props", ".targets", ".slnx"];

    /// <summary>File names that carry the header whatever their extension.</summary>
    private static readonly string[] HeaderedNames =
        ["BannedSymbols.txt", "pre-commit", ".editorconfig", ".gitattributes", ".gitignore", "nuget.config"];

    /// <summary>
    /// Characters a person does not type on a keyboard and a text generator does:
    /// the en and em dash, the ellipsis, curly quotes of both kinds, the no-break
    /// space, the zero-width space and the byte order mark. Built from code points so
    /// this file does not carry what it forbids.
    /// </summary>
    private static readonly char[] UntypedCharacters =
        [.. new[] { 0x2013, 0x2014, 0x2026, 0x2018, 0x2019, 0x201C, 0x201D, 0x00A0, 0x200B, 0xFEFF }.Select(code => (char)code)];

    /// <summary>Every source file carries the two-line SPDX header in its first five lines.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task EverySourceFileCarriesTheTwoLineSpdxHeader()
    {
        var offenders = new List<string>();
        var checkedFiles = 0;

        foreach (var file in await RepositoryTree.FilesAsync())
        {
            var name = Path.GetFileName(file);

            if (!HeaderedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                && !HeaderedNames.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            checkedFiles++;

            if (!HasHeader(await RepositoryTree.ReadTextAsync(file) ?? string.Empty))
            {
                offenders.Add(file);
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
        await Assert.That(checkedFiles).IsGreaterThan(10);

        // The control: the predicate refuses a file without the header, and one
        // whose header is below the fifth line.
        await Assert.That(HasHeader("namespace X;\n")).IsFalse();
        await Assert.That(HasHeader("1\n2\n3\n4\n5\n// SPDX-FileCopyrightText: 2026 Jori Huisman\n// " + LicenseLine + "\n")).IsFalse();
        await Assert.That(HasHeader("// SPDX-FileCopyrightText: 2026 Jori Huisman\n// " + LicenseLine + "\n")).IsTrue();
    }

    /// <summary>No test in the tree carries a skip attribute.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task NoTestIsSkipped()
    {
        // Composed so that this file does not match its own scan.
        var needle = "[" + "Skip";
        var offenders = new List<string>();

        foreach (var file in (await RepositoryTree.FilesAsync()).Where(file => file.EndsWith(".cs", StringComparison.Ordinal)))
        {
            if ((await RepositoryTree.ReadTextAsync(file) ?? string.Empty).Contains(needle, StringComparison.Ordinal))
            {
                offenders.Add(file);
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
        await Assert.That(("    " + needle + "(\"later\")]").Contains(needle, StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>No text file carries a character a person does not type.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task NoTextFileCarriesACharacterAPersonDoesNotType()
    {
        var offenders = new List<string>();
        var textFiles = 0;

        foreach (var file in await RepositoryTree.FilesAsync())
        {
            if (await RepositoryTree.ReadTextAsync(file) is not { } text)
            {
                continue;
            }

            textFiles++;
            offenders.AddRange(UntypedIn(text).Select(where => $"{file}:{where}"));
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
        await Assert.That(textFiles).IsGreaterThan(10);

        // The control: each forbidden character is found where it was planted.
        foreach (var character in UntypedCharacters)
        {
            var planted = "line one\nso" + character + " it goes\n";

            await Assert.That(UntypedIn(planted)).IsEquivalentTo([string.Create(CultureInfo.InvariantCulture, $"2: U+{(int)character:X4}")]);
        }

        await Assert.That(UntypedIn("plain -- text, 'quoted' and \"quoted\"...\n")).IsEmpty();
    }

    /// <summary>
    /// Every process the tree starts is started without a console window and reads
    /// the child's output as UTF-8.
    /// </summary>
    /// <remarks>
    /// Redirecting the streams does not suppress the window: from a parent with no
    /// console, a child started without the flag opens a terminal on the screen. And
    /// a child's output decoded with the console code page turns a non-ASCII path
    /// into question marks, which then reads as somebody else's path.
    /// </remarks>
    /// <returns>The test.</returns>
    [Test]
    public async Task EveryProcessLaunchHidesItsWindowAndReadsUtf8()
    {
        var offenders = new List<string>();
        var sites = 0;

        foreach (var file in (await RepositoryTree.FilesAsync()).Where(IsCodeFile))
        {
            var lines = (await RepositoryTree.ReadTextAsync(file) ?? string.Empty).Split('\n');

            foreach (var (line, offence) in LaunchOffences(lines))
            {
                sites++;

                if (offence is not null)
                {
                    offenders.Add($"{file}:{line}: {offence}");
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
        await Assert.That(sites).IsGreaterThan(1);

        // The controls, one per missing setting and one with both.
        string[] bare = ["var start = new Process" + "StartInfo(\"git\")", "{", "    UseShellExecute = false,", "};"];
        string[] windowOnly = ["var start = new Process" + "StartInfo(\"git\")", "{", "    CreateNoWindow = true,", "};"];
        string[] both = ["var start = new Process" + "StartInfo(\"git\")", "{", "    CreateNoWindow = true,", "    StandardOutputEncoding = Utf8,", "};"];
        string[] script = ["$start = [System.Diagnostics.Process" + "StartInfo]::new('git')", "$start.UseShellExecute = $false"];

        await Assert.That(LaunchOffences(bare).Single().Offence).IsNotNull();
        await Assert.That(LaunchOffences(windowOnly).Single().Offence!).Contains("UTF-8");
        await Assert.That(LaunchOffences(both).Single().Offence).IsNull();
        await Assert.That(LaunchOffences(script).Single().Offence).IsNotNull();
    }

    /// <summary>Warnings are errors, and no build file suppresses one.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task WarningsAreErrorsAndNoBuildFileSuppressesOne()
    {
        var props = await File.ReadAllTextAsync(Path.Combine(RepositoryTree.Root, "Directory.Build.props"));

        await Assert.That(props).Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>");
        await Assert.That(props).Contains("build\\BannedSymbols.txt");

        var offenders = new List<string>();
        var buildFiles = 0;

        foreach (var file in (await RepositoryTree.FilesAsync()).Where(IsBuildFile))
        {
            buildFiles++;

            var text = await RepositoryTree.ReadTextAsync(file) ?? string.Empty;

            if (SuppressionInBuildFile().IsMatch(text))
            {
                offenders.Add(file);
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
        await Assert.That(buildFiles).IsGreaterThanOrEqualTo(3);

        // The control.
        await Assert.That(SuppressionInBuildFile().IsMatch("<PropertyGroup><NoWarn>CA1000</NoWarn></PropertyGroup>")).IsTrue();
        await Assert.That(SuppressionInBuildFile().IsMatch("<TreatWarningsAsErrors>false</TreatWarningsAsErrors>")).IsTrue();
    }

    private static bool HasHeader(string text)
    {
        var head = string.Join('\n', text.Split('\n').Take(5));

        return head.Contains("SPDX-FileCopyrightText: ", StringComparison.Ordinal)
            && head.Contains(LicenseLine, StringComparison.Ordinal);
    }

    private static List<string> UntypedIn(string text)
    {
        var found = new List<string>();
        var lines = text.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            foreach (var character in lines[index].Where(character => UntypedCharacters.Contains(character)).Distinct())
            {
                found.Add(string.Create(CultureInfo.InvariantCulture, $"{index + 1}: U+{(int)character:X4}"));
            }
        }

        return found;
    }

    private static bool IsCodeFile(string file) =>
        file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".ps1", StringComparison.Ordinal);

    private static bool IsBuildFile(string file) =>
        file.EndsWith(".props", StringComparison.Ordinal)
        || file.EndsWith(".targets", StringComparison.Ordinal)
        || file.EndsWith(".csproj", StringComparison.Ordinal);

    /// <summary>
    /// Each place a process start is configured, with what it lacks, or null when it
    /// lacks nothing. A site's settings are read up to the end of its initializer.
    /// </summary>
    private static List<(int Line, string? Offence)> LaunchOffences(string[] lines)
    {
        var sites = new List<(int, string?)>();

        for (var index = 0; index < lines.Length; index++)
        {
            if (!LaunchSite().IsMatch(lines[index]))
            {
                continue;
            }

            var settings = string.Join('\n', lines.Skip(index).Take(30).TakeWhile((line, offset) => offset is 0 || !line.TrimStart().StartsWith('}')));
            var hidden = settings.Contains("CreateNoWindow = true", StringComparison.Ordinal)
                || settings.Contains("CreateNoWindow = $true", StringComparison.Ordinal);
            var utf8 = settings.Contains("StandardOutputEncoding = ", StringComparison.Ordinal);

            sites.Add((index + 1, (hidden, utf8) switch
            {
                (false, _) => "starts a process without CreateNoWindow",
                (true, false) => "starts a process without reading its output as UTF-8",
                _ => null,
            }));
        }

        return sites;
    }

    [GeneratedRegex(@"new\s+ProcessStartInfo\b|ProcessStartInfo\]::new\(")]
    private static partial Regex LaunchSite();

    [GeneratedRegex(@"<(NoWarn|WarningsNotAsErrors)\b|<TreatWarningsAsErrors>\s*false", RegexOptions.IgnoreCase)]
    private static partial Regex SuppressionInBuildFile();
}
