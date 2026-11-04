## CI/CD and DevSecOps exercises

The primary purpose of this project is to practice **configuring and using Continuous Integration (CI) and automated software security tools**.

The application deliberately contains known security vulnerabilities. Students are informed about these vulnerabilities in advance because the objective is not vulnerability discovery, but learning how to configure tools that can detect and report security problems.

**The starter repository does not contain a GitHub Actions workflow or Dependabot configuration.** Students will create these during the exercises.

The exercises focus on:

1. **Automated builds:** Configure GitHub Actions to restore dependencies and build the solution.
2. **Automated testing:** Execute the existing xUnit tests as part of the CI pipeline.
3. **Dependency scanning:** Configure NuGet Audit and Dependabot to identify vulnerable third-party dependencies.
4. **Static security analysis:** Configure CodeQL to analyze the C# source code and report security findings.
5. **Verification:** Examine the results and confirm that the configured tools identify the expected vulnerabilities.

No cloud deployment or external infrastructure is required. The emphasis is on **Continuous Integration and automated security verification**, rather than Continuous Deployment.

## Known vulnerabilities and expected findings

The following vulnerabilities have been intentionally introduced and verified as part of the teaching material.

| Vulnerability | Component | Expected result |
|---|---|---|
| SQL Injection | Order search endpoint | CodeQL: `cs/sql-injection` |
| Path Traversal | Document retrieval endpoint | CodeQL: `cs/path-injection` |
| Information Disclosure | Exception handling | Demonstrated through manual HTTP testing; not reported by the tested CodeQL configuration |
| Vulnerable dependency | `Newtonsoft.Json` 12.0.1 | NuGet Audit: vulnerable dependency warning |

**Note:** Not every vulnerability is detectable by every security tool. Students should distinguish between a successfully executed security scan and a scan that actually reports the expected findings.

The intended learning outcome is to understand how automated security checks can be integrated into a software development workflow and how their results can be interpreted.