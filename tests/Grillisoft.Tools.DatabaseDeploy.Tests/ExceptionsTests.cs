
using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public class ExceptionsTests
{
    [Fact]
    public void BranchNotFoundException_Message_IsCorrect()
    {
        // Arrange
        const string branchName = "test_branch";
        var exception = new BranchNotFoundException(branchName);

        // Act
        var message = exception.Message;

        // Assert
        Assert.Equal($"Branch {branchName} not found", message);
        Assert.Equal(branchName, exception.BranchName);
    }

    [Fact]
    public void CircularDependencyException_Message_IsCorrect()
    {
        // Arrange
        var names = new[] { "A", "B", "C" };
        var exception = new CircularDependencyException(names);

        // Act
        var message = exception.Message;

        // Assert
        Assert.Equal($"Circular dependency detected: {string.Join(",", names)}", message);
    }

    [Fact]
    public void CircularIncludeException_Message_IsCorrect()
    {
        // Arrange
        const string filename = "test_file.sql";
        var exception = new CircularIncludeException(filename);

        // Act
        var message = exception.Message;

        // Assert
        Assert.Equal($"Circular include detected on file {filename}", message);
    }

    [Fact]
    public void DatabaseConfigNotFoundException_Message_IsCorrect()
    {
        // Arrange
        const string databaseName = "test_db";
        var exception = new DatabaseConfigNotFoundException(databaseName);

        // Act
        var message = exception.Message;

        // Assert
        Assert.Equal($"Database configuration for '{databaseName}' was not found.", message);
    }

    [Fact]
    public void DatabaseProviderNotFoundException_Message_IsCorrect()
    {
        // Arrange
        const string providerName = "test_provider";
        const string databaseName = "test_db";
        var exception = new DatabaseProviderNotFoundException(providerName, databaseName);

        // Act
        var message = exception.Message;

        // Assert
        Assert.Equal($"Could not find database factory of type '{providerName}' for database '{databaseName}'", message);
    }

    [Fact]
    public void DatabasesNotFoundException_Message_IsCorrect()
    {
        // Arrange
        var missingDatabases = new[] { "db1", "db2" };
        var exception = new DatabasesNotFoundException(missingDatabases);

        // Act
        var message = exception.Message;

        // Assert
        Assert.Equal($"Databases not found: {string.Join(", ", missingDatabases)}", message);
    }

    [Fact]
    public void DbObjectNotFoundException_Message_IsCorrect()
    {
        // Arrange
        var dbObject = new DbObject("test_object", "test_type");
        var exception = new DbObjectNotFoundException(dbObject);

        // Act
        var message = exception.Message;

        // Assert
        Assert.Equal($"Database Object {dbObject.Name} of type {dbObject.Type} not found", message);
    }

    [Fact]
    public void InvalidBranchesConfigurationException_Message_IsCorrect()
    {
        // Arrange
        var errors = new[] { "error1", "error2" };
        var exception = new InvalidBranchesConfigurationException(errors);

        // Act
        var message = exception.Message;

        // Assert
        Assert.Equal($"Invalid branches configuration: {string.Join(", ", errors)}", message);
        Assert.Equal(errors, exception.Errors);
    }

    [Fact]
    public void StepMigrationMismatchException_Message_IsCorrect()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        var step = new Step("db_name", "step_name", "main", false, fileSystem.DirectoryInfo.New("."));
        var migration = new DatabaseMigration("migration_name", "test_user", "12345678901234567890123456789012");
        var exception = new StepMigrationMismatchException(step, migration);

        // Act
        var message = exception.Message;

        // Assert
        Assert.Equal($"Expected step {step.Name} on database {step.Database} but found {migration.Name}", message);
    }
}
