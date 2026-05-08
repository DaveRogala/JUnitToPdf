using System.Globalization;
using System.Xml.Linq;

namespace JUnitToPdf;

record TestCase(
    string Name,
    string NameDisplay,
    string ClassName,
    string ClassShort,
    double Time,
    string Status,   // "pass" | "fail" | "skip"
    string Details);

record SuiteSummary(
    string Name,
    string NameDisplay,
    int    Total,
    int    Passed,
    int    Failed,
    int    Skipped,
    double Time);

record ParsedResults(
    IReadOnlyList<XElement>      Suites,
    IReadOnlyList<TestCase>      TestCases,
    IReadOnlyList<SuiteSummary>  SuiteSummaries,
    string                       AssemblyName,
    double                       TotalTimeSeconds);

static class TestResultParser
{
    public static ParsedResults Parse(XDocument doc)
    {
        var suites = doc.Descendants("testsuite").ToList();
        if (suites.Count == 0)
            throw new InvalidOperationException("No <testsuite> elements found in JUnit XML.");

        var testCases = suites
            .SelectMany(s => s.Elements("testcase"))
            .Select(ParseTestCase)
            .ToList();

        string assemblyName =
            suites.Select(s => s.Attribute("name")?.Value)
                  .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            ?? "JUnit";

        // Only sum time from top-level suites to avoid double-counting nested suites.
        var topLevelSuites = suites
            .Where(s => s.Parent?.Name.LocalName != "testsuite")
            .ToList();

        double totalTime = topLevelSuites.Sum(s => ParseDouble(s.Attribute("time")?.Value));
        if (totalTime <= 0)
            totalTime = testCases.Sum(t => t.Time);

        var suiteSummaries = suites
            .Select(BuildSuiteSummary)
            .OrderByDescending(x => x.Failed)
            .ThenByDescending(x => x.Time)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ParsedResults(suites, testCases, suiteSummaries, assemblyName, totalTime);
    }

    private static TestCase ParseTestCase(XElement tc)
    {
        var failure   = tc.Element("failure");
        var error     = tc.Element("error");
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

        details = StringHelpers.NormalizeWhitespace(details);

        var className = tc.Attribute("classname")?.Value ?? "";
        var name      = tc.Attribute("name")?.Value ?? "";

        return new TestCase(
            Name:        name,
            NameDisplay: StringHelpers.PrettyIdentifier(name, spaceAfterDots: false),
            ClassName:   className,
            ClassShort:  StringHelpers.ShortClassName(className),
            Time:        ParseDouble(tc.Attribute("time")?.Value),
            Status:      status,
            Details:     details);
    }

    private static SuiteSummary BuildSuiteSummary(XElement s)
    {
        var tcs   = s.Elements("testcase").ToList();
        int total = tcs.Count;
        int fail  = tcs.Count(tc => tc.Element("failure") != null || tc.Element("error") != null);
        int skip  = tcs.Count(tc => tc.Element("skipped") != null);
        int pass  = Math.Max(0, total - fail - skip);

        double time = ParseDouble(s.Attribute("time")?.Value);
        if (time <= 0)
            time = tcs.Sum(tc => ParseDouble(tc.Attribute("time")?.Value));

        string name = s.Attribute("name")?.Value ?? "(unnamed suite)";

        return new SuiteSummary(
            Name:        name,
            NameDisplay: StringHelpers.PrettyIdentifier(name, spaceAfterDots: true),
            Total:       total,
            Passed:      pass,
            Failed:      fail,
            Skipped:     skip,
            Time:        time);
    }

    internal static double ParseDouble(string? s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
