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

## Unit Test and Integration Test Strategy

The solution has a comprehensive test suite. The unit tests are located in the `tests` folder. The integration tests are located in the `.github/workflows/integration-tests.yml` file.

### Unit Tests

The unit tests are written using xUnit. For each source project, there is a corresponding test project with the `.Tests` suffix. The tests are located in the following projects:

- **Grillisoft.Tools.DatabaseDeploy.Tests**: Contains the unit tests for the core project.
- **Grillisoft.Tools.DatabaseDeploy.Database.Tests**: Contains the unit tests for the database providers.
- **Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests**: Contains the unit tests for the SQL Server database provider.
- **Grillisoft.Tools.DatabaseDeploy.MySql.Tests**: Contains the unit tests for the MySQL database provider.
- **Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests**: Contains the unit tests for the PostgreSQL database provider.
- **Grillisoft.Tools.DatabaseDeploy.Oracle.Tests**: Contains the unit tests for the Oracle database provider.

The unit tests are run on every push and pull request to the `main` and `feature/**` branches. The tests are run on Windows, Linux, and macOS.

### Integration Tests

The integration tests are run on every push and pull request to the `main` and `feature/**` branches. The tests are run on Linux. The integration tests use Docker to spin up databases for testing. The following databases are tested:

- SQL Server
- Oracle
- MariaDB
- PostgreSQL

The integration tests run the `dbdeploy` tool against the example databases located in the `examples` folder.

## Gemini Instructions

- When running tests, only target the specific tests that were added or changed by using the `--filter` option with `dotnet test` to speed up the process. For example: `dotnet test --filter "FullyQualifiedName~MyNewTests"`.