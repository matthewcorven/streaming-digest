# Convert to CPM

## Conversion overview

Scope: repository-root CPM adoption for the .NET projects under `src/` and `tests/`.

Converted projects:
- `src/StreamingDigest.Api`
- `src/StreamingDigest.AppHost`
- `src/StreamingDigest.Application`
- `src/StreamingDigest.Infrastructure`
- `src/StreamingDigest.Web`
- `src/StreamingDigest.Worker`
- `tests/StreamingDigest.IntegrationTests`
- `tests/StreamingDigest.UnitTests`
- `tests/StreamingDigest.Web.UnitTests`

The repo now uses `Directory.Packages.props` with `ManagePackageVersionsCentrally=true`.
Per-project `Version` attributes were removed from `PackageReference` items, while direct package references used for security remediation remain explicit and are now documented in both:
- `Directory.Packages.props` comments
- local `.csproj` comments at the corresponding `PackageReference` items

Centralized packages: 35.

## Version conflict resolutions

No in-solution package conflicts required `VersionOverride`.

One out-of-solution project, `tests/StreamingDigest.Web.UnitTests`, had older test-package versions than the rest of the repo. These were aligned to the repo-wide central versions during CPM adoption:
- `coverlet.collector`: `6.0.2` -> `6.0.4`
- `Microsoft.NET.Test.Sdk`: `17.12.0` -> `17.14.1`
- `xunit`: `2.9.2` -> `2.9.3`
- `xunit.runner.visualstudio`: `2.8.2` -> `3.1.4`

Validation for that project passed after alignment:
- `dotnet build tests/StreamingDigest.Web.UnitTests/StreamingDigest.Web.UnitTests.csproj`
- `dotnet test tests/StreamingDigest.Web.UnitTests/StreamingDigest.Web.UnitTests.csproj --no-build`
- `dotnet list tests/StreamingDigest.Web.UnitTests/StreamingDigest.Web.UnitTests.csproj package --vulnerable --include-transitive`

## Package comparison: baseline vs. result

For the 10 projects included in `StreamingDigest.slnx`, `baseline-packages.json` and `after-cpm-packages.json` showed no resolved-version drift after the CPM conversion itself. The resolved package graph remained stable.

Changes table:
- No resolved package changes in the solution-scoped baseline comparison.
- The out-of-solution `tests/StreamingDigest.Web.UnitTests` project was intentionally aligned to the repo-wide central test package versions listed above.

Unchanged table:
- All solution-scoped resolved package versions remained unchanged across the baseline and post-CPM snapshots.

## Risk assessment

[Low risk]

Reasoning:
- CPM adoption is version-neutral for the solution-scoped projects.
- The only intentional version changes were for the out-of-solution Web unit test project’s test infrastructure packages.
- Full build and test validation succeeded after conversion.
- Security remediation pins are explicitly documented for future maintenance.

Residual notes:
- Integration test logs still show the pre-existing Hangfire PostgreSQL initializer warning that falls back to in-memory Hangfire storage during test-host startup. This warning did not fail the suite and was not introduced by CPM.

## Follow-up items

1. If the repo later adds more .NET projects outside `StreamingDigest.slnx`, keep them under CPM by omitting per-project package versions and adding new package versions in `Directory.Packages.props`.
2. If the Hangfire startup warning in integration-host logs becomes a concern, investigate it separately from CPM and package-governance work.
3. If future security advisories require direct top-level transitive overrides, preserve the pattern used here: comment the `PackageVersion` pin in `Directory.Packages.props` and the corresponding explicit `PackageReference` in the owning `.csproj`.

## Artifacts and how to use them

Generated artifacts:
- `baseline.binlog`: baseline solution build before CPM conversion
- `after-cpm.binlog`: solution build after CPM conversion
- `baseline-packages.json`: baseline resolved package graph for `StreamingDigest.slnx`
- `after-cpm-packages.json`: post-conversion resolved package graph for `StreamingDigest.slnx`
- `convert-to-cpm.md`: this report

Recommended usage:
- Use the two `.binlog` files for restore/build troubleshooting if a future CPM edit regresses.
- Use the two JSON package snapshots to verify whether future package edits change resolved versions intentionally or unexpectedly.
- Use this report as the PR/change summary for the CPM adoption.
