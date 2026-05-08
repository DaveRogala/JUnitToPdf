using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using JUnitToPdf;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Tables;
using MigraDocCore.Rendering;
using PdfSharpCore.Fonts;

static string? GetArg(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (int i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];

    return null;
}

static double ParseDouble(string? s)
    => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

static string Clip(string? s, int max)
{
    s ??= "";
    if (s.Length <= max) return s;
    return s.Substring(0, max) + "…";
}

static string NormalizeWhitespace(string s)
{
    if (string.IsNullOrWhiteSpace(s)) return "";
    var sb = new StringBuilder(s.Length);
    bool lastWasWs = false;
    foreach (var ch in s)
    {
        var isWs = char.IsWhiteSpace(ch);
        if (isWs)
        {
            if (!lastWasWs) sb.Append(' ');
        }
        else
        {
            sb.Append(ch);
        }
        lastWasWs = isWs;
    }
    return sb.ToString().Trim();
}

// Make long identifiers look professional and wrap naturally (no zero-width chars).
// - Converts underscores to spaces.
// - Inserts spaces at camelCase boundaries.
// - Optionally inserts a space after dots to allow wrapping on namespaces.
static string PrettyIdentifier(string s, bool spaceAfterDots)
{
    if (string.IsNullOrWhiteSpace(s)) return "";

    s = s.Replace("_", " ");
    if (spaceAfterDots)
        s = s.Replace(".", ". ");

    var sb = new StringBuilder(s.Length + 16);
    char prev = '\0';
    for (int i = 0; i < s.Length; i++)
    {
        char c = s[i];

        if (i > 0)
        {
            if ((char.IsLower(prev) && char.IsUpper(c)) || (char.IsDigit(prev) && char.IsLetter(c)))
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                    sb.Append(' ');
            }
        }

        sb.Append(c);
        prev = c;
    }

    return NormalizeWhitespace(sb.ToString());
}

static string ShortClassName(string? full)
{
    if (string.IsNullOrWhiteSpace(full)) return "(no classname)";

    var s = full;

    var plus = s.LastIndexOf('+');
    if (plus >= 0 && plus < s.Length - 1)
        s = s[(plus + 1)..];

    var dot = s.LastIndexOf('.');
    if (dot >= 0 && dot < s.Length - 1)
        s = s[(dot + 1)..];

    return s;
}

// ---- Nickel City font support ----
// Copies font files to output via csproj so they end up at: <base>/Nickel City/*.otf|*.ttf
// This resolver maps the family name "Nickel City" to discovered font files.


var input =
    GetArg("--input")
    ?? Environment.GetEnvironmentVariable("JUNIT_XML")
    ?? "artifacts/TestResults.xml";

var output =
    GetArg("--output")
    ?? Environment.GetEnvironmentVariable("TEST_REPORT_PDF")
    ?? "artifacts/TestReport.pdf";

var pipeline = Environment.GetEnvironmentVariable("CI_PIPELINE_ID") ?? "N/A";
var branch = Environment.GetEnvironmentVariable("CI_COMMIT_REF_NAME") ?? "N/A";
var commit = Environment.GetEnvironmentVariable("CI_COMMIT_SHA") ?? "N/A";

var docXml = XDocument.Load(input);

// Collect ALL testsuite elements (handles <testsuites> root, multiple suites, nested suites).
var suites = docXml.Descendants("testsuite").ToList();
if (suites.Count == 0)
    throw new InvalidOperationException("No <testsuite> elements found in JUnit XML.");

var testcases =
    suites.SelectMany(s => s.Elements("testcase"))
          .Select(tc =>
          {
              var failure = tc.Element("failure");
              var error = tc.Element("error");
              var skippedEl = tc.Element("skipped");

              string status =
                  skippedEl != null ? "skip" :
                  (failure != null || error != null) ? "fail" :
                  "pass";

              var details =
                  failure?.Attribute("message")?.Value
                  ?? error?.Attribute("message")?.Value
                  ?? failure?.Value
                  ?? error?.Value
                  ?? "";

              details = NormalizeWhitespace(details);

              var className = tc.Attribute("classname")?.Value ?? "";
              var name = tc.Attribute("name")?.Value ?? "";

              return new
              {
                  Name = name,
                  NameDisplay = PrettyIdentifier(name, spaceAfterDots: false),
                  ClassName = className,
                  ClassShort = ShortClassName(className),
                  Time = ParseDouble(tc.Attribute("time")?.Value),
                  Status = status,
                  Details = details
              };
          })
          .ToList();

int total = testcases.Count;
int failed = testcases.Count(t => t.Status == "fail");
int skipped = testcases.Count(t => t.Status == "skip");
int passed = Math.Max(0, total - failed - skipped);

string assemblyName =
    suites.Select(s => s.Attribute("name")?.Value)
          .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
    ?? "JUnit";

double totalTimeSeconds = suites.Sum(s => ParseDouble(s.Attribute("time")?.Value));
if (totalTimeSeconds <= 0)
    totalTimeSeconds = testcases.Sum(t => t.Time);

string assemblyTime = totalTimeSeconds.ToString("0.###", CultureInfo.InvariantCulture);

var suiteSummaries =
    suites.Select(s =>
    {
        var tcs = s.Elements("testcase").ToList();

        int sTotal = tcs.Count;
        int sFail = tcs.Count(tc => tc.Element("failure") != null || tc.Element("error") != null);
        int sSkip = tcs.Count(tc => tc.Element("skipped") != null);
        int sPass = Math.Max(0, sTotal - sFail - sSkip);

        double sTime = ParseDouble(s.Attribute("time")?.Value);
        if (sTime <= 0)
            sTime = tcs.Sum(tc => ParseDouble(tc.Attribute("time")?.Value));

        string sName = s.Attribute("name")?.Value ?? "(unnamed suite)";

        return new
        {
            Name = sName,
            NameDisplay = PrettyIdentifier(sName, spaceAfterDots: true),
            Total = sTotal,
            Passed = sPass,
            Failed = sFail,
            Skipped = sSkip,
            Time = sTime
        };
    })
    .OrderByDescending(x => x.Failed)
    .ThenByDescending(x => x.Time)
    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
    .ToList();

var slow = testcases
    .Where(t => t.Status != "skip")
    .OrderByDescending(t => t.Time)
    .Take(5)
    .Select(t => (t.ClassName, t.Name))
    .ToHashSet();

var groups = testcases
    .GroupBy(t => t.ClassName ?? "", StringComparer.Ordinal)
    .OrderBy(g => ShortClassName(g.Key), StringComparer.OrdinalIgnoreCase)
    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
    .Select(g => new
    {
        ClassName = g.Key,
        ClassShort = ShortClassName(g.Key),
        Tests = g
            .OrderBy(t => t.Status == "fail" ? 0 : t.Status == "pass" ? 1 : 2)
            .ThenByDescending(t => t.Time)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
    })
    .ToList();

// ----- Font selection / fallback -----
var fontDir = Path.Combine(AppContext.BaseDirectory, "Nickel City");
var resolver = new NickelCityFontResolver(fontDir);
bool nickelAvailable = resolver.HasFonts;

if (nickelAvailable && GlobalFontSettings.FontResolver is null)
    GlobalFontSettings.FontResolver = resolver;

string baseFont = nickelAvailable ? "Nickel City" : "Helvetica";
string bulletFont = "Helvetica"; // force bullets to a reliable glyph set

// Slight size bump when using Nickel City so the report doesn't look tiny.
int baseSize = nickelAvailable ? 10 : 9;
int smallSize = nickelAvailable ? 9 : 8;
int headerSize = nickelAvailable ? 10 : 9;
int titleSize = nickelAvailable ? 22 : 20;

// ----- PDF Generation -----
var doc = new Document();
doc.Info.Title = "Unit Test Report";

var section = doc.AddSection();
section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);

// Styles
var normal = doc.Styles["Normal"]!;
normal.Font.Name = baseFont;
normal.Font.Size = baseSize;

var titleStyle = doc.Styles.AddStyle("Title", "Normal");
titleStyle.Font.Name = baseFont;
titleStyle.Font.Size = titleSize;
titleStyle.Font.Bold = true; // should map to Nickel City Bold when available

var headerStyle = doc.Styles.AddStyle("TableHeader", "Normal");
headerStyle.Font.Name = baseFont;
headerStyle.Font.Size = headerSize;
headerStyle.Font.Color = Colors.White;
headerStyle.Font.Bold = true;

var small = doc.Styles.AddStyle("Small", "Normal");
small.Font.Name = baseFont;
small.Font.Size = smallSize;
small.Font.Color = Colors.Gray;

// Title
var title = section.AddParagraph("Unit Test Report", "Title");
title.Format.SpaceAfter = Unit.FromPoint(8);

// Summary box
var summary = section.AddTable();
summary.Borders.Width = 0.75;
summary.Borders.Color = Colors.LightGray;
summary.AddColumn(Unit.FromCentimeter(12));
summary.AddColumn(Unit.FromCentimeter(4.5));

var sRow = summary.AddRow();
sRow.Shading.Color = Colors.WhiteSmoke;
sRow.TopPadding = Unit.FromPoint(8);
sRow.BottomPadding = Unit.FromPoint(8);

sRow.Cells[0].AddParagraph($"Test Assembly: {PrettyIdentifier(assemblyName, spaceAfterDots: true)}");
sRow.Cells[0].AddParagraph($"Suites: {suites.Count}");
sRow.Cells[0].AddParagraph($"Total Time (s): {assemblyTime}");

void AddLegend(Cell cell, string label, int value, Color color)
{
    var p = cell.AddParagraph();
    // Bullet rendered with a known-good font, while the rest uses the report font.
    p.AddFormattedText("• ", new Font { Name = bulletFont, Color = color, Bold = true, Size = headerSize + 2 });
    p.AddText($"{label}: {value}");
}

AddLegend(sRow.Cells[1], "Passed", passed, Colors.DarkGreen);
AddLegend(sRow.Cells[1], "Failed", failed, Colors.DarkRed);
AddLegend(sRow.Cells[1], "Skipped", skipped, Colors.DarkOrange);

section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);

// Summary by suite
var sh = section.AddParagraph("Summary by suite");
sh.Format.Font.Bold = true;
sh.Format.SpaceAfter = Unit.FromPoint(4);

var st = section.AddTable();
st.Borders.Width = 0.25;
st.Borders.Color = Colors.LightGray;
st.Format.Font.Size = smallSize; // slightly smaller so it fits comfortably

// Total width = 16.9cm
st.AddColumn(Unit.FromCentimeter(8.8)); // Suite
st.AddColumn(Unit.FromCentimeter(1.6)); // Passed
st.AddColumn(Unit.FromCentimeter(1.6)); // Failed
st.AddColumn(Unit.FromCentimeter(1.6)); // Skipped
st.AddColumn(Unit.FromCentimeter(1.5)); // Total
st.AddColumn(Unit.FromCentimeter(1.8)); // Time

var sth = st.AddRow();
sth.Shading.Color = new Color(0x2F, 0x55, 0x97);

sth.Cells[0].AddParagraph("Suite").Style = "TableHeader";
sth.Cells[1].AddParagraph("Passed").Style = "TableHeader";
sth.Cells[2].AddParagraph("Failed").Style = "TableHeader";
sth.Cells[3].AddParagraph("Skipped").Style = "TableHeader";
sth.Cells[4].AddParagraph("Total").Style = "TableHeader";
sth.Cells[5].AddParagraph("Time (s)").Style = "TableHeader";

for (int c = 1; c <= 5; c++)
    sth.Cells[c].Format.Alignment = ParagraphAlignment.Right;

for (int i = 0; i < suiteSummaries.Count; i++)
{
    var ss = suiteSummaries[i];
    var r = st.AddRow();

    if (i % 2 == 1)
        r.Shading.Color = new Color(0xF5, 0xF7, 0xFA);

    r.Cells[0].AddParagraph(Clip(ss.NameDisplay, 200));
    r.Cells[1].AddParagraph(ss.Passed.ToString(CultureInfo.InvariantCulture));
    r.Cells[2].AddParagraph(ss.Failed.ToString(CultureInfo.InvariantCulture));
    r.Cells[3].AddParagraph(ss.Skipped.ToString(CultureInfo.InvariantCulture));
    r.Cells[4].AddParagraph(ss.Total.ToString(CultureInfo.InvariantCulture));
    r.Cells[5].AddParagraph(ss.Time.ToString("0.###", CultureInfo.InvariantCulture));

    for (int c = 1; c <= 5; c++)
        r.Cells[c].Format.Alignment = ParagraphAlignment.Right;

    if (ss.Failed > 0)
    {
        r.Cells[2].Shading.Color = new Color(0xFF, 0xEB, 0xEE);
        r.Cells[0].Borders.Left.Width = 2;
        r.Cells[0].Borders.Left.Color = Colors.DarkRed;
    }
}

section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(10);

// Results heading
var h = section.AddParagraph("Test Results");
h.Format.Font.Bold = true;
h.Format.SpaceAfter = Unit.FromPoint(2);

var hint = section.AddParagraph("Highlighted time cells indicate the slowest tests in this run (top 5).", "Small");
hint.Format.SpaceAfter = Unit.FromPoint(6);

// Results table
var table = section.AddTable();
table.Borders.Width = 0.25;
table.Borders.Color = Colors.LightGray;

table.AddColumn(Unit.FromCentimeter(2.2));  // Status
table.AddColumn(Unit.FromCentimeter(12.3)); // Test Name

table.AddColumn(Unit.FromCentimeter(2.0));  // Time

var header = table.AddRow();
header.Shading.Color = new Color(0x2F, 0x55, 0x97);

header.Cells[0].AddParagraph("Status").Style = "TableHeader";
header.Cells[1].AddParagraph("Test Name").Style = "TableHeader";
header.Cells[2].AddParagraph("Time (s)").Style = "TableHeader";
header.Cells[2].Format.Alignment = ParagraphAlignment.Right;

static Color StatusColor(string status) => status switch
{
    "fail" => Colors.DarkRed,
    "skip" => Colors.DarkOrange,
    _ => Colors.DarkGreen
};

static string StatusText(string status) => status switch
{
    "fail" => "Fail",
    "skip" => "Skip",
    _ => "Pass"
};

int rowIndex = 0;
foreach (var g in groups)
{
    var sub = table.AddRow();
    sub.Shading.Color = Colors.WhiteSmoke;
    sub.TopPadding = Unit.FromPoint(4);
    sub.BottomPadding = Unit.FromPoint(4);

    sub.Cells[0].MergeRight = 2;

    var cp = sub.Cells[0].AddParagraph(g.ClassShort);
    cp.Format.Font.Bold = true;

    foreach (var tc in g.Tests)
    {
        var r = table.AddRow();

        if (rowIndex % 2 == 1)
            r.Shading.Color = new Color(0xF5, 0xF7, 0xFA);

        // Bullet rendered with a known-good font.
        var sp = r.Cells[0].AddParagraph();
        sp.AddFormattedText("• ", new Font { Name = bulletFont, Color = StatusColor(tc.Status), Bold = true, Size = headerSize + 2 });
        sp.AddText(StatusText(tc.Status));
        r.Cells[0].VerticalAlignment = VerticalAlignment.Top;

        r.Cells[1].AddParagraph(Clip(tc.NameDisplay, 260));
        r.Cells[1].VerticalAlignment = VerticalAlignment.Top;

        r.Cells[2].AddParagraph(tc.Time.ToString("0.000000", CultureInfo.InvariantCulture));
        r.Cells[2].Format.Alignment = ParagraphAlignment.Right;

        if (slow.Contains((tc.ClassName, tc.Name)))
        {
            r.Cells[2].Shading.Color = new Color(0xFF, 0xF3, 0xE0);
            r.Cells[0].Borders.Left.Width = 2;
            r.Cells[0].Borders.Left.Color = Colors.DarkOrange;
        }

        rowIndex++;
    }
}

// Failures section (first 20)
var failedTests = testcases.Where(t => t.Status == "fail").Take(20).ToList();
if (failedTests.Count > 0)
{
    section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(10);

    var fh = section.AddParagraph("Failures (first 20)");
    fh.Format.Font.Bold = true;
    fh.Format.SpaceAfter = Unit.FromPoint(4);

    foreach (var ft in failedTests)
    {
        var displayName = string.IsNullOrWhiteSpace(ft.ClassName)
            ? ft.Name
            : $"{ft.ClassName}::{ft.Name}";

        section.AddParagraph(PrettyIdentifier(displayName, spaceAfterDots: true)).Format.Font.Bold = true;

        var d = section.AddParagraph(Clip(ft.Details, 1600), "Small");
        d.Format.SpaceAfter = Unit.FromPoint(6);
    }
}

// Footer (pipeline metadata + page numbers)
section.Footers.Primary.AddParagraph(
        $"CI Pipeline: {pipeline} " +
        $"Branch: {branch} " +
        $"Commit: {commit}")
    .Style = "Small";

section.Footers.Primary.AddParagraph()
    .AddPageField();

var renderer = new PdfDocumentRenderer(unicode: true)
{
    Document = doc
};

renderer.RenderDocument();

var outDir = Path.GetDirectoryName(output);
if (!string.IsNullOrWhiteSpace(outDir))
    Directory.CreateDirectory(outDir);

renderer.PdfDocument.Save(output);
