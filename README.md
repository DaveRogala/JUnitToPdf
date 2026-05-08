# JUnitToPdf

A .NET dotnet tool that converts JUnit XML test results into a styled PDF report.

## Features

- Summary box: total/passed/failed/skipped counts
- Per-suite breakdown table ordered by failures then duration
- Full test-results table grouped by class, with the five slowest tests highlighted
- Failures detail section with error messages (first 20)
- CI metadata footer (pipeline ID, branch, commit)
- Nickel City font (bundled) with Helvetica fallback

## Installation

```bash
dotnet tool install --global JUnitToPdf
```

Or from a local build:

```bash
dotnet pack JUnitToPdf/JUnitToPdf.csproj
dotnet tool install --global --add-source JUnitToPdf/nupkg JUnitToPdf
```

## Usage

```
junit-to-pdf [--input <path>] [--output <path>]

Options:
  --input  <path>   JUnit XML file to read  (default: artifacts/TestResults.xml)
                    Also reads JUNIT_XML env var.
  --output <path>   PDF file to write       (default: artifacts/TestReport.pdf)
                    Also reads TEST_REPORT_PDF env var.
  --help            Show this help message.
```

### Example

```bash
junit-to-pdf --input TestResults.xml --output report.pdf
```

A sample JUnit XML file is provided in `samples/TestResults.xml`.

## CI Integration

### GitLab CI

```yaml
test:
  script:
    - dotnet test --logger "junit;LogFilePath=artifacts/TestResults.xml"
  artifacts:
    paths:
      - artifacts/TestResults.xml

report:
  needs: [test]
  script:
    - junit-to-pdf
  artifacts:
    paths:
      - artifacts/TestReport.pdf
```

The footer automatically reads `CI_PIPELINE_ID`, `CI_COMMIT_REF_NAME`, and `CI_COMMIT_SHA`.

### GitHub Actions

```yaml
- name: Run tests
  run: dotnet test --logger "junit;LogFilePath=artifacts/TestResults.xml"

- name: Generate PDF report
  run: junit-to-pdf
  env:
    JUNIT_XML: artifacts/TestResults.xml
    TEST_REPORT_PDF: artifacts/TestReport.pdf

- name: Upload report
  uses: actions/upload-artifact@v4
  with:
    name: test-report
    path: artifacts/TestReport.pdf
```

The footer automatically reads `GITHUB_RUN_ID`, `GITHUB_REF_NAME`, and `GITHUB_SHA`.

## Environment Variables

| Variable            | GitLab equivalent      | Purpose                    |
|---------------------|------------------------|----------------------------|
| `JUNIT_XML`         | —                      | Input XML path             |
| `TEST_REPORT_PDF`   | —                      | Output PDF path            |
| `CI_PIPELINE_ID`    | `GITHUB_RUN_ID`        | Pipeline/run ID in footer  |
| `CI_COMMIT_REF_NAME`| `GITHUB_REF_NAME`      | Branch name in footer      |
| `CI_COMMIT_SHA`     | `GITHUB_SHA`           | Commit SHA in footer       |

GitLab variables take precedence; GitHub Actions variables are used as fallbacks.

## Building from source

```bash
git clone https://github.com/daverogala/junittopdf.git
cd junittopdf
dotnet build
dotnet test
```
