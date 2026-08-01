# AGENTS.md

Instructions for AI coding agents working on this repository.

## .NET Solution Architecture

The solution is a .NET tool for database migrations. It is composed of the following projects:

- **Grillisoft.Tools.DatabaseDeploy.Cli**: The command line interface for the tool. It is the entry point of the application.
- **Grillisoft.Tools.DatabaseDeploy**: The core project that contains the logic for deploying and rolling back database changes.
- **Grillisoft.Tools.DatabaseDeploy.Abstractions**: Contains the abstractions for the core project.
- **Grillisoft.Tools.DatabaseDeploy.Contracts**: Contains the data contracts for the tool.
- **Grillisoft.Tools.DatabaseDeploy.Database**: Contains the base classes for database providers.
- **Grillisoft.Tools.DatabaseDeploy.SqlServer**: The SQL Server database provider.
- **Grillisoft.Tools.DatabaseDeploy.MySql**: The MySQL database provider.
- **Grillisoft.Tools.DatabaseDeploy.PostgreSql**: The PostgreSQL database provider.
- **Grillisoft.Tools.DatabaseDeploy.Oracle**: The Oracle database provider.
- **Grillisoft.Tools.DatabaseDeploy.AI**: The AI provider for generating database migrations.

Package versions are managed centrally in `Directory.Packages.props`; a `PackageReference` in a
project must not carry a `Version` attribute. `src/Directory.Build.props` makes every project's
internals visible to its `.Tests` counterpart, so test projects do not need an `InternalsVisibleTo`
of their own.

## Editing Files

- Edit files directly with the file editing tools. Do not rewrite files through throwaway
  `python`, `sed` or `awk` scripts: those edits are invisible in the transcript, hard to review,
  and have mangled line endings in this repository before.
- Files in this repository are inconsistent about BOM and CRLF. Do not try to preserve either by
  hand. Run `dotnet format` once a change set is complete and let it normalise charset, line
  endings and style.

## Code Standards

The code standards are defined in the `.editorconfig` file. The main rules are:

- Use spaces for indentation.
- Use Allman style for braces.
- Sort `System.*` using directives alphabetically and place them before other usings.
- Use `var` over explicit type.
- Use file-scoped namespaces.
- Use expression-bodied members for properties and accessors.
- Use PascalCase for types and non-field members.
- Interfaces should start with `I`.

`SonarAnalyzer.CSharp` runs on every project under `src`. A clean build has zero Sonar warnings in
the files being changed; the pre-existing ones are S1075, S4790 and S6444.

## SQL Formatting

`dbdeploy format` has two modes. By default it walks the branch layout and re-lays-out the
`.Deploy.sql` and `.Rollback.sql` script of every step, skipping init steps. Given `--include`
globs it formats whatever they match instead, reading no branch files; the dialect then comes from
the nearest folder named after a configured database, else `--provider`, else
`global:defaultProvider`.

**Neither mode ever contacts a database, and neither may start doing so.** Everything comes off
disk. The dialect is resolved through `IDatabasesCollection.GetSqlFormatter`, which maps a database
name to its provider's `IDatabaseFactory.SqlFormatter` from configuration alone - no `IDatabase` is
built, so no connection string has to be valid or even present, and `dbsettings.json` is optional
for the verb (see `Program.cs`). Whether a step is already deployed therefore comes from the branch
files: a step whose `Step.Branch` is `global:defaultBranch` has been released, which is the closest
thing to "already deployed" that exists on disk. Its **deploy script is not formatted** unless
`--force` is passed, because that file's MD5 is the migration hash, and the skip is warned about
whether or not formatting would have changed anything; its rollback script is formatted normally.
`ISqlFormatter.Dialect` carries the provider name so every formatted file can be logged with the
dialect it was laid out with. Matching is
`Microsoft.Extensions.FileSystemGlobbing`, which supports only `*`, `**` and `?` - a character class
such as `[Ii]` silently matches nothing.

The shared machinery
lives in `Grillisoft.Tools.DatabaseDeploy.Database/Formatting` (`SqlLexer`, `SqlEmitter`,
`SqlFormatVerifier`, `SqlKeywords`, `SqlDialect`), and each provider contributes a `Formatting/`
folder with its own `SqlDialect` subclass and keyword sets - the same shared-plus-per-dialect shape
as the `Ddl/` folders. `.editorconfig` is read in `Grillisoft.Tools.DatabaseDeploy/Formatting`,
because the core project cannot reference the provider base.

Two invariants hold the design up, and both are covered by tests that must keep passing:

- **The lexer is lossless.** Concatenating every token's `Text` reproduces the input byte for byte.
  Any new token kind or dialect hook has to preserve this.
- **Every format is verified.** `SqlFormatVerifier` re-tokenises the output and compares the
  significant tokens against the source. Layout, keyword casing and comment indentation may differ;
  nothing else may. A file that fails verification is left untouched and the run reports a failure.

The `*CorpusTests` in each provider's test project run the `examples/**/*.sql` scripts through the
formatter and assert both verification and idempotency, via `CorpusFiles.AssertFormatsCleanly`. They
are the real regression net - prefer adding a case to `examples/` over hand-writing a fixture when
reproducing a layout bug. `CorpusFiles` skips anything over 2 MB, which drops the bulk
`_Init.Data*.sql` dumps only: they are never formatted by the product, and they cost four minutes of
CI time. Keep a new fixture under that size or it will be silently ignored.

## Pre and post scripts

`preDeploy`, `postDeploy`, `preRollback` and `postRollback` name an optional script per database,
set globally and overridable per database (`IDatabasesCollection.GetHooks`). Like
`GetSqlFormatter`, they resolve **from configuration alone**: no `IDatabase` is built to read them.
The merge is `OverrideWithAllowEmpty`, not `OverrideWith`: an empty name on a database is how it
opts out of a global hook, so only a key that is absent falls back to the global name.
The file is `<root>/<database>/<name>.sql` and falls back to `<root>/<name>.sql`; both candidate
paths must keep being added to the tracked set in `BranchesValidator.CheckFiles`, otherwise
configuring a hook turns its own file into an `Untracked file detected` error and every run fails.
`CheckHookFiles` then makes a configured but missing script an error for `deploy`, `rollback` and
`validate` alike. Running them is `DatabaseHooksRunner`'s job and only its job: `Run` stops at the
first failure (the pre scripts) and `TryRun` carries on and returns how many failed (the post
scripts). It is bound to the run it belongs to - root folder and dry run come from its constructor,
so a call is only the hook and the databases - and `DeployService`/`RollbackService` build one each
and return the post failure count as their exit code. Both it and
`BaseService` execute a file through `ScriptsRunner`, which is the one place that parses a script
into batches, runs them and logs them.

## Unit Test and Integration Test Strategy

The solution has a comprehensive test suite. The unit tests are located in the `tests` folder. The integration tests are located in the `.github/workflows/integration-tests.yml` file.

### Unit Tests

The unit tests are written using TUnit, which runs on Microsoft.Testing.Platform: `global.json`
selects that runner, every test project is an `Exe`, and the platform options are passed straight to
`dotnet test` (there are no VSTest loggers or collectors). For each source project, there is a
corresponding test project with the `.Tests` suffix. The tests are located in the following
projects:

- **Grillisoft.Tools.DatabaseDeploy.Tests**: Contains the unit tests for the core project.
- **Grillisoft.Tools.DatabaseDeploy.Database.Tests**: Contains the unit tests for the database providers.
- **Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests**: Contains the unit tests for the SQL Server database provider.
- **Grillisoft.Tools.DatabaseDeploy.MySql.Tests**: Contains the unit tests for the MySQL database provider.
- **Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests**: Contains the unit tests for the PostgreSQL database provider.
- **Grillisoft.Tools.DatabaseDeploy.Oracle.Tests**: Contains the unit tests for the Oracle database provider.

The unit tests are run on every push and pull request to the `main` and `feature/**` branches. The tests are run on Windows, Linux, and macOS.

Assertions use AwesomeAssertions (`Should()`), not `Assert.That`. `AwesomeAssertions`, `TUnit.Core`
and the TUnit assertion namespaces are global usings from `tests/Directory.Build.props`, so a test
file needs none of them.

A provider test class derives from `DatabaseTest<TDatabase>` and carries three attributes:
`[InheritsTests]` so the shared cases run for it, `[ClassDataSource<TFixture>(Shared =
SharedType.PerAssembly)]` to be handed its `DatabaseFixture<TContainer>`, and - from the base -
`[NotInParallel]`, because every case clears and re-creates the one migrations table of the one
container they all share. Starting a database is by far the most expensive thing these tests do, so
a test project starts exactly one: not one per test as under xUnit, and not one per class either.
That is why the sharing is `PerAssembly` - a provider has two such classes, and `PerClass` would
buy a second container for the `*SchemaDdlTests` one.

Tests that need a Docker engine are marked `[Category(TestCategories.Docker)]`. Windows and macOS
have no engine, so CI runs them with `--treenode-filter "/*/*/*/*[Category!=Docker]"`; Linux runs
everything unfiltered in one pass. Keep it to one `dotnet test` per platform: a filter matching
nothing in a project still writes that project's TRX, and `dorny/test-reporter` fails on a TRX with
no test in it.

### Integration Tests

The integration tests are run on every push and pull request to the `main` and `feature/**` branches. The tests are run on Linux. The integration tests use Docker to spin up databases for testing. The following databases are tested:

- SQL Server
- Oracle
- MariaDB
- PostgreSQL

The integration tests run the `dbdeploy` tool against the example databases located in the `examples` folder.

## Agent Instructions

- When running tests, only target the specific tests that were added or changed, to speed up the
  process. Microsoft.Testing.Platform filters by tree node, not by `--filter`: for example
  `dotnet test --treenode-filter "/*/*/MyNewTests/*"` (the path is
  `/assembly/namespace/class/test`), or run the test project's executable directly with the same
  option.
- Run `dotnet format` after finishing a change set, before reporting the work as done.
- Do not commit or push unless asked.
