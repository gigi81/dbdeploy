using FluentAssertions;
using Testcontainers.MsSql;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests;

public class SqlServerDatabaseTests : IAsyncLifetime
{
    private readonly MsSqlContainer _msSqlContainer = new MsSqlBuilder().Build();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;

    public SqlServerDatabaseTests()
    {
        _cancellationToken = _cancellationTokenSource.Token;
    }
    
    [Fact (Skip = "CI fails decause of docker setup")]
    public async Task InitializeMigrations_Then_GetMigrations_ShouldBeEmpty()
    {
        //arrange
        var sut = new SqlServerDatabase("test", _msSqlContainer.GetConnectionString(), "__Migrations", new SqlServerScriptParser());

        //act
        await sut.InitializeMigrations(_cancellationToken);
        var migrations = await sut.GetMigrations(_cancellationToken);

        //assert
        migrations.Count.Should().Be(0);
    }

    public Task InitializeAsync() => _msSqlContainer.StartAsync();

    public Task DisposeAsync() => _msSqlContainer.DisposeAsync().AsTask();    
}