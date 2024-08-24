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

public class DeployServiceTests
{
    private readonly ITestOutputHelper _output;
    private readonly GlobalSettings _globalSettings = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;

    public DeployServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _cancellationToken = _cancellationTokenSource.Token;
    }

    [Fact]
    public async Task Execute_WhenDeployingMainBranch_IsSuccessful()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath
        };
        var sut = CreateService(deployOptions, database01, database02);

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

    [Fact]
    public async Task Execute_WhenDeployingRelease1_1Branch_IsSuccessful()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Branch = "release/1.1"
        };
        var sut = CreateService(deployOptions, database01, database02);

        //act
        await sut.Execute(_cancellationToken);

        //assert
        var migrations01 = await database01.GetMigrations(_cancellationToken);
        var migrations02 = await database02.GetMigrations(_cancellationToken);

        migrations01.Count.Should().Be(2);
        migrations02.Count.Should().Be(1);
        migrations01.First().Name.Should().Be(_globalSettings.InitStepName);
        migrations01.Skip(1).First().Name.Should().Be("TKT-001.SampleDescription");
        migrations02.First().Name.Should().Be(_globalSettings.InitStepName);
    }

    [Fact]
    public async Task Execute_WhenDeployingRelease1_2Branch_IsSuccessful()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Branch = "release/1.2"
        };
        var sut = CreateService(deployOptions, database01, database02);

        //act
        await sut.Execute(_cancellationToken);

        //assert
        var migrations01 = await database01.GetMigrations(_cancellationToken);
        var migrations02 = await database02.GetMigrations(_cancellationToken);

        migrations01.Count.Should().Be(2);
        migrations01.First().Name.Should().Be(_globalSettings.InitStepName);
        migrations01.Skip(1).First().Name.Should().Be("TKT-001.SampleDescription");

        migrations02.Count.Should().Be(2);
        migrations02.First().Name.Should().Be(_globalSettings.InitStepName);
        migrations02.Skip(1).First().Name.Should().Be("TKT-002.SampleDescription");
    }

    private DeployService CreateService(DeployOptions deployOptions, params IDatabase[] databases)
    {
        var provider = new TestServiceCollection<DeployService>(_output)
            .AddSingleton(deployOptions)
            .AddSingleton<IFileSystem>(SampleFilesystems.Sample01)
            .AddSingleton<IProgress<int>>(new Progress<int>())
            .AddSingleton<IDatabaseFactory>(new DatabaseFactoryMock(databases))
            .AddSingleton<IDatabasesCollection>(new DatabasesCollectionMock(databases))
            .Configure<GlobalSettings>(options => { })
            .BuildServiceProvider();

        return provider.GetRequiredService<DeployService>();
    }
}