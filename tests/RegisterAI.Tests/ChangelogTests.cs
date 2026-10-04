// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RegisterAI.Tests.Harness;

namespace RegisterAI.Tests;

/// <summary>
/// CHANGELOG.md is written in the shape a release page is built from, and
/// <c>build\New-ReleaseNotes.ps1</c> builds that page: the section's opening paragraphs,
/// one line per entry linked to the entry's own lines, the legend and a link to the
/// section. Every rule carries a planted control that must fail, because a check that
/// reads nothing reports a clean file whether or not the file is clean.
/// </summary>
internal sealed partial class ChangelogTests
{
    /// <summary>How long a headline may be, its full stop included.</summary>
    /// <remarks>
    /// A reading budget, chosen and not measured: about the width of the bold line a
    /// reader scans on a release page, and short enough that a headline cannot become
    /// the entry.
    /// </remarks>
    private const int HeadlineBudget = 100;

    /// <summary>How many of a headline's opening words its detail may open with.</summary>
    /// <remarks>
    /// An entry about a named thing opens with that name, and a short subject shared with
    /// the first sentence is ordinary English. Five words in a row is the headline said
    /// twice.
    /// </remarks>
    private const int RestatementBudget = 4;

    /// <summary>Where the release pages point.</summary>
    private const string Repository = "https://github.com/SixFive7/RegisterAI";

    /// <summary>The icons an entry may open with, in the order the legend lists them.</summary>
    private static readonly (string Icon, string Means)[] Palette =
    [
        ("✨", "new capability"),
        ("🐛", "fix"),
        ("🔧", "behaviour or configuration change"),
        ("🔒", "security or permissions"),
        ("🗑️", "removal or deprecation"),
        ("💥", "breaking, or the reader must act"),
        ("📝", "documentation"),
        ("✅", "tests and the gate"),
        ("📦", "packaging, installer, release pipeline"),
        ("⚡", "performance"),
        ("♻️", "refactor with no behaviour change"),
        ("⬆️", "dependency move"),
    ];

    /// <summary>The Keep a Changelog groups, in the order that format fixes.</summary>
    private static readonly string[] Groups = ["Added", "Changed", "Deprecated", "Removed", "Fixed", "Security"];

    private static string Changelog => Path.Combine(RepositoryTree.Root, "CHANGELOG.md");

    private static string NotesScript => Path.Combine(RepositoryTree.Root, "build", "New-ReleaseNotes.ps1");

    /// <summary>The legend as the changelog writes it: a table, two icons to a row.</summary>
    private static string LegendText =>
        "| Icon | Meaning | Icon | Meaning |\n|---|---|---|---|\n"
        + string.Join('\n', Palette.Chunk(2).Select(static pair => $"| {pair[0].Icon} | {pair[0].Means} | {pair[1].Icon} | {pair[1].Means} |"))
        + "\n";

    /// <summary>
    /// A small changelog in the house shape: an unreleased section, two releases, two
    /// groups, and entries of one line and of three.
    /// </summary>
    private static string Fixture =>
        "# Changelog\n\nA paragraph about the format.\n\n"
        + LegendText
        + "\n## [Unreleased]\n\n## [9.9.9] - 2026-01-01\n\n"
        + "The first paragraph of the release.\n\nThe second paragraph,\nwrapped over two lines.\n\n"
        + "### Added\n\n"
        + "- ✨ **A thing was added.** It is new.\n\n"
        + "- ✅ **A test holds it.**\n  The detail runs\n  over three lines.\n\n"
        + "### Fixed\n\n"
        + "- 🐛 **A thing was fixed.** It was broken.\n\n"
        + "## [9.9.8] - 2025-12-31\n\nThe release before.\n\n### Added\n\n- ✨ **An older thing.** Old.\n";

    /// <summary>Changelogs the generator must refuse, each one defect away from the fixture, and what the refusal says.</summary>
    /// <returns>The rows.</returns>
    public static IEnumerable<Func<(string Label, string Version, string Changelog, string Says)>> Refusals()
    {
        yield return () => ("no section", "9.9.7", Fixture, "no '## [9.9.7]' section");
        yield return () => ("an entry with no bold headline", "9.9.9", Fixture.Replace("- ✨ **A thing was added.** It is new.", "- ✨ A thing was added. It is new.", StringComparison.Ordinal), "is not written as");
        yield return () => ("an entry above the groups", "9.9.9", Fixture.Replace("### Added\n\n- ✨ **A thing", "- ✨ **A stray.** Above the groups.\n\n### Added\n\n- ✨ **A thing", StringComparison.Ordinal), "above its first group heading");
        yield return () => ("a paragraph inside a group", "9.9.9", Fixture.Replace("### Fixed\n\n", "### Fixed\n\nA paragraph that is not an entry.\n\n", StringComparison.Ordinal), "that is not an entry");
        yield return () => ("a group with no entries", "9.9.9", Fixture.Replace("### Fixed\n\n", "### Changed\n\n### Fixed\n\n", StringComparison.Ordinal), "with no entries under it");
        yield return () => ("a legend that is not a table", "9.9.9", Fixture.Replace(LegendText, string.Join(", ", Palette.Select(static entry => $"{entry.Icon} {entry.Means}")) + "\n", StringComparison.Ordinal), "no icon legend");
    }

    /// <summary>Every entry opens with one icon from the legend and a bold headline of one sentence, within the budget.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task EveryEntryOpensWithAPaletteIconAndABoldOneSentenceHeadline()
    {
        var text = await ReadChangelogAsync();
        var icons = LegendIcons(text);

        await Assert.That(icons.Count).IsEqualTo(Palette.Length);
        await Assert.That(string.Join(Environment.NewLine, Malformed(text, icons))).IsEmpty();
        await Assert.That(Entries(text).Count).IsGreaterThan(10);

        // The controls: each mistake a person writing an entry makes, and the shape one
        // step from each, so that they fail for their own reason.
        await Assert.That(Malformed(sectionWith("- An entry with no icon."), icons)).IsNotEmpty();
        await Assert.That(Malformed(sectionWith("- 🎉 **An icon that is not in the palette.**"), icons)).IsNotEmpty();
        await Assert.That(Malformed(sectionWith("- ✨ A headline nobody made bold."), icons)).IsNotEmpty();
        await Assert.That(Malformed(sectionWith("- ✨ **Two sentences. That is one too many.**"), icons)).IsNotEmpty();
        await Assert.That(Malformed(sectionWith("- ✨ **A headline with no full stop**"), icons)).IsNotEmpty();
        await Assert.That(Malformed(sectionWith("- ✨ **" + new string('x', HeadlineBudget) + ".**"), icons)).IsNotEmpty();
        await Assert.That(Malformed(sectionWith("- ✨ **A headline of the right shape, at version 1.2.3.** The detail."), icons)).IsEmpty();

        static string sectionWith(string entry) => "## [9.9.9] - 2026-01-01\n\nA paragraph.\n\n### Added\n\n" + entry + "\n";
    }

    /// <summary>The legend at the top is a table of exactly the palette, in order, two icons to a row.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheLegendIsATableOfExactlyThePaletteTwoToARow()
    {
        var text = await ReadChangelogAsync();

        await Assert.That(string.Join(" | ", LegendPairs(text))).IsEqualTo(string.Join(" | ", Palette.Select(static entry => $"{entry.Icon} {entry.Means}")));

        var rows = LegendRows(text);

        await Assert.That(rows.Count).IsEqualTo(6);
        await Assert.That(rows.TrueForAll(static row => row.Count is 4)).IsTrue();

        // The control: a legend written as one line is not read as a legend, and the
        // table form is, so the control fails for its own reason.
        var oneLine = "# C\n\n" + string.Join(", ", Palette.Select(static entry => $"{entry.Icon} {entry.Means}")) + "\n\n## [9.9.9] - 2026-01-01\n";

        await Assert.That(LegendPairs(oneLine)).IsEmpty();
        await Assert.That(LegendPairs("# C\n\n" + LegendText + "\n## [9.9.9] - 2026-01-01\n").Count).IsEqualTo(Palette.Length);
    }

    /// <summary>Each version's groups are the Keep a Changelog set, each at most once, in that format's order.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task EverySectionsGroupsAreTheKeepAChangelogSetInItsOrder()
    {
        await Assert.That(string.Join(Environment.NewLine, OutOfOrder(await ReadChangelogAsync()))).IsEmpty();

        // The controls: a repeat, the wrong order and a group the format does not have.
        await Assert.That(OutOfOrder("## [9.9.9] - 2026-01-01\n\n### Added\n\n### Fixed\n\n### Added\n")).IsNotEmpty();
        await Assert.That(OutOfOrder("## [9.9.9] - 2026-01-01\n\n### Fixed\n\n### Added\n")).IsNotEmpty();
        await Assert.That(OutOfOrder("## [9.9.9] - 2026-01-01\n\n### Improved\n")).IsNotEmpty();
        await Assert.That(OutOfOrder("## [9.9.9] - 2026-01-01\n\n### Added\n\n### Fixed\n")).IsEmpty();
    }

    /// <summary>
    /// The first section heading is <c>[Unreleased]</c>, and every other one is a bare
    /// version and a date: the tag carries the v, the heading does not.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task EveryVersionHeadingIsUnreleasedOrABareVersionAndADate()
    {
        var headings = SectionHeadings(await ReadChangelogAsync());

        await Assert.That(headings.Count).IsGreaterThan(1);
        await Assert.That(headings[0]).IsEqualTo("## [Unreleased]");
        await Assert.That(string.Join(Environment.NewLine, headings.Skip(1).Where(static heading => !DatedHeading().IsMatch(heading)))).IsEmpty();
        await Assert.That(headings.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(headings.Count);

        // The controls: the tag's v, a missing date and missing brackets are each
        // refused, and the house form passes.
        await Assert.That(DatedHeading().IsMatch("## [v9.9.9] - 2026-01-01")).IsFalse();
        await Assert.That(DatedHeading().IsMatch("## [9.9.9]")).IsFalse();
        await Assert.That(DatedHeading().IsMatch("## 9.9.9 - 2026-01-01")).IsFalse();
        await Assert.That(DatedHeading().IsMatch("## [9.9.9] - 2026-01-01")).IsTrue();
    }

    /// <summary>
    /// Every released section opens with a paragraph before its groups, because the
    /// release page opens with it and a reader may never have seen RegisterAI.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task EveryReleasedSectionOpensWithAParagraph()
    {
        var sections = DatedSections(await ReadChangelogAsync());

        await Assert.That(sections.Count).IsGreaterThan(0);
        await Assert.That(string.Join(", ", sections.Where(static section => Preamble(section.Body).Length is 0).Select(static section => section.Version))).IsEmpty();

        // The control: a section that goes straight into its groups.
        await Assert.That(Preamble("\n\n### Added\n\n- ✨ **A thing.**\n")).IsEmpty();
        await Assert.That(Preamble("\n\nA paragraph.\n\n### Added\n")).IsEqualTo("A paragraph.");
    }

    /// <summary>No entry's detail opens by saying its headline again.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task NoEntrysDetailOpensByRestatingItsHeadline()
    {
        var offences = new List<string>();
        var read = 0;

        foreach (var (line, text) in Entries(await ReadChangelogAsync()))
        {
            var shaped = EntryShape().Match(text);

            if (!shaped.Success)
            {
                continue;
            }

            read++;

            if (SharedOpening(shaped.Groups["headline"].Value, text[(shaped.Index + shaped.Length)..]) > RestatementBudget)
            {
                offences.Add(string.Create(CultureInfo.InvariantCulture, $"CHANGELOG.md:{line}: the detail opens with the headline's own words: {Excerpt(text)}"));
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offences)).IsEmpty();
        await Assert.That(read).IsGreaterThan(10);

        // The controls: the headline said twice, and a detail that starts elsewhere.
        await Assert.That(SharedOpening("The release page lists one line per change.", " The release page lists one line per change, as the script writes it.")).IsGreaterThan(RestatementBudget);
        await Assert.That(SharedOpening("The release page lists one line per change.", " `New-ReleaseNotes.ps1` writes one line per change.")).IsLessThanOrEqualTo(RestatementBudget);
    }

    /// <summary>
    /// The release notes are the section's paragraphs unwrapped, each group with one line
    /// per entry linked to that entry's own lines, the legend, and a link to the section.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheReleaseNotesListEachHeadlineLinkedToItsOwnLines()
    {
        using var scratch = Scratch.Create("notes");
        var changelog = scratch.In("CHANGELOG.md");
        var body = scratch.In("body.md");

        await File.WriteAllTextAsync(changelog, Fixture);

        var run = await NotesAsync("-Version", "9.9.9", "-Path", changelog, "-Destination", body);

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Output.TrimEnd().Split('\n')[^1]).StartsWith("linked ");

        var source = Repository + "/blob/v9.9.9/CHANGELOG.md";
        string[] expected =
        [
            "The first paragraph of the release.",
            string.Empty,
            "The second paragraph, wrapped over two lines.",
            string.Empty,
            "### Added",
            string.Empty,
            $"- ✨ **A thing was added.** [read more]({source}?plain=1#{Lines("- ✨ **A thing was added.**", 1)})",
            $"- ✅ **A test holds it.** [read more]({source}?plain=1#{Lines("- ✅ **A test holds it.**", 3)})",
            string.Empty,
            "### Fixed",
            string.Empty,
            $"- 🐛 **A thing was fixed.** [read more]({source}?plain=1#{Lines("- 🐛 **A thing was fixed.**", 1)})",
            string.Empty,
            "---",
            string.Empty,
            LegendText.TrimEnd(),
            string.Empty,
            $"The full changelog for this release: [CHANGELOG.md]({source}#999---2026-01-01)",
            string.Empty,
        ];

        var bytes = await File.ReadAllBytesAsync(body);

        await Assert.That(Child.Utf8.GetString(bytes)).IsEqualTo(string.Join('\n', expected));
        await Assert.That(bytes is [0xEF, 0xBB, 0xBF, ..]).IsFalse();
        await Assert.That(bytes.Contains((byte)'\r')).IsFalse();

        // The control: the range of an entry of three lines is three lines long, so the
        // ranges above are read from the fixture and not merely copied into it.
        await Assert.That(Lines("- ✅ **A test holds it.**", 3)).IsNotEqualTo(Lines("- ✅ **A test holds it.**", 1));
    }

    /// <summary>A body over the limit keeps its headlines, drops the per-entry links, and says so.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ABodyOverTheLimitKeepsItsHeadlinesAndDropsTheLinks()
    {
        using var scratch = Scratch.Create("notes-limit");
        var changelog = scratch.In("CHANGELOG.md");
        var body = scratch.In("body.md");

        await File.WriteAllTextAsync(changelog, Fixture);

        var run = await NotesAsync("-Version", "9.9.9", "-Path", changelog, "-Destination", body, "-Limit", "100");
        var text = await File.ReadAllTextAsync(body);

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Output.TrimEnd().Split('\n')[^1]).StartsWith("headlines ");
        await Assert.That(text).DoesNotContain("[read more]");
        await Assert.That(text).Contains("- ✨ **A thing was added.**\n");
        await Assert.That(text).Contains("The full changelog for this release: ");

        // The control: the same changelog under the default limit keeps its links.
        var linked = await NotesAsync("-Version", "9.9.9", "-Path", changelog, "-Destination", body);

        await Assert.That(linked.ExitCode).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(body)).Contains("[read more]");
    }

    /// <summary>A changelog the generator cannot read in its shape is refused with a sentence that names the defect.</summary>
    /// <param name="row">The defect, the version asked for, the changelog and what the refusal says.</param>
    /// <returns>The test.</returns>
    [Test]
    [MethodDataSource(nameof(Refusals))]
    public async Task TheGeneratorRefusesAChangelogNotInItsShape((string Label, string Version, string Changelog, string Says) row)
    {
        using var scratch = Scratch.Create("notes-refused");
        var changelog = scratch.In("CHANGELOG.md");
        var body = scratch.In("body.md");

        // The control: the planted changelog is a different file from the fixture, so
        // the refusal is the defect's and not something the fixture always does.
        await Assert.That(row.Changelog != Fixture || row.Version != "9.9.9").IsTrue();

        await File.WriteAllTextAsync(changelog, row.Changelog);

        var run = await NotesAsync("-Version", row.Version, "-Path", changelog, "-Destination", body);

        await Assert.That(run.ExitCode).IsEqualTo(1);
        await Assert.That(run.Error).Contains(row.Says);
        await Assert.That(File.Exists(body)).IsFalse();
    }

    /// <summary>
    /// The links are line ranges, so the notes are built only from a changelog that
    /// matches HEAD, and only when the tag they point into is at HEAD or not made yet.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheNotesAreBuiltOnlyFromTheChangelogTheTagCarries()
    {
        using var scratch = Scratch.Create("notes-git");
        var repository = Directory.CreateDirectory(scratch.In("repository")).FullName;
        var changelog = Path.Combine(repository, "CHANGELOG.md");
        var body = scratch.In("body.md");

        await GitAsync(repository, "init", "-q");
        await File.WriteAllTextAsync(changelog, Fixture);
        await GitAsync(repository, "add", "CHANGELOG.md");
        await GitAsync(repository, "commit", "-q", "-m", "first");

        // Committed and not tagged yet: built, and the run says the tag is still to come.
        var untagged = await NotesAsync("-Version", "9.9.9", "-Path", changelog, "-Destination", body);

        await Assert.That(untagged.ExitCode).IsEqualTo(0);
        await Assert.That(untagged.Output).Contains("does not exist yet");

        // Tagged at HEAD: built.
        await GitAsync(repository, "tag", "v9.9.9");

        var tagged = await NotesAsync("-Version", "9.9.9", "-Path", changelog, "-Destination", body);

        await Assert.That(tagged.ExitCode).IsEqualTo(0);
        await Assert.That(tagged.Output).Contains("which is HEAD");

        // A working copy that differs from HEAD: refused. The edit is above the first
        // section, so no line number moves and no section changes shape.
        await File.WriteAllTextAsync(changelog, Fixture.Replace("about the format.", "about the format, edited.", StringComparison.Ordinal));

        var edited = await NotesAsync("-Version", "9.9.9", "-Path", changelog, "-Destination", body);

        await Assert.That(edited.ExitCode).IsEqualTo(1);
        await Assert.That(edited.Error).Contains("differs from HEAD");

        // Committed, so the tag is behind HEAD: refused.
        await GitAsync(repository, "commit", "-q", "-a", "-m", "second");

        var behind = await NotesAsync("-Version", "9.9.9", "-Path", changelog, "-Destination", body);

        await Assert.That(behind.ExitCode).IsEqualTo(1);
        await Assert.That(behind.Error).Contains("The tag v9.9.9 is at");

        // An older release's page pointed into a tag that does not exist: refused.
        var missing = await NotesAsync("-Version", "9.9.8", "-At", "v9.9.10", "-Path", changelog, "-Destination", body);

        await Assert.That(missing.ExitCode).IsEqualTo(1);
        await Assert.That(missing.Error).Contains("does not exist");

        // And into a newer tag at HEAD that carries the same section: built, linking there.
        await GitAsync(repository, "tag", "v9.9.10");

        var older = await NotesAsync("-Version", "9.9.8", "-At", "v9.9.10", "-Path", changelog, "-Destination", body);

        await Assert.That(older.ExitCode).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(body)).Contains(Repository + "/blob/v9.9.10/CHANGELOG.md?plain=1#L");
    }

    private static async Task<string> ReadChangelogAsync() =>
        (await File.ReadAllTextAsync(Changelog)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static Task<ChildResult> NotesAsync(params string[] arguments) =>
        Child.RunAsync("pwsh", ["-NoProfile", "-NonInteractive", "-File", NotesScript, .. arguments], TimeSpan.FromMinutes(2));

    /// <summary>Runs git in a scratch repository, as a fixed test identity.</summary>
    private static async Task GitAsync(string repository, params string[] arguments)
    {
        var result = await Child.RunAsync(
            "git",
            ["-C", repository, "-c", "user.name=RegisterAI Tests", "-c", "user.email=tests@example.com", .. arguments],
            TimeSpan.FromMinutes(1));

        if (result.ExitCode is not 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} exited {result.ExitCode.ToString(CultureInfo.InvariantCulture)}: {result.Error}");
        }
    }

    /// <summary>The line range an entry of the fixture covers, as a link writes it.</summary>
    private static string Lines(string firstLine, int count)
    {
        var first = Array.FindIndex(Fixture.Split('\n'), line => line.StartsWith(firstLine, StringComparison.Ordinal)) + 1;

        return string.Create(CultureInfo.InvariantCulture, $"L{first}-L{first + count - 1}");
    }

    /// <summary>Each entry: its line number and its text, its lines joined into one.</summary>
    private static List<(int Line, string Text)> Entries(string changelog)
    {
        var entries = new List<(int, string)>();
        var lines = changelog.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var block = new StringBuilder(lines[index]);

            for (var next = index + 1; next < lines.Length && (lines[next].Length is 0 || char.IsWhiteSpace(lines[next][0])); next++)
            {
                _ = block.Append(' ').Append(lines[next]);
            }

            entries.Add((index + 1, Whitespace().Replace(block.ToString(), " ").Trim()));
        }

        return entries;
    }

    /// <summary>Every entry not in the shape the generator reads, with what is wrong.</summary>
    private static List<string> Malformed(string changelog, List<string> icons)
    {
        var offences = new List<string>();

        foreach (var (line, text) in Entries(changelog))
        {
            var where = string.Create(CultureInfo.InvariantCulture, $"CHANGELOG.md:{line}");
            var shaped = EntryShape().Match(text);

            if (!shaped.Success)
            {
                offences.Add($"{where}: not '- <icon> **Headline.** the rest': {Excerpt(text)}");
                continue;
            }

            var icon = shaped.Groups["icon"].Value;
            var headline = shaped.Groups["headline"].Value;

            if (!icons.Contains(icon, StringComparer.Ordinal))
            {
                offences.Add($"{where}: '{icon}' is not in the legend: {Excerpt(text)}");
            }

            if (!headline.EndsWith('.'))
            {
                offences.Add($"{where}: the headline does not end in a full stop: {headline}");
            }
            else if (SentenceBreak().IsMatch(headline[..^1]))
            {
                offences.Add($"{where}: the headline is more than one sentence: {headline}");
            }

            if (headline.Length > HeadlineBudget)
            {
                offences.Add(string.Create(CultureInfo.InvariantCulture, $"{where}: the headline is {headline.Length} characters, over {HeadlineBudget}: {headline}"));
            }
        }

        return offences;
    }

    /// <summary>The legend table's body rows, each split into its cells.</summary>
    private static List<List<string>> LegendRows(string changelog)
    {
        var first = changelog.IndexOf("\n## ", StringComparison.Ordinal);
        var top = first < 0 ? changelog : changelog[..first];

        // The last block above the first version heading whose every line is a table row.
        var block = top.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(static paragraph => paragraph.Split('\n').Select(static line => line.Trim()).Where(static line => line.Length > 0).ToList())
            .LastOrDefault(static rows => rows.Count >= 3 && rows.TrueForAll(static row => row.StartsWith('|')));

        return block is null
            ? []
            : [.. block.Skip(2).Select(static row => (List<string>)[.. row.Trim('|').Split('|').Select(static cell => cell.Trim())])];
    }

    /// <summary>The icon and meaning pairs the legend lists, in reading order.</summary>
    private static List<string> LegendPairs(string changelog) =>
    [
        .. LegendRows(changelog).SelectMany(static row => row
            .Chunk(2)
            .Where(static pair => pair.Length is 2 && pair[0].Length > 0)
            .Select(static pair => $"{pair[0]} {pair[1]}")),
    ];

    private static List<string> LegendIcons(string changelog) =>
        [.. LegendPairs(changelog).Select(static pair => pair.Split(' ')[0])];

    /// <summary>Group headings that repeat, run out of order, or are not Keep a Changelog groups.</summary>
    private static List<string> OutOfOrder(string changelog)
    {
        var offences = new List<string>();
        var seen = new List<string>();

        foreach (var line in changelog.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                seen.Clear();
                continue;
            }

            if (!line.StartsWith("### ", StringComparison.Ordinal))
            {
                continue;
            }

            var group = line[4..].Trim();

            if (!Groups.Contains(group, StringComparer.Ordinal))
            {
                offences.Add($"'{group}' is not a Keep a Changelog group");
            }
            else if (seen.Contains(group, StringComparer.Ordinal))
            {
                offences.Add($"'{group}' appears twice under one version");
            }
            else if (seen.Count > 0 && Array.IndexOf(Groups, group) < Array.IndexOf(Groups, seen[^1]))
            {
                offences.Add($"'{group}' comes after '{seen[^1]}'");
            }

            seen.Add(group);
        }

        return offences;
    }

    private static List<string> SectionHeadings(string changelog) =>
        [.. changelog.Split('\n').Where(static line => line.StartsWith("## ", StringComparison.Ordinal)).Select(static line => line.TrimEnd())];

    /// <summary>Each released section: its version and everything below its heading up to the next one.</summary>
    private static List<(string Version, string Body)> DatedSections(string changelog)
    {
        var sections = new List<(string, string)>();
        var lines = changelog.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (DatedHeading().Match(lines[index].TrimEnd()) is not { Success: true } heading)
            {
                continue;
            }

            var body = lines.Skip(index + 1).TakeWhile(static line => !line.StartsWith("## ", StringComparison.Ordinal));

            sections.Add((heading.Groups["version"].Value, string.Join('\n', body)));
        }

        return sections;
    }

    /// <summary>The prose a section opens with, before its first group.</summary>
    private static string Preamble(string section)
    {
        var firstGroup = section.IndexOf("\n### ", StringComparison.Ordinal);

        return (firstGroup < 0 ? section : section[..firstGroup]).Trim();
    }

    /// <summary>How many words a detail's opening shares with its headline's, case and markup aside.</summary>
    private static int SharedOpening(string headline, string detail)
    {
        var left = Words(headline);
        var right = Words(detail);
        var shared = 0;

        while (shared < left.Count && shared < right.Count && string.Equals(left[shared], right[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        return shared;
    }

    private static List<string> Words(string text) =>
        [.. WordRun().Matches(text.Replace("`", string.Empty, StringComparison.Ordinal).Replace("*", string.Empty, StringComparison.Ordinal)).Select(static word => word.Value.ToUpperInvariant())];

    private static string Excerpt(string line) => line.Length <= 80 ? line : line[..80] + "...";

    /// <summary>An entry: a dash, one icon, and a bold headline.</summary>
    [GeneratedRegex(@"^-\s+(?<icon>\S+)\s+\*\*(?<headline>.+?)\*\*")]
    private static partial Regex EntryShape();

    /// <summary>A sentence ending inside a headline. A full stop in a version number or a file name has no space after it.</summary>
    [GeneratedRegex(@"[.!?]\s")]
    private static partial Regex SentenceBreak();

    /// <summary>A released section's heading: <c>## [1.2.3] - 2026-01-01</c>.</summary>
    [GeneratedRegex(@"^## \[(?<version>\d+\.\d+\.\d+)\] - \d{4}-\d{2}-\d{2}$")]
    private static partial Regex DatedHeading();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("[A-Za-z0-9']+")]
    private static partial Regex WordRun();
}
