using System.Xml.Linq;
using JUnitToPdf;
using Xunit;

namespace JUnitToPdf.Tests;

public class TestResultParserTests
{
    private static XDocument Xml(string content) => XDocument.Parse(content);

    // ---- Basic counts ----

    [Fact]
    public void Parse_SingleSuiteAllPassing_CorrectCounts()
    {
        var doc = Xml("""
            <testsuites>
              <testsuite name="Suite" time="1.0">
                <testcase classname="C" name="test1" time="0.5"/>
                <testcase classname="C" name="test2" time="0.5"/>
              </testsuite>
            </testsuites>
            """);

        var result = TestResultParser.Parse(doc);

        Assert.Equal(2, result.TestCases.Count);
        Assert.All(result.TestCases, t => Assert.Equal("pass", t.Status));
    }

    [Fact]
    public void Parse_FailureElement_StatusIsFail()
    {
        var doc = Xml("""
            <testsuites>
              <testsuite name="Suite" time="1.0">
                <testcase classname="C" name="failing" time="0.1">
                  <failure message="assert failed">stack trace here</failure>
                </testcase>
              </testsuite>
            </testsuites>
            """);

        var result = TestResultParser.Parse(doc);

        Assert.Single(result.TestCases);
        Assert.Equal("fail", result.TestCases[0].Status);
        Assert.Equal("assert failed", result.TestCases[0].Details);
    }

    [Fact]
    public void Parse_ErrorElement_StatusIsFail()
    {
        var doc = Xml("""
            <testsuite name="Suite" time="0.5">
              <testcase classname="C" name="erroring" time="0.1">
                <error message="unexpected error"/>
              </testcase>
            </testsuite>
            """);

        var result = TestResultParser.Parse(doc);

        Assert.Equal("fail", result.TestCases[0].Status);
    }

    [Fact]
    public void Parse_SkippedElement_StatusIsSkip()
    {
        var doc = Xml("""
            <testsuite name="Suite" time="0.0">
              <testcase classname="C" name="skipped_test" time="0.0">
                <skipped/>
              </testcase>
            </testsuite>
            """);

        var result = TestResultParser.Parse(doc);

        Assert.Equal("skip", result.TestCases[0].Status);
    }

    // ---- Details extraction ----

    [Fact]
    public void Parse_FailureWithNoMessageAttr_UsesElementText()
    {
        var doc = Xml("""
            <testsuite name="S" time="0">
              <testcase classname="C" name="t" time="0">
                <failure>body text details</failure>
              </testcase>
            </testsuite>
            """);

        var result = TestResultParser.Parse(doc);

        Assert.Equal("body text details", result.TestCases[0].Details);
    }

    // ---- Time calculation ----

    [Fact]
    public void Parse_TotalTime_SumsTopLevelSuitesOnly()
    {
        // Outer suite time=10 includes inner suite time=5; should not double-count.
        var doc = Xml("""
            <testsuite name="Outer" time="10">
              <testsuite name="Inner" time="5">
                <testcase classname="C" name="t" time="5"/>
              </testsuite>
              <testcase classname="C" name="t2" time="5"/>
            </testsuite>
            """);

        var result = TestResultParser.Parse(doc);

        // Only top-level suite (Outer) should be summed → 10, not 15.
        Assert.Equal(10.0, result.TotalTimeSeconds);
    }

    [Fact]
    public void Parse_TotalTimeFallsBackToTestCaseSum_WhenSuiteTimeIsZero()
    {
        var doc = Xml("""
            <testsuite name="Suite" time="0">
              <testcase classname="C" name="t1" time="1.5"/>
              <testcase classname="C" name="t2" time="2.5"/>
            </testsuite>
            """);

        var result = TestResultParser.Parse(doc);

        Assert.Equal(4.0, result.TotalTimeSeconds);
    }

    // ---- Assembly name ----

    [Fact]
    public void Parse_AssemblyName_UsesFirstNonEmptySuiteName()
    {
        var doc = Xml("""
            <testsuites>
              <testsuite name="" time="0"/>
              <testsuite name="MyAssembly" time="1">
                <testcase classname="C" name="t" time="0"/>
              </testsuite>
            </testsuites>
            """);

        var result = TestResultParser.Parse(doc);

        Assert.Equal("MyAssembly", result.AssemblyName);
    }

    // ---- Suite summaries ----

    [Fact]
    public void Parse_SuiteSummaries_OrderedByFailuresDescending()
    {
        var doc = Xml("""
            <testsuites>
              <testsuite name="Healthy" time="1">
                <testcase classname="C" name="t1" time="1"/>
              </testsuite>
              <testsuite name="Failing" time="1">
                <testcase classname="C" name="t2" time="1">
                  <failure message="oops"/>
                </testcase>
              </testsuite>
            </testsuites>
            """);

        var result = TestResultParser.Parse(doc);

        Assert.Equal("Failing", result.SuiteSummaries[0].Name);
    }

    // ---- Error on empty ----

    [Fact]
    public void Parse_NoTestSuiteElements_Throws()
    {
        var doc = Xml("<results/>");

        Assert.Throws<InvalidOperationException>(() => TestResultParser.Parse(doc));
    }

    // ---- Multiple suites ----

    [Fact]
    public void Parse_MultipleSuites_AggregatesAllTestCases()
    {
        var doc = Xml("""
            <testsuites>
              <testsuite name="A" time="1">
                <testcase classname="A" name="t1" time="0.5"/>
              </testsuite>
              <testsuite name="B" time="1">
                <testcase classname="B" name="t2" time="0.5"/>
                <testcase classname="B" name="t3" time="0.5"/>
              </testsuite>
            </testsuites>
            """);

        var result = TestResultParser.Parse(doc);

        Assert.Equal(3, result.TestCases.Count);
    }
}
