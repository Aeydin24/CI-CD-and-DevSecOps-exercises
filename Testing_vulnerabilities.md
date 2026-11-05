# Security Testing Guide – Order Management API

## Purpose

This document describes how to test and verify the intentionally introduced security vulnerabilities in the Order Management API.

The application was developed for educational exercises in Continuous Integration (CI) and DevSecOps. It deliberately contains insecure code and a vulnerable third-party dependency.

The objectives of these tests are to:

1. Verify that the intended vulnerabilities exist.
2. Demonstrate how the vulnerabilities can be exploited in a controlled environment.
3. Document the expected and observed results.
4. Compare manual security testing with Static Application Security Testing (SAST) and dependency vulnerability scanning.

> ⚠️ **Important:** Perform these tests only against your own local instance of the educational application. The application is intentionally insecure and must not be exposed to the public internet.

---

## Prerequisites

- .NET 10 SDK
- PowerShell
- The Order Management API project
- CodeQL CLI for the static analysis test
- An internet connection for downloading dependencies and vulnerability information

The application uses an in-memory SQLite database. No external database server is required.

### Start the application

Open a terminal in the solution directory and run:

```powershell
dotnet run --project OrderManagement.Api
```

The application can be started using the configured `Testing` launch profile.

Example console output:

```text
Now listening on: http://localhost:5000
Hosting environment: Testing
```

The API is then available at:

http://localhost:5000

Swagger UI, when enabled, is available at:

http://localhost:5000/swagger

> The SQLite database is stored in memory. All test data is lost when the application stops, so test orders must be recreated after restarting the application.

If your application uses a different local port, adjust the URLs in the following examples accordingly.

---

# Test 1 – SQL Injection

## Purpose

To verify that user-controlled input can modify the logic of an SQL query.

**Endpoint:**

`GET /api/orders/search?customerName=...`

The vulnerability exists because user input is inserted directly into an SQL statement without parameterization.

## Step 1 – Create test data

Create an order for Alice:

```powershell
$body = @{
    customerName = "Alice"
    productName = "Keyboard"
    quantity = 2
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "http://localhost:5000/api/orders" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Create another order for Bob:

```powershell
$body = @{
    customerName = "Bob"
    productName = "Mouse"
    quantity = 1
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "http://localhost:5000/api/orders" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

Both requests should succeed and create orders with the status `Created`.

## Step 2 – Perform a normal search

Search for Alice:

```powershell
Invoke-RestMethod `
    -Uri "http://localhost:5000/api/orders/search?customerName=Alice"
```

**Expected result:**

Only Alice's order is returned.

## Step 3 – Perform an SQL Injection test

Send the following request:

```powershell
Invoke-RestMethod `
    -Uri ("http://localhost:5000/api/orders/search?customerName=" +
          [uri]::EscapeDataString("' OR 1=1 --"))
```

**Payload:**

```sql
' OR 1=1 --
```

The resulting SQL query becomes:

```sql
SELECT * FROM Orders
WHERE CustomerName = '' OR 1=1 --'
```

### Explanation

- `'` closes the original SQL string.
- `OR 1=1` introduces a condition that is always true.
- `--` comments out the remainder of the SQL statement.

As a result, the query no longer filters results by the intended customer name.

## Expected result

The API returns both Alice's and Bob's orders.

This demonstrates that user input can change the SQL query's logic.

### Verification

**Status: VERIFIED**

SQL Injection was successfully demonstrated using an HTTP request that returned both test orders.

The vulnerability was also identified by CodeQL during local static analysis.

### Secure implementation

Use parameterized queries rather than constructing SQL statements through string concatenation or unsafe interpolation.

For example:

```csharp
var results = await db.Orders
    .FromSqlInterpolated(
        $"SELECT * FROM Orders WHERE CustomerName = {customerName}")
    .ToListAsync();
```

Alternatively, use LINQ:

```csharp
var results = await db.Orders
    .Where(o => o.CustomerName == customerName)
    .ToListAsync();
```

Both approaches avoid directly inserting untrusted input into SQL syntax.

---

# Test 2 – Path Traversal

## Purpose

To determine whether a client can access files outside the directory that the API is intended to expose.

**Endpoint:**

`GET /api/orders/documents?fileName=...`

The application contains the following code:

```csharp
var path = Path.Combine(
    environment.ContentRootPath,
    "Documents",
    fileName);

var content = await System.IO.File.ReadAllTextAsync(path);
```

The problem is that the `fileName` parameter is controlled by the client and used directly to construct a filesystem path.

## Step 1 – Retrieve a legitimate document

Send the following request:

```powershell
Invoke-RestMethod `
    -Uri "http://localhost:5000/api/orders/documents?fileName=order-info.txt"
```

### Expected result

The API returns the contents of:

```text
OrderManagement.Api/Documents/order-info.txt
```

This demonstrates the endpoint's intended behavior.

## Step 2 – Attempt Path Traversal

We will now attempt to retrieve the application's `Program.cs` file, which is located outside the `Documents` directory.

```powershell
$fileName = [uri]::EscapeDataString("../Program.cs")

Invoke-RestMethod `
    -Uri "http://localhost:5000/api/orders/documents?fileName=$fileName"
```

**Payload:**

```text
../Program.cs
```

### Explanation

`..` refers to a parent directory in a filesystem path.

Consider the following directory structure:

```text
OrderManagement.Api/
├── Program.cs
└── Documents/
    └── order-info.txt
```

Combining the path `Documents` with `../Program.cs` can cause the resulting path to reference `Program.cs` in the application's root directory.

The API does not validate that the resolved path remains inside the intended document directory.

### Expected result

The API returns the contents of `Program.cs`.

This demonstrates that a client can request a file outside the intended directory.

### Verification

**Status: VERIFIED**

The application contains an exploitable path construction vulnerability, and the issue was identified during local CodeQL analysis using the `cs/path-injection` rule.

The manual test demonstrates the intended attack scenario using a path containing `../`.

### Secure implementation

A preferred solution is to restrict access to a predefined set of documents.

For example, the API could accept a document identifier and resolve it to an approved filename on the server.

If the client must provide a filename, the application should:

1. Resolve the resulting path to a canonical absolute path.
2. Verify that the resolved path remains inside the permitted directory.
3. Reject paths that escape the permitted directory.
4. Apply appropriate filesystem access permissions.

Using `Path.GetFileName()` may help for simple filename-only APIs, but an allowlist of permitted documents is often a more robust design.

---

# Test 3 – Information Disclosure

## Purpose

To determine whether the application exposes internal technical information through its error responses.

**Endpoint:**

`GET /api/orders/documents?fileName=...`

The application contains the following exception handling:

```csharp
catch (Exception ex)
{
    return StatusCode(500, ex.ToString());
}
```

The problem is that `ex.ToString()` can include:

- Stack traces
- Internal filesystem paths
- Class names
- Method names
- Source code line numbers
- Implementation details

This information should not normally be exposed to clients.

## Step 1 – Trigger an exception

Attempt to retrieve a document that does not exist.

Open PowerShell and execute:

```powershell
curl.exe -i "http://localhost:5000/api/orders/documents?fileName=does-not-exist.txt"
```

The `-i` option instructs curl to include the HTTP response headers.

This allows us to inspect both the response status and the response body, even when the server returns an error.

## Expected result

The server returns:

`HTTP 500 Internal Server Error`

The response body includes internal technical details about the exception.

**Example of the observed response (abbreviated):**

```text
HTTP/1.1 500 Internal Server Error
Content-Type: text/plain; charset=utf-8
Server: Kestrel

System.IO.FileNotFoundException:
Could not find file '<application-directory>\Documents\does-not-exist.txt'.

File name: '<application-directory>\Documents\does-not-exist.txt'

   at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(...)
   at System.IO.File.InternalReadAllTextAsync(...)
   at OrderManagement.Api.Controllers.OrdersController.GetDocument(String fileName)
      in <application-directory>\Controllers\OrdersController.cs:line 102
```

This is an abbreviated and generalized version of the response observed during testing.

The exact content depends on the operating system, .NET version, and filesystem location of the application.

## Step 2 – Examine the disclosed information

Inspect the HTTP response and identify the internal details it reveals.

| Information | Observed result |
|---|---|
| Exception type | `System.IO.FileNotFoundException` |
| Internal filesystem path | Application directory and document path |
| Controller | `OrdersController` |
| Method | `GetDocument()` |
| Source file | `OrdersController.cs` |
| Source line | `102` in the tested version |
| Stack trace | Returned to the client |

### Security impact

An attacker may use these details to gain insight into the application's internal structure and implementation.

The response can reveal:

- How the application's directories are organized.
- Which internal classes and methods are used.
- Where an exception occurs in the source code.
- Which .NET components participate in request processing.

Such information may help an attacker identify additional attack opportunities.

It is important to note that an HTTP `500` status code is not itself an Information Disclosure vulnerability.

The vulnerability is the exposure of internal details in the response body.

### Verification

**Status: VERIFIED**

The vulnerability was verified through an HTTP request to the document endpoint.

The test demonstrated that the application returns:

- HTTP `500 Internal Server Error`
- A detailed exception message
- Internal filesystem paths
- Class and method names
- A stack trace containing source file and line information

This confirms that the application exposes internal technical information to the client.

The vulnerability was **not reported by the CodeQL configuration used in the local static analysis**.

## Secure implementation

Internal exceptions should be logged on the server, while clients receive a generic error response.

For example:

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Error retrieving order document");

    return Problem(
        title: "An unexpected error occurred.",
        statusCode: 500);
}
```

This example assumes that an `ILogger` is available in the controller.

The approach allows developers to investigate exceptions through server-side logs without returning detailed error information to clients.

Expected errors should also use appropriate HTTP status codes.

For example, a document that does not exist should normally result in `404 Not Found` rather than `500 Internal Server Error`.

### Relationship to environment configuration

The application is configured to enable the Developer Exception Page only outside Production.

However, the vulnerability demonstrated here originates from the controller's own exception handling:

```csharp
return StatusCode(500, ex.ToString());
```

Consequently, detailed error information may also be exposed in Production, even when the Developer Exception Page is disabled.

This demonstrates why secure exception handling must be applied consistently throughout an application.

### Theoretical connections

The vulnerability relates to:

- *Secure by Design* – Secure failure handling
- *OWASP Code Review Guide v2* – Error handling
- *OWASP ASVS 5.0.0* – Secure error handling and prevention of information leakage

It illustrates how improper exception handling can compromise confidentiality, even when the original error was not caused by a malicious request.

---

# Test 4 – NuGet Vulnerability Scanning

## Purpose

To verify that the application includes a third-party dependency with a known security vulnerability.

The application deliberately includes:

```xml
<PackageReference Include="Newtonsoft.Json" Version="12.0.1" />
```

This version is affected by a known vulnerability associated with:

- CVE-2024-21907
- High severity
- Denial of Service (DoS)

The purpose is to demonstrate how known vulnerabilities in software dependencies can be identified automatically.

## Step 1 – Restore dependencies

Open PowerShell in the solution directory.

Run:

```powershell
dotnet restore -p:NuGetAudit=true -p:NuGetAuditMode=all
```

This restores the project's NuGet dependencies and enables auditing of direct and transitive dependencies.

In .NET 10, NuGet Audit is normally enabled by default.

Examine the output for vulnerability warnings.

NuGet uses the following warning codes:

| Warning | Severity |
|---|---|
| `NU1901` | Low |
| `NU1902` | Moderate |
| `NU1903` | High |
| `NU1904` | Critical |

## Step 2 – List vulnerable dependencies

Run:

```powershell
dotnet package list --vulnerable --include-transitive
```

This command lists dependencies associated with known vulnerabilities.

### Expected result

NuGet Audit identifies the vulnerable version of `Newtonsoft.Json`.

Review the reported advisory, including:

- Affected package and version
- Vulnerability severity
- Advisory reference
- Available fixed versions

The exact output depends on the vulnerability information available from the configured audit sources.

### Verification

**Status: VERIFIED**

The intentionally vulnerable dependency has been included and verified as part of the project's security testing.

The test demonstrates the use of NuGet Audit to identify known vulnerabilities in third-party packages.

### Secure implementation

Update the dependency to a version in which the reported vulnerability has been resolved.

After the update, run:

```powershell
dotnet restore -p:NuGetAudit=true -p:NuGetAuditMode=all

dotnet package list --vulnerable --include-transitive
```

Verify that the original vulnerability is no longer reported for the updated dependency.

Also execute the project's automated tests to check for compatibility issues introduced by the update.

---

# Test 5 – Static Security Analysis with CodeQL

## Purpose

To determine whether CodeQL can identify security vulnerabilities in the application's source code without executing the application itself.

CodeQL is a Static Application Security Testing (SAST) tool that analyzes source code for potentially unsafe code patterns and dangerous dataflows.

In this test, we use **CodeQL CLI** to perform the analysis locally on Windows.

This makes it possible to verify the intentionally introduced vulnerabilities without creating a GitHub Actions workflow.

We will investigate the following vulnerabilities:

| Vulnerability | CodeQL rule |
|---|---|
| SQL Injection | `cs/sql-injection` |
| Path Traversal | `cs/path-injection` |
| Information Disclosure | `cs/information-exposure-through-exception` |

A vulnerability can exist even if CodeQL does not identify it.

---

## Prerequisites

- Windows with PowerShell
- .NET 10 SDK
- The Order Management API solution
- CodeQL CLI
- CodeQL's C# query package

The application must compile successfully before creating the CodeQL database.

The Web API does not need to be running during this test.

---

## Step 1 – Install CodeQL CLI

Download CodeQL CLI from GitHub's official repository:

https://github.com/github/codeql-cli-binaries/releases

The version used during development of the exercise material was:

https://github.com/github/codeql-cli-binaries/releases/tag/v2.27.4

Download the Windows archive:

`codeql-win64.zip`

Extract the archive, for example to:

`C:\tools\codeql\`

Verify that the following executable exists:

`C:\tools\codeql\codeql.exe`

### Verify the installation

Open PowerShell and run:

```powershell
C:\tools\codeql\codeql.exe version
```

The command should display the installed CodeQL version.

### Note about CodeQL packages

The standalone CodeQL CLI does not necessarily include all query packages required for analyzing C#.

GitHub also provides a CodeQL bundle containing the CLI and compatible analysis packages.

This guide uses the standalone CLI together with the relevant C# query package.

---

## Step 2 – Install CodeQL's C# query package

First, verify that CodeQL supports C#:

```powershell
C:\tools\codeql\codeql.exe resolve languages
```

Verify that `csharp` is available.

Then download the official C# queries:

```powershell
C:\tools\codeql\codeql.exe pack download codeql/csharp-queries
```

This package contains queries used to identify different types of security problems in C# applications.

---

## Step 3 – Configure paths and project directory

To make the guide independent of the project's location on a particular computer, we will use PowerShell variables.

### Open the project

Navigate to the root directory of the Order Management solution.

This is the directory containing:

`OrderManagement.slnx`

For example:

```powershell
cd "<path-to-project>"
```

Replace `<path-to-project>` with the actual directory containing the solution file.

Verify that the solution builds:

```powershell
dotnet build
```

### Define the paths

Run:

```powershell
$codeql = "C:\tools\codeql\codeql.exe"
$projectPath = (Get-Location).Path
$databasePath = Join-Path $env:TEMP "order-codeql-db"
$reportPath = Join-Path $env:TEMP "order-codeql-results.sarif"
```

The variables have the following purposes:

| Variable | Purpose |
|---|---|
| `$codeql` | Location of the CodeQL executable |
| `$projectPath` | Root directory of the Order Management solution |
| `$databasePath` | Location of the CodeQL database |
| `$reportPath` | Location of the SARIF report |

If CodeQL is installed elsewhere, only the `$codeql` variable needs to be changed.

The project directory is determined automatically from the current working directory.

The CodeQL database and report are stored in the Windows temporary directory rather than inside the Git repository.

---

## Step 4 – Create a CodeQL database

Before executing security queries, CodeQL creates a database containing a structured representation of the source code.

The database is then used for static analysis.

### Create the database

```powershell
& $codeql database create `
    $databasePath `
    --language=csharp `
    --source-root="$projectPath" `
    --command="dotnet build /t:rebuild"
```

### Explanation

| Parameter | Purpose |
|---|---|
| `database create` | Creates a CodeQL database |
| `$databasePath` | Specifies the database location |
| `--language=csharp` | Selects C# |
| `--source-root` | Specifies the source code directory |
| `--command` | Specifies the build command |

The build command is:

```powershell
dotnet build /t:rebuild
```

This forces the solution to rebuild, allowing CodeQL to collect the information required for analysis.

### Expected result

CodeQL creates a database at the location specified by `$databasePath`.

> The database directory must not already exist. To repeat the analysis after modifying the source code, create a new database.

---

## Step 5 – Run the static security analysis

Once the database is ready, execute CodeQL's `security-extended` query suite.

This suite includes additional security queries beyond the default security query set.

### Run the analysis

```powershell
& $codeql database analyze `
    $databasePath `
    "codeql/csharp-queries:codeql-suites/csharp-security-extended.qls" `
    --format=sarif-latest `
    --output="$reportPath"
```

### Explanation

| Parameter | Purpose |
|---|---|
| `database analyze` | Executes the CodeQL analysis |
| `$databasePath` | Selects the database |
| `csharp-security-extended.qls` | Selects the extended security query suite |
| `--format=sarif-latest` | Specifies SARIF output |
| `--output` | Specifies the report file |

### Expected result

CodeQL creates a SARIF report at the location specified by `$reportPath`.

SARIF (*Static Analysis Results Interchange Format*) is a standardized JSON-based format for reporting static analysis results.

The report contains information about security findings identified by CodeQL.

---

## Step 6 – Examine the analysis results

We can inspect the SARIF report directly from PowerShell.

### Load the report

```powershell
$report = Get-Content $reportPath -Raw | ConvertFrom-Json
```

### Display the findings

```powershell
$report.runs.results | ForEach-Object {
    [PSCustomObject]@{
        Rule = $_.ruleId
        Message = $_.message.text
        File = $_.locations[0].physicalLocation.artifactLocation.uri
        Line = $_.locations[0].physicalLocation.region.startLine
    }
} | Format-Table -AutoSize -Wrap
```

The output shows:

- CodeQL rule identifier
- Description of the finding
- Affected source file
- Source code line number

If no findings are reported, the table will be empty.

### Alternative detailed view

For a more readable presentation, run:

```powershell
$report.runs.results | ForEach-Object {
    [PSCustomObject]@{
        Rule = $_.ruleId
        File = $_.locations[0].physicalLocation.artifactLocation.uri
        Line = $_.locations[0].physicalLocation.region.startLine
        Message = $_.message.text
    }
} | Format-List
```

---

## Step 7 – Examine the expected security findings

The local CodeQL analysis of the Order Management API identified two vulnerabilities.

### Finding 1 – SQL Injection

**CodeQL rule:**

`cs/sql-injection`

**File:**

`OrderManagement.Api/Controllers/OrdersController.cs`

**Reported message:**

`This query depends on this ASP.NET Core MVC action method parameter.`

The finding occurs because an HTTP request parameter is used directly to construct an SQL query.

Relevant code:

```csharp
var sql =
    $"SELECT * FROM Orders WHERE CustomerName = '{customerName}'";

var results = await db.Orders
    .FromSqlRaw(sql)
    .ToListAsync();
```

The user-controlled input can modify the SQL query's logic.

This vulnerability was also verified dynamically using an HTTP request containing an SQL Injection payload.

### Finding 2 – Path Traversal

**CodeQL rule:**

`cs/path-injection`

**File:**

`OrderManagement.Api/Controllers/OrdersController.cs`

**Reported message:**

`This path depends on a user-provided value.`

The finding occurs because an HTTP request parameter is used directly to construct a filesystem path.

Relevant code:

```csharp
var path = Path.Combine(
    environment.ContentRootPath,
    "Documents",
    fileName);

var content = await System.IO.File.ReadAllTextAsync(path);
```

If the client can influence the path using sequences such as `../`, the application may read files outside the intended document directory.

---

## Step 8 – Examine Information Disclosure

The Information Disclosure vulnerability was previously verified through an HTTP request that triggered an exception.

The application returned:

- Internal filesystem paths
- Class names
- Method names
- Source file and line number
- Stack trace information

The relevant code is:

```csharp
catch (Exception ex)
{
    return StatusCode(500, ex.ToString());
}
```

We also investigated whether CodeQL would identify this issue using the rule:

`cs/information-exposure-through-exception`

### Result

**Information Disclosure was not reported by the local CodeQL analysis.**

This does not mean that the vulnerability does not exist.

The application has already been shown to expose internal information through its HTTP response.

The result demonstrates an important limitation of automated static analysis.

A CodeQL analysis can complete successfully without identifying every vulnerability in an application.

Therefore, static analysis should be combined with other security testing methods and manual assessment.

---

## Step 9 – Repeat the analysis after code changes

If the source code changes, the CodeQL database must be recreated to analyze the updated version.

Make sure no analysis is currently using the database.

### Delete the existing database

```powershell
if (Test-Path $databasePath) {
    Remove-Item $databasePath -Recurse -Force
}
```

This removes the existing database at the configured location.

### Recreate the database

```powershell
& $codeql database create `
    $databasePath `
    --language=csharp `
    --source-root="$projectPath" `
    --command="dotnet build /t:rebuild"
```

### Run the analysis again

```powershell
if (Test-Path $reportPath) {
    Remove-Item $reportPath -Force
}

& $codeql database analyze `
    $databasePath `
    "codeql/csharp-queries:codeql-suites/csharp-security-extended.qls" `
    --format=sarif-latest `
    --output="$reportPath"
```

Load the updated report:

```powershell
$report = Get-Content $reportPath -Raw | ConvertFrom-Json
```

Examine whether the previously reported vulnerabilities are still present.

After implementing security fixes, this process can be used to verify whether the original findings are no longer reported.

A finding disappearing from the report does not, by itself, prove that the underlying vulnerability has been eliminated. The correction should also be reviewed and tested.

---

## Verification

**Status: VERIFIED – LOCAL CODEQL ANALYSIS**

The following results were observed:

| Vulnerability | CodeQL rule | Result |
|---|---|---|
| SQL Injection | `cs/sql-injection` | Identified |
| Path Traversal | `cs/path-injection` | Identified |
| Information Disclosure | `cs/information-exposure-through-exception` | Not reported |

The two identified findings were reported in:

`OrderManagement.Api/Controllers/OrdersController.cs`

The analysis was performed locally using CodeQL CLI and the `security-extended` query suite.

### What has been verified?

- CodeQL successfully created a database for the C# solution.
- The static analysis completed.
- A SARIF report was generated.
- SQL Injection was identified.
- Path Traversal was identified.
- Information Disclosure was not reported by the selected configuration.

### What remains to be verified?

- Whether SQL Injection and Path Traversal findings disappear after the corresponding vulnerabilities are corrected.
- Whether GitHub Actions reports the same findings when CodeQL is integrated into the CI pipeline.

---

## Key learning points

This test demonstrates the difference between running a security analysis and verifying that an application is secure.

CodeQL can identify potential security problems by analyzing dataflows from untrusted input sources to security-sensitive operations.

However, a successful analysis does not mean that every vulnerability has been identified.

In our application:

1. SQL Injection was identified by CodeQL.
2. Path Traversal was identified by CodeQL.
3. Information Disclosure was demonstrated dynamically but was not reported by CodeQL.

This demonstrates the importance of combining:

- Static Application Security Testing (SAST)
- Dynamic security testing
- Unit testing
- Manual code review
- Risk-based security assessment

This approach supports the principle of **defense in depth**.

### References

- GitHub CodeQL CLI: https://github.com/github/codeql-cli-binaries/releases
- GitHub CodeQL documentation: https://docs.github.com/en/code-security/codeql-cli
- CodeQL C# Query Help: https://codeql.github.com/codeql-query-help/csharp/
- *Secure by Design*, Chapter 8 – Leveraging your delivery pipeline for security

---

# Summary of Security Tests

| Vulnerability or finding | Testing method | Status |
|---|---|---|
| SQL Injection | HTTP request with an SQL Injection payload | Verified |
| Path Traversal | HTTP request containing `../Program.cs` | Verified |
| Information Disclosure | HTTP request triggering an exception | Verified |
| Vulnerable NuGet dependency | NuGet Audit | Verified |
| CodeQL – SQL Injection | Static code analysis | Identified |
| CodeQL – Path Traversal | Static code analysis | Identified |
| CodeQL – Information Disclosure | Static code analysis | Not reported |

**Note:** "Not reported" does not mean "not vulnerable." It only describes the result of the selected CodeQL analysis.

---

# Final Discussion

The different testing approaches examine different aspects of application security.

## Dynamic security testing

- Tests the running application.
- Can demonstrate the actual behavior of a vulnerable endpoint.
- Requires relevant inputs and attack paths to be exercised.

## Static Application Security Testing (SAST)

- Analyzes source code without running the application.
- Can identify unsafe code patterns and potentially dangerous dataflows.
- May produce false positives or fail to identify existing vulnerabilities.

## Software Composition Analysis (SCA)

- Examines third-party dependencies used by the application.
- Identifies dependencies associated with known security advisories.
- Does not necessarily establish whether a particular vulnerability is exploitable in the application.

## Conclusion

A successful build and five passing unit tests do not mean that an application is secure.

Likewise, a successful CodeQL analysis does not guarantee the absence of security vulnerabilities.

Secure software development therefore requires a combination of automated testing, security analysis, manual review, and risk-based decision-making.

This supports the principle of **defense in depth** and the integration of security activities throughout the software development lifecycle.