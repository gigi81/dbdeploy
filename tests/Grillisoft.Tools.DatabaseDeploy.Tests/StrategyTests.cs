using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public class StrategyTests
{
    private const string Database01 = "database";
    private const string MainBranch = "main";

    private readonly IDirectoryInfo _directory;
    private readonly ILogger<Strategy> _logger;
    private readonly Step[] _steps;

    public StrategyTests(ITestOutputHelper output)
    {
        _directory = CreateDatabaseDirectory(Database01);
        _steps = GetSteps(_directory);
        _logger = output.BuildLoggerFor<Strategy>();
    }

    [Fact]
    public async Task GetDeploySteps_WhenAllStepsAreDeployed_ShouldReturnEmptyCollection()
    {
        //arrange
        var sut = new Strategy(_steps, GetMigrations(_steps.Length), _logger);
            
        //act
        var deploySteps = await sut.GetDeploySteps(MainBranch).ToArrayAsync();
        
        //assert
        deploySteps.Length.Should().Be(0);
    }

    [Fact]
    public async Task GetDeploySteps_WhenOnlyInitStepIsDeployed_ShouldReturnTheStepsAfter()
    {
        //arrange
        var sut = new Strategy(_steps, GetMigrations(1), _logger);
            
        //act
        var deploySteps = await sut.GetDeploySteps(MainBranch).ToArrayAsync();
        
        //assert
        deploySteps.Length.Should().Be(_steps.Length - 1);
    }

    private static Dictionary<string, DatabaseMigration[]> GetMigrations(int count)
    {
        return new Dictionary<string, DatabaseMigration[]>
        {
            { 
                Database01,
                new []
                {
                    new DatabaseMigration(Step.InitStepName, DateTimeOffset.Now, "", ""),
                    new DatabaseMigration("TKT-001.SampleDescription", DateTimeOffset.Now, "", "")
                }.Take(count).ToArray()
            }
        };
    }

    private static Step[] GetSteps(IDirectoryInfo databaseDirectory)
    {
        return new[]
        {
            new Step(Database01, Step.InitStepName, MainBranch, databaseDirectory),
            new Step(Database01, "TKT-001.SampleDescription", MainBranch, databaseDirectory)
        };
    }

    private static IDirectoryInfo CreateDatabaseDirectory(string database)
    {
        var filesystem = new MockFileSystem();
        var directory = filesystem.Directory.CreateDirectory(database);
        var deploy01 = directory.File($@"{Step.InitStepName}.sql");
        deploy01.WriteAllText("Step1 Deploy");
        var deploy02 = directory.File("TKT-001.SampleDescription.Deploy.sql");
        deploy02.WriteAllText("Step2 Deploy");
        var rollback02 = directory.File("TKT-001.SampleDescription.Rollback.sql");
        rollback02.WriteAllText("Step2 Rollback");
        return directory;
    }
}