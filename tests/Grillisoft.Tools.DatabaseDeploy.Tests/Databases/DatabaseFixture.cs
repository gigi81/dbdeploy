using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Databases;

/// <summary>
/// The connection string of a database a test can run against.
/// </summary>
public interface IDatabaseFixture
{
    string ConnectionString { get; }
}

/// <summary>
/// Starts one Testcontainers database and keeps it up for as long as the fixture is shared.
/// </summary>
/// <remarks>
/// Under xUnit the container lived on the test class, which is built again for every test method, so
/// a provider paid for a container per test. A TUnit fixture is a data source: declared with
/// <c>[ClassDataSource&lt;T&gt;(Shared = SharedType.PerAssembly)]</c> the container is started once
/// for the whole test project and disposed when it has finished. Per assembly rather than per class
/// because starting a database is far and away the most expensive thing these tests do - an Oracle
/// one costs the best part of a minute - and the two classes a provider has would otherwise pay for
/// it twice.
/// </remarks>
public abstract class DatabaseFixture<TContainer> : IDatabaseFixture, IAsyncInitializer, IAsyncDisposable
    where TContainer : DockerContainer, IDatabaseContainer
{
    private readonly Lazy<TContainer> _container;

    protected DatabaseFixture()
    {
        _container = new Lazy<TContainer>(this.CreateContainer);
    }

    protected abstract TContainer CreateContainer();

    public TContainer Container => _container.Value;

    public virtual string ConnectionString => this.Container.GetConnectionString();

    public virtual Task InitializeAsync() => this.Container.StartAsync();

    public virtual ValueTask DisposeAsync() => this.Container.DisposeAsync();
}
