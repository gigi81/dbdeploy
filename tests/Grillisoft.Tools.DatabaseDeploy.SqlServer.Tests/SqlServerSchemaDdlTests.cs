using System.IO.Abstractions.TestingHelpers;
using System.Text;
using AwesomeAssertions;
using DotNet.Testcontainers.Containers;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Tests;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests;

/// <summary>
/// End to end check of the schema DDL generation: build a database that uses the features a real
/// application database uses, script it, wipe the database and replay the script through the very
/// parser dbdeploy uses at deploy time. Whatever comes back has to match what was there before.
/// </summary>
public class SqlServerSchemaDdlTests : DatabaseTest<SqlServerDatabase, MsSqlContainer>
{
    private const string DatabaseName = "dbdeploy_ddl";

    private readonly ITestOutputHelper _output;

    public SqlServerSchemaDdlTests(ITestOutputHelper output)
        : base(new MsSqlBuilder().Build(), output)
    {
        _output = output;
    }

    protected override IDictionary<string, string?> GetConfigurationSettings()
    {
        var ret = base.GetConfigurationSettings();
        ret["databases:test:connectionString"] = this.TargetConnectionString;
        return ret;
    }

    protected override IDatabaseFactory CreateDatabaseFactory()
    {
        return new SqlServerDatabaseFactory(
            new SqlServerScriptParser(),
            this.GlobalSettingsOptions,
            this.LoggerFactory);
    }

    protected override string ProviderName => SqlServerDatabaseFactory.ProviderName;

    /// <summary>The container hands out a connection to master; the test needs one of its own.</summary>
    private string TargetConnectionString =>
        new SqlConnectionStringBuilder(this.ConnectionString) { InitialCatalog = DatabaseName }.ConnectionString;

    /// <summary>
    /// The container only ever has master, and scripting master is not a thing anybody wants.
    /// </summary>
    public async override Task InitializeAsync()
    {
        await base.InitializeAsync();

        var database = await this.CreateDatabase();
        if (!await database.Exists(CancellationToken.None))
            await database.Create(CancellationToken.None);
    }

    /// <summary>
    /// Everything here exists because it is the kind of thing a generator gets wrong: a schema that
    /// has to be created before anything in it, a user defined type and a table type used by a
    /// column and by a parameter, a computed column, a default reading from a sequence, a filtered
    /// index, an indexed view, two tables pointing at each other, a trigger, and literals holding
    /// quotes and batch separators.
    /// </summary>
    private static readonly string[] Schema =
    [
        """
        CREATE SCHEMA [app]
        """,
        """
        CREATE TYPE [app].[Code] FROM nvarchar(30) NOT NULL
        """,
        """
        CREATE TYPE [app].[IdList] AS TABLE ([Id] int NOT NULL PRIMARY KEY)
        """,
        """
        CREATE SEQUENCE [app].[OrderNumbers] AS bigint START WITH 1000 INCREMENT BY 1
        """,
        """
        CREATE TABLE [app].[Customer] (
          [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED,
          [Code] [app].[Code] NOT NULL,
          [Name] nvarchar(100) NOT NULL,
          [Email] nvarchar(200) NULL,
          [LastOrderId] int NULL,
          CONSTRAINT [UQ_Customer_Email] UNIQUE ([Email]),
          CONSTRAINT [CK_Customer_Name] CHECK (LEN([Name]) > 1)
        )
        """,
        """
        CREATE TABLE [app].[Orders] (
          [Id] int NOT NULL CONSTRAINT [PK_Orders] PRIMARY KEY,
          [CustomerId] int NOT NULL,
          [Quantity] int NOT NULL CONSTRAINT [DF_Orders_Quantity] DEFAULT (1),
          [UnitPrice] decimal(10,2) NOT NULL,
          [Total] AS ([Quantity] * [UnitPrice]) PERSISTED,
          [Note] nvarchar(400) NULL CONSTRAINT [DF_Orders_Note] DEFAULT (N'careful: it''s got a GO in it'),
          [Number] bigint NOT NULL CONSTRAINT [DF_Orders_Number] DEFAULT (NEXT VALUE FOR [app].[OrderNumbers]),
          CONSTRAINT [FK_Orders_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [app].[Customer]([Id])
        )
        """,
        // the other half of the cycle: the two tables point at each other
        """
        ALTER TABLE [app].[Customer]
          ADD CONSTRAINT [FK_Customer_LastOrder] FOREIGN KEY ([LastOrderId]) REFERENCES [app].[Orders]([Id])
        """,
        """
        CREATE NONCLUSTERED INDEX [IX_Orders_Customer] ON [app].[Orders] ([CustomerId]) INCLUDE ([Total])
        """,
        """
        CREATE UNIQUE NONCLUSTERED INDEX [UX_Orders_Number] ON [app].[Orders] ([Number]) WHERE [Number] > 0
        """,
        """
        CREATE FUNCTION [app].[fn_Tax] (@amount decimal(10,2)) RETURNS decimal(10,2)
        AS
        BEGIN
          -- don't remove this comment: the apostrophe is deliberate
          RETURN @amount * 0.22
        END
        """,
        """
        CREATE VIEW [app].[v_OrderTotals]
        AS
        SELECT o.[Id], o.[Total], [app].[fn_Tax](o.[Total]) AS [Tax] FROM [app].[Orders] o
        """,
        """
        CREATE VIEW [app].[v_OrdersPerCustomer] WITH SCHEMABINDING
        AS
        SELECT [CustomerId], COUNT_BIG(*) AS [Orders] FROM [app].[Orders] GROUP BY [CustomerId]
        """,
        """
        CREATE UNIQUE CLUSTERED INDEX [UX_v_OrdersPerCustomer] ON [app].[v_OrdersPerCustomer] ([CustomerId])
        """,
        """
        CREATE FUNCTION [app].[fn_CustomerOrders] (@id int) RETURNS TABLE
        AS
        RETURN (SELECT [Id], [Total] FROM [app].[Orders] WHERE [CustomerId] = @id)
        """,
        """
        CREATE PROCEDURE [app].[pr_AddOrders] @ids [app].[IdList] READONLY
        AS
        BEGIN
          SET NOCOUNT ON;
          SELECT [Id] FROM @ids;
        END
        """,
        """
        CREATE TRIGGER [app].[tr_Orders_Audit] ON [app].[Orders] AFTER INSERT
        AS
        BEGIN
          SET NOCOUNT ON;
        END
        """,
        """
        CREATE SYNONYM [app].[syn_Customer] FOR [app].[Customer]
        """,
        // an object outside the app schema, to prove dbo is never emitted as a CREATE SCHEMA
        """
        CREATE TABLE [dbo].[Lookup] ([Code] nvarchar(30) NOT NULL CONSTRAINT [PK_Lookup] PRIMARY KEY, [Label] nvarchar(100) NULL)
        """,
        """
        EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'The customer''s master data',
          @level0type = N'SCHEMA', @level0name = N'app', @level1type = N'TABLE', @level1name = N'Customer'
        """,
        """
        EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Full name; don''t truncate',
          @level0type = N'SCHEMA', @level0name = N'app', @level1type = N'TABLE', @level1name = N'Customer',
          @level2type = N'COLUMN', @level2name = N'Name'
        """,
    ];

    [Fact]
    [Trait(nameof(DockerPlatform), nameof(DockerPlatform.Linux))]
    public async Task GenerateSchemaDdl_ShouldProduceAScriptThatRebuildsTheDatabase()
    {
        // arrange
        var sut = await this.CreateDatabase();
        await DropEverything();

        // the migrations table and everything hanging off it must stay out of the script
        await sut.InitializeMigrations(CancellationToken.None);

        foreach (var statement in Schema)
            await sut.RunScript(statement, CancellationToken.None);

        var expectedObjects = await GetObjects();
        var expectedForeignKeys = await GetForeignKeys();
        var expectedColumns = await GetColumns();
        var expectedProperties = await GetExtendedProperties();

        expectedObjects.Should().NotBeEmpty();
        expectedForeignKeys.Should().NotBeEmpty();
        expectedProperties.Should().NotBeEmpty();

        // act
        var script = await GenerateScript(sut);
        _output.WriteLine(script);

        await DropEverything();
        (await GetObjects()).Should().BeEmpty("the database must be wiped before replaying the script");

        await Replay(sut, script);

        // assert
        (await GetObjects()).Should().BeEquivalentTo(expectedObjects);
        (await GetForeignKeys()).Should().BeEquivalentTo(expectedForeignKeys);
        (await GetColumns()).Should().BeEquivalentTo(expectedColumns);
        (await GetExtendedProperties()).Should().BeEquivalentTo(expectedProperties);

        // dbo always exists, and creating it again fails
        script.Should().NotContain("CREATE SCHEMA [dbo]");

        // nothing that belongs to dbdeploy itself may end up in the script
        script.Should().NotContain("__Migrations");

        // a script carrying the filegroups of the server it was taken from cannot be replayed
        // anywhere else
        script.Should().NotContain("ON [PRIMARY]");
    }

    private static async Task<string> GenerateScript(SqlServerDatabase database)
    {
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
        {
            await database.GenerateSchemaDdl(writer, CancellationToken.None);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Replays the script the way the deploy command does, batch by batch, through the SQL Server
    /// script parser.
    /// </summary>
    private async Task Replay(SqlServerDatabase database, string script)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/scripts/init.sql", new MockFileData(script));

        var parser = new SqlServerScriptParser();
        var executed = 0;

        await foreach (var statement in parser.Parse(fileSystem.FileInfo.New("/scripts/init.sql"), CancellationToken.None))
        {
            try
            {
                await database.RunScript(statement, CancellationToken.None);
                executed++;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Batch {executed + 1} of the generated script failed: {ex.Message}{Environment.NewLine}{statement}", ex);
            }
        }

        _output.WriteLine($"Replayed {executed} batches");
        executed.Should().BeGreaterThan(0);
    }

    private async Task<List<string>> Read(string sql)
    {
        await using var connection = new SqlConnection(this.TargetConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));

        return result;
    }

    private Task<List<string>> GetObjects() => Read("""
        SELECT (o.type_desc + ' ' + SCHEMA_NAME(o.schema_id) + '.' + o.name) COLLATE DATABASE_DEFAULT
        FROM sys.objects o
        WHERE o.is_ms_shipped = 0 AND o.name NOT LIKE '\_\_%' ESCAPE '\'
          AND o.parent_object_id NOT IN (SELECT object_id FROM sys.objects WHERE name LIKE '\_\_%' ESCAPE '\')
        UNION ALL
        SELECT ('SCHEMA ' + s.name) COLLATE DATABASE_DEFAULT FROM sys.schemas s
        WHERE s.name NOT IN ('dbo','guest','sys','INFORMATION_SCHEMA') AND s.schema_id < 16384
        UNION ALL
        SELECT ('TYPE ' + SCHEMA_NAME(t.schema_id) + '.' + t.name) COLLATE DATABASE_DEFAULT
        FROM sys.types t WHERE t.is_user_defined = 1
        UNION ALL
        SELECT ('INDEX ' + SCHEMA_NAME(o.schema_id) + '.' + o.name + '.' + i.name) COLLATE DATABASE_DEFAULT
        FROM sys.indexes i INNER JOIN sys.objects o ON o.object_id = i.object_id
        WHERE o.is_ms_shipped = 0 AND i.name IS NOT NULL AND o.name NOT LIKE '\_\_%' ESCAPE '\'
        ORDER BY 1
        """);

    private Task<List<string>> GetForeignKeys() => Read("""
        SELECT (fk.name + ' ' + OBJECT_SCHEMA_NAME(fk.parent_object_id) + '.' + OBJECT_NAME(fk.parent_object_id)
             + ' -> ' + OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + OBJECT_NAME(fk.referenced_object_id)) COLLATE DATABASE_DEFAULT
        FROM sys.foreign_keys fk
        ORDER BY 1
        """);

    /// <summary>
    /// Column by column, so a table that comes back with the right name but the wrong shape - a lost
    /// identity, a computed column turned into a plain one, a default that did not make it - is
    /// caught.
    /// </summary>
    private Task<List<string>> GetColumns() => Read("""
        SELECT (SCHEMA_NAME(o.schema_id) + '.' + o.name + '.' + c.name
             + ' ' + TYPE_NAME(c.user_type_id) + '(' + CAST(c.max_length AS varchar(10)) + ')'
             + CASE WHEN c.is_nullable = 1 THEN ' NULL' ELSE ' NOT NULL' END
             + CASE WHEN c.is_identity = 1 THEN ' IDENTITY' ELSE '' END
             + CASE WHEN c.is_computed = 1 THEN ' COMPUTED' ELSE '' END) COLLATE DATABASE_DEFAULT
             + ISNULL(' DEFAULT ' + d.definition, '')
        FROM sys.columns c
        INNER JOIN sys.objects o ON o.object_id = c.object_id
        LEFT JOIN sys.default_constraints d ON d.object_id = c.default_object_id
        WHERE o.is_ms_shipped = 0 AND o.type IN ('U','V') AND o.name NOT LIKE '\_\_%' ESCAPE '\'
        ORDER BY 1
        """);

    private Task<List<string>> GetExtendedProperties() => Read("""
        SELECT (p.name + ' ' + OBJECT_SCHEMA_NAME(p.major_id) + '.' + OBJECT_NAME(p.major_id)
             + ISNULL('.' + COL_NAME(p.major_id, NULLIF(p.minor_id, 0)), '')) COLLATE DATABASE_DEFAULT
             + ' = ' + CAST(p.value AS nvarchar(400))
        FROM sys.extended_properties p
        WHERE p.class = 1
        ORDER BY 1
        """);

    /// <summary>
    /// Wipes the database. Foreign keys go first because dropping in dependency order is exactly the
    /// problem this test is about, and here it does not need solving.
    /// </summary>
    private async Task DropEverything()
    {
        const string drop = """
            DECLARE @statements TABLE (seq int IDENTITY(1,1), rank int, sql nvarchar(max));

            INSERT INTO @statements (rank, sql)
            SELECT 0, N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
                     + N' DROP CONSTRAINT ' + QUOTENAME(fk.name)
            FROM sys.foreign_keys fk INNER JOIN sys.tables t ON t.object_id = fk.parent_object_id;

            INSERT INTO @statements (rank, sql)
            SELECT CASE o.type WHEN 'V' THEN 1 WHEN 'P' THEN 2 WHEN 'SN' THEN 4 WHEN 'U' THEN 5 WHEN 'SO' THEN 6 ELSE 3 END,
                   N'DROP ' + CASE o.type
                                WHEN 'V' THEN N'VIEW' WHEN 'P' THEN N'PROCEDURE'
                                WHEN 'SN' THEN N'SYNONYM' WHEN 'SO' THEN N'SEQUENCE'
                                WHEN 'U' THEN N'TABLE' ELSE N'FUNCTION' END
                 + N' ' + QUOTENAME(SCHEMA_NAME(o.schema_id)) + N'.' + QUOTENAME(o.name)
            FROM sys.objects o
            WHERE o.is_ms_shipped = 0 AND o.type IN ('V','P','SN','SO','FN','IF','TF','U');

            INSERT INTO @statements (rank, sql)
            SELECT 7, N'DROP TYPE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
            FROM sys.types t WHERE t.is_user_defined = 1;

            INSERT INTO @statements (rank, sql)
            SELECT 8, N'DROP SCHEMA ' + QUOTENAME(s.name)
            FROM sys.schemas s
            WHERE s.name NOT IN ('dbo','guest','sys','INFORMATION_SCHEMA') AND s.schema_id < 16384;

            DECLARE statements CURSOR LOCAL FAST_FORWARD FOR SELECT sql FROM @statements ORDER BY rank, seq;
            DECLARE @sql nvarchar(max);

            OPEN statements;
            FETCH NEXT FROM statements INTO @sql;

            WHILE @@FETCH_STATUS = 0
            BEGIN
              BEGIN TRY
                EXEC sys.sp_executesql @sql;
              END TRY
              BEGIN CATCH
              END CATCH
              FETCH NEXT FROM statements INTO @sql;
            END

            CLOSE statements;
            DEALLOCATE statements;
            """;

        await using var connection = new SqlConnection(this.TargetConnectionString);
        await connection.OpenAsync();

        // views can depend on views and functions on functions, so one pass is not always enough
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = drop;
            await command.ExecuteNonQueryAsync();
        }
    }
}
