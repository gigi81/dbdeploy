using System.IO.Abstractions;
using FluentAssertions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Grillisoft.Tools.DatabaseDeploy.Services;
using Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public class RollbackServiceTests
{
    private readonly ITestOutputHelper _output;
    private readonly GlobalSettings _globalSettings = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;

    public RollbackServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _cancellationToken = _cancellationTokenSource.Token;
    }
    
    [Fact]
    public async Task Execute_WhenRollingBackRelease1_1_IsSuccessful()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var migration0101 = new DatabaseMigration(_globalSettings.InitStepName,"", "");
        var migration0102 = new DatabaseMigration("TKT-001.SampleDescription", "", "");
        await database01.AddMigration(migration0101, _cancellationToken);
        await database01.AddMigration(migration0102, _cancellationToken);
        
        var database02 = new DatabaseMock("Database02");
        var migration0201 = new DatabaseMigration(_globalSettings.InitStepName, "", "");
        await database02.AddMigration(migration0201, _cancellationToken);
        var rollbackOptions = new RollbackOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Branch = "release/1.1"
        };
        var sut = CreateService(rollbackOptions, database01, database02);

        //act
        await sut.Execute(_cancellationToken);

        //assert
        var migrations01 = await database01.GetMigrations(_cancellationToken);
        var migrations02 = await database02.GetMigrations(_cancellationToken);

        migrations01.Count.Should().Be(1);
        migrations02.Count.Should().Be(1);
        migrations01.First().Name.Should().Be(_globalSettings.InitStepName);
        migrations02.First().Name.Should().Be(_globalSettings.InitStepName);
    }
    
    private RollbackService CreateService(RollbackOptions deployOptions, params IDatabase[] databases)
    {
        var provider = new TestServiceCollection<RollbackService>(_output)
            .AddSingleton(deployOptions)
            .AddSingleton<IFileSystem>(SampleFilesystems.Sample01)
            .AddSingleton<IProgress<int>>(new Progress<int>())
            .AddSingleton<IDatabaseFactory>(new DatabaseFactoryMock(databases))
            .AddSingleton<IDatabasesCollection>(new DatabasesCollectionMock(databases))
            .Configure<GlobalSettings>(options => {})
            .BuildServiceProvider();

        return provider.GetRequiredService<RollbackService>();
    }
}