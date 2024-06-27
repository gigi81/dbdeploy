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

    private static readonly GlobalSettings GlobalSettings = new();
    private readonly IDirectoryInfo _directory;
    private readonly ILogger<Strategy> _logger;

    public StrategyTests(ITestOutputHelper output)
    {
        _directory = CreateDatabaseDirectory(Database01);
        _logger = output.BuildLoggerFor<Strategy>();
    }

    [Fact]
    public async Task GetDeploySteps_WhenAllStepsAreDeployed_ShouldReturnEmptyCollection()
    {
        //arrange
        var steps = GetSteps(_directory);
        var sut = new Strategy(steps, GetMigrations(steps.Length), _logger);
            
        //act
        var deploySteps = await sut.GetDeploySteps(MainBranch).ToArrayAsync();
        
        //assert
        deploySteps.Length.Should().Be(0);
    }

    [Fact]
    public async Task GetDeploySteps_WhenOnlyInitStepIsDeployed_ShouldReturnTheStepsAfter()
    {
        //arrange
        var steps = GetSteps(_directory);
        var sut = new Strategy(steps, GetMigrations(1), _logger);
            
        //act
        var deploySteps = await sut.GetDeploySteps(MainBranch).ToArrayAsync();
        
        //assert
        deploySteps.Length.Should().Be(steps.Length - 1);
    }

    [Fact]
    public async Task GetDeploySteps_WhenDeployingRelease_ShouldDeployOneStep()
    {
        //arrange
        const string releaseBranch = "release/1.1";
        var releaseSteps = new[]
        {
            new Step(Database01, "TKT-001.SampleDescription", releaseBranch, false, _directory)
        };
        var steps = GetSteps(_directory).Concat(releaseSteps).ToArray();
        var sut = new Strategy(steps, GetMigrations(2), _logger);
            
        //act
        var deploySteps = await sut.GetDeploySteps(releaseBranch).ToArrayAsync();
        
        //assert
        deploySteps.Length.Should().Be(1);
        deploySteps.Should().BeEquivalentTo(releaseSteps);
    }
    
    private static Dictionary<string, DatabaseMigration[]> GetMigrations(int count)
    {
        return new Dictionary<string, DatabaseMigration[]>
        {
            { 
                Database01,
                new []
                {
                    new DatabaseMigration(GlobalSettings.InitStepName, "", ""),
                    new DatabaseMigration("TKT-001.SampleDescription", "", ""),
                    new DatabaseMigration("TKT-002.SampleDescription", "", "")
                }.Take(count).ToArray()
            }
        };
    }

    private static Step[] GetSteps(IDirectoryInfo databaseDirectory)
    {
        return
        [
            new Step(Database01, GlobalSettings.InitStepName, MainBranch, true, databaseDirectory),
            new Step(Database01, "TKT-001.SampleDescription", MainBranch, false, databaseDirectory)
        ];
    }

    private static IDirectoryInfo CreateDatabaseDirectory(string database)
    {
        var filesystem = new MockFileSystem();
        var directory = filesystem.Directory.CreateDirectory(database);
        var deploy01 = directory.File($@"{GlobalSettings.InitStepName}.sql");
        deploy01.WriteAllText("Step1 Deploy");
        var deploy02 = directory.File("TKT-001.SampleDescription.Deploy.sql");
        deploy02.WriteAllText("Step2 Deploy");
        var rollback02 = directory.File("TKT-001.SampleDescription.Rollback.sql");
        rollback02.WriteAllText("Step2 Rollback");
        return directory;
    }
}