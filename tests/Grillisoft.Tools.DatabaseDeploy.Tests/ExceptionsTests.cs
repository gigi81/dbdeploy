using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public class ExceptionsTests
{
    [Test]
    public void BranchNotFoundException_Message_IsCorrect()
    {
        // Arrange
        const string branchName = "test_branch";
        var exception = new BranchNotFoundException(branchName);

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Be($"Branch {branchName} not found");
        exception.BranchName.Should().Be(branchName);
    }

    [Test]
    public void CircularDependencyException_Message_IsCorrect()
    {
        // Arrange
        var names = new[] { "A", "B", "C" };
        var exception = new CircularDependencyException(names);

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Be($"Circular dependency detected: {string.Join(",", names)}");
    }

    [Test]
    public void CircularIncludeException_Message_IsCorrect()
    {
        // Arrange
        const string filename = "test_file.sql";
        var exception = new CircularIncludeException(filename);

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Be($"Circular include detected on file {filename}");
    }

    [Test]
    public void DatabaseConfigNotFoundException_Message_IsCorrect()
    {
        // Arrange
        const string databaseName = "test_db";
        var exception = new DatabaseConfigNotFoundException(databaseName);

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Be($"Database configuration for '{databaseName}' was not found.");
    }

    [Test]
    public void DatabaseProviderNotFoundException_Message_IsCorrect()
    {
        // Arrange
        const string providerName = "test_provider";
        const string databaseName = "test_db";
        var exception = new DatabaseProviderNotFoundException(providerName, databaseName);

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Be($"Could not find database factory of type '{providerName}' for database '{databaseName}'");
    }

    [Test]
    public void DatabasesNotFoundException_Message_IsCorrect()
    {
        // Arrange
        var missingDatabases = new[] { "db1", "db2" };
        var exception = new DatabasesNotFoundException(missingDatabases);

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Be($"Databases not found: {string.Join(", ", missingDatabases)}");
    }

    [Test]
    public void DbObjectNotFoundException_Message_IsCorrect()
    {
        // Arrange
        var dbObject = new DbObject("test_object", "test_type");
        var exception = new DbObjectNotFoundException(dbObject);

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Be($"Database Object {dbObject.Name} of type {dbObject.Type} not found");
    }

    [Test]
    public void InvalidBranchesConfigurationException_Message_IsCorrect()
    {
        // Arrange
        var errors = new[] { "error1", "error2" };
        var exception = new InvalidBranchesConfigurationException(errors);

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Be($"Invalid branches configuration: {string.Join(", ", errors)}");
        exception.Errors.Should().Equal(errors);
    }

    [Test]
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
        message.Should().Be($"Expected step {step.Name} on database {step.Database} but found {migration.Name}");
    }
}
