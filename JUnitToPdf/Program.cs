using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using JUnitToPdf;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Tables;
using MigraDocCore.Rendering;
using PdfSharpCore.Fonts;

const int MaxFailureDetails = 20;

static void PrintHelp()
{
    Console.WriteLine("junit-to-pdf  —  Convert JUnit XML test results to a PDF report");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  junit-to-pdf [--input <path>] [--output <path>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --input  <path>   JUnit XML file to read  (default: artifacts/TestResults.xml)");
    Console.WriteLine("                    Also reads JUNIT_XML env var.");
    Console.WriteLine("  --output <path>   PDF file to write       (default: artifacts/TestReport.pdf)");
    Console.WriteLine("                    Also reads TEST_REPORT_PDF env var.");
    Console.WriteLine("  --help            Show this help message.");
    Console.WriteLine();
    Console.WriteLine("CI footer env vars (GitLab or GitHub Actions):");
    Console.WriteLine("  CI_PIPELINE_ID / GITHUB_RUN_ID");
    Console.WriteLine("  CI_COMMIT_REF_NAME / GITHUB_REF_NAME");
    Console.WriteLine("  CI_COMMIT_SHA / GITHUB_SHA");
}

static string? GetArg(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (int i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

static bool HasFlag(string name) =>
    Environment.GetCommandLineArgs()
               .Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

// Prefer GitLab CI variable; fall back to GitHub Actions equivalent.
static string CiEnv(string gitlabVar, string githubVar, string fallback = "N/A") =>
    Environment.GetEnvironmentVariable(gitlabVar)
    ?? Environment.GetEnvironmentVariable(githubVar)
    ?? fallback;

// ---- Entry point ----

if (HasFlag("--help") || HasFlag("-h"))
{
    PrintHelp();
    return 0;
}

var input =
    GetArg("--input")
    ?? Environment.GetEnvironmentVariable("JUNIT_XML")
    ?? "artifacts/TestResults.xml";

var output =
    GetArg("--output")
    ?? Environment.GetEnvironmentVariable("TEST_REPORT_PDF")
    ?? "artifacts/TestReport.pdf";

if (!File.Exists(input))
{
    Console.Error.WriteLine($"error: input file not found: {input}");
    Console.Error.WriteLine("Run with --help for usage.");
    return 1;
}

XDocument docXml;
try
{
    docXml = XDocument.Load(input);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: could not parse JUnit XML from '{input}': {ex.Message}");
    return 1;
}

var pipeline = CiEnv("CI_PIPELINE_ID",     "GITHUB_RUN_ID");
var branch   = CiEnv("CI_COMMIT_REF_NAME", "GITHUB_REF_NAME");
var commit   = CiEnv("CI_COMMIT_SHA",       "GITHUB_SHA");

var parsed = TestResultParser.Parse(docXml);

var suites         = parsed.Suites;
var testcases      = parsed.TestCases;
var suiteSummaries = parsed.SuiteSummaries;

int total   = testcases.Count;
int failed  = testcases.Count(t => t.Status == "fail");
int skipped = testcases.Count(t => t.Status == "skip");
int passed  = Math.Max(0, total - failed - skipped);

string assemblyName = parsed.AssemblyName;
string assemblyTime = parsed.TotalTimeSeconds.ToString("0.###", CultureInfo.InvariantCulture);

var slow = testcases
    .Where(t => t.Status != "skip")
    .OrderByDescending(t => t.Time)
    .Take(5)
    .Select(t => (t.ClassName, t.Name))
    .ToHashSet();

var groups = testcases
    .GroupBy(t => t.ClassName ?? "", StringComparer.Ordinal)
    .OrderBy(g => StringHelpers.ShortClassName(g.Key), StringComparer.OrdinalIgnoreCase)
    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
    .Select(g => new
    {
        ClassName  = g.Key,
        ClassShort = StringHelpers.ShortClassName(g.Key),
        Tests = g
            .OrderBy(t => t.Status == "fail" ? 0 : t.Status == "pass" ? 1 : 2)
            .ThenByDescending(t => t.Time)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
    })
    .ToList();

// ----- Font selection / fallback -----
var fontDir  = Path.Combine(AppContext.BaseDirectory, "Nickel City");
var resolver = new NickelCityFontResolver(fontDir);
bool nickelAvailable = resolver.HasFonts;

if (nickelAvailable && GlobalFontSettings.FontResolver is null)
    GlobalFontSettings.FontResolver = resolver;

string baseFont   = nickelAvailable ? "Nickel City" : "Helvetica";
string bulletFont = "Helvetica"; // force bullets to a reliable glyph set

// Slight size bump when using Nickel City so the report doesn't look tiny.
int baseSize   = nickelAvailable ? 10 : 9;
int smallSize  = nickelAvailable ? 9  : 8;
int headerSize = nickelAvailable ? 10 : 9;
int titleSize  = nickelAvailable ? 22 : 20;

// ----- PDF Generation -----
var doc = new Document();
doc.Info.Title = "Unit Test Report";

var section = doc.AddSection();
section.PageSetup.TopMargin    = Unit.FromCentimeter(1.8);
section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
section.PageSetup.LeftMargin   = Unit.FromCentimeter(1.8);
section.PageSetup.RightMargin  = Unit.FromCentimeter(1.8);

// Styles
var normal = doc.Styles["Normal"]!;
normal.Font.Name = baseFont;
normal.Font.Size = baseSize;

var titleStyle = doc.Styles.AddStyle("Title", "Normal");
titleStyle.Font.Name = baseFont;
titleStyle.Font.Size = titleSize;
titleStyle.Font.Bold = true;

var headerStyle = doc.Styles.AddStyle("TableHeader", "Normal");
headerStyle.Font.Name  = baseFont;
headerStyle.Font.Size  = headerSize;
headerStyle.Font.Color = Colors.White;
headerStyle.Font.Bold  = true;

var smallStyle = doc.Styles.AddStyle("Small", "Normal");
smallStyle.Font.Name  = baseFont;
smallStyle.Font.Size  = smallSize;
smallStyle.Font.Color = Colors.Gray;

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
sRow.TopPadding    = Unit.FromPoint(8);
sRow.BottomPadding = Unit.FromPoint(8);

sRow.Cells[0].AddParagraph($"Test Assembly: {StringHelpers.PrettyIdentifier(assemblyName, spaceAfterDots: true)}");
sRow.Cells[0].AddParagraph($"Suites: {suites.Count}");
sRow.Cells[0].AddParagraph($"Total Time (s): {assemblyTime}");

void AddLegend(Cell cell, string label, int value, Color color)
{
    var p = cell.AddParagraph();
    p.AddFormattedText("• ", new Font { Name = bulletFont, Color = color, Bold = true, Size = headerSize + 2 });
    p.AddText($"{label}: {value}");
}

AddLegend(sRow.Cells[1], "Passed",  passed,  Colors.DarkGreen);
AddLegend(sRow.Cells[1], "Failed",  failed,  Colors.DarkRed);
AddLegend(sRow.Cells[1], "Skipped", skipped, Colors.DarkOrange);

section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);

// Summary by suite
var sh = section.AddParagraph("Summary by suite");
sh.Format.Font.Bold  = true;
sh.Format.SpaceAfter = Unit.FromPoint(4);

var st = section.AddTable();
st.Borders.Width    = 0.25;
st.Borders.Color    = Colors.LightGray;
st.Format.Font.Size = smallSize;

// Total width = 16.9 cm (within A4 usable width at 1.8 cm margins)
st.AddColumn(Unit.FromCentimeter(8.8)); // Suite
st.AddColumn(Unit.FromCentimeter(1.6)); // Passed
st.AddColumn(Unit.FromCentimeter(1.6)); // Failed
st.AddColumn(Unit.FromCentimeter(1.6)); // Skipped
st.AddColumn(Unit.FromCentimeter(1.5)); // Total
st.AddColumn(Unit.FromCentimeter(1.8)); // Time

var sth = st.AddRow();
sth.Shading.Color = new Color(0x2F, 0x55, 0x97);

sth.Cells[0].AddParagraph("Suite").Style    = "TableHeader";
sth.Cells[1].AddParagraph("Passed").Style   = "TableHeader";
sth.Cells[2].AddParagraph("Failed").Style   = "TableHeader";
sth.Cells[3].AddParagraph("Skipped").Style  = "TableHeader";
sth.Cells[4].AddParagraph("Total").Style    = "TableHeader";
sth.Cells[5].AddParagraph("Time (s)").Style = "TableHeader";

for (int c = 1; c <= 5; c++)
    sth.Cells[c].Format.Alignment = ParagraphAlignment.Right;

for (int i = 0; i < suiteSummaries.Count; i++)
{
    var ss = suiteSummaries[i];
    var r  = st.AddRow();

    if (i % 2 == 1)
        r.Shading.Color = new Color(0xF5, 0xF7, 0xFA);

    r.Cells[0].AddParagraph(StringHelpers.Clip(ss.NameDisplay, 200));
    r.Cells[1].AddParagraph(ss.Passed.ToString(CultureInfo.InvariantCulture));
    r.Cells[2].AddParagraph(ss.Failed.ToString(CultureInfo.InvariantCulture));
    r.Cells[3].AddParagraph(ss.Skipped.ToString(CultureInfo.InvariantCulture));
    r.Cells[4].AddParagraph(ss.Total.ToString(CultureInfo.InvariantCulture));
    r.Cells[5].AddParagraph(ss.Time.ToString("0.###", CultureInfo.InvariantCulture));

    for (int c = 1; c <= 5; c++)
        r.Cells[c].Format.Alignment = ParagraphAlignment.Right;

    if (ss.Failed > 0)
    {
        r.Cells[2].Shading.Color      = new Color(0xFF, 0xEB, 0xEE);
        r.Cells[0].Borders.Left.Width = 2;
        r.Cells[0].Borders.Left.Color = Colors.DarkRed;
    }
}

section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(10);

// Results heading
var h = section.AddParagraph("Test Results");
h.Format.Font.Bold  = true;
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

var tableHeader = table.AddRow();
tableHeader.Shading.Color = new Color(0x2F, 0x55, 0x97);

tableHeader.Cells[0].AddParagraph("Status").Style    = "TableHeader";
tableHeader.Cells[1].AddParagraph("Test Name").Style = "TableHeader";
tableHeader.Cells[2].AddParagraph("Time (s)").Style  = "TableHeader";
tableHeader.Cells[2].Format.Alignment = ParagraphAlignment.Right;

static Color StatusColor(string status) => status switch
{
    "fail" => Colors.DarkRed,
    "skip" => Colors.DarkOrange,
    _      => Colors.DarkGreen
};

static string StatusText(string status) => status switch
{
    "fail" => "Fail",
    "skip" => "Skip",
    _      => "Pass"
};

int rowIndex = 0;
foreach (var g in groups)
{
    var sub = table.AddRow();
    sub.Shading.Color = Colors.WhiteSmoke;
    sub.TopPadding    = Unit.FromPoint(4);
    sub.BottomPadding = Unit.FromPoint(4);

    sub.Cells[0].MergeRight = 2;
    sub.Cells[0].AddParagraph(g.ClassShort).Format.Font.Bold = true;

    foreach (var tc in g.Tests)
    {
        var r = table.AddRow();

        if (rowIndex % 2 == 1)
            r.Shading.Color = new Color(0xF5, 0xF7, 0xFA);

        var sp = r.Cells[0].AddParagraph();
        sp.AddFormattedText("• ", new Font { Name = bulletFont, Color = StatusColor(tc.Status), Bold = true, Size = headerSize + 2 });
        sp.AddText(StatusText(tc.Status));
        r.Cells[0].VerticalAlignment = VerticalAlignment.Top;

        r.Cells[1].AddParagraph(StringHelpers.Clip(tc.NameDisplay, 260));
        r.Cells[1].VerticalAlignment = VerticalAlignment.Top;

        r.Cells[2].AddParagraph(tc.Time.ToString("0.000000", CultureInfo.InvariantCulture));
        r.Cells[2].Format.Alignment = ParagraphAlignment.Right;

        if (slow.Contains((tc.ClassName, tc.Name)))
        {
            r.Cells[2].Shading.Color      = new Color(0xFF, 0xF3, 0xE0);
            r.Cells[0].Borders.Left.Width = 2;
            r.Cells[0].Borders.Left.Color = Colors.DarkOrange;
        }

        rowIndex++;
    }
}

// Failures detail section
var failedTests = testcases.Where(t => t.Status == "fail").Take(MaxFailureDetails).ToList();
if (failedTests.Count > 0)
{
    section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(10);

    var fh = section.AddParagraph($"Failures (first {MaxFailureDetails})");
    fh.Format.Font.Bold  = true;
    fh.Format.SpaceAfter = Unit.FromPoint(4);

    foreach (var ft in failedTests)
    {
        var displayName = string.IsNullOrWhiteSpace(ft.ClassName)
            ? ft.Name
            : $"{ft.ClassName}::{ft.Name}";

        section.AddParagraph(StringHelpers.PrettyIdentifier(displayName, spaceAfterDots: true)).Format.Font.Bold = true;

        var d = section.AddParagraph(StringHelpers.Clip(ft.Details, 1600), "Small");
        d.Format.SpaceAfter = Unit.FromPoint(6);
    }
}

// Footer — MigraDoc renders footer paragraphs bottom-to-top, so add page number
// first (it ends up at the very bottom) and CI metadata second (it sits above).
section.Footers.Primary.AddParagraph()
    .AddPageField();

section.Footers.Primary.AddParagraph(
        $"CI Pipeline: {pipeline}  Branch: {branch}  Commit: {commit}")
    .Style = "Small";

var pdfRenderer = new PdfDocumentRenderer(unicode: true) { Document = doc };
pdfRenderer.RenderDocument();

var outDir = Path.GetDirectoryName(output);
if (!string.IsNullOrWhiteSpace(outDir))
    Directory.CreateDirectory(outDir);

try
{
    pdfRenderer.PdfDocument.Save(output);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: could not save PDF to '{output}': {ex.Message}");
    return 1;
}

Console.WriteLine($"PDF written to {output}");
return 0;
