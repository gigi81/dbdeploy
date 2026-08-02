using Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests.Ddl;

/// <summary>
/// What <c>SHOW CREATE</c> hands back describes the object where it stands. These are the four
/// things that have to come off before it describes an object anywhere else.
/// </summary>
public class MySqlDdlRewriterTests
{
    /// <summary>
    /// Line endings with the carriage returns taken out.
    /// </summary>
    /// <remarks>
    /// The fixtures below are raw string literals, so their line endings are whatever this file
    /// happens to be saved with - and this repository is not consistent about that. Comparing
    /// normalised means these cases are about the commas and the constraints rather than about
    /// which machine last ran <c>dotnet format</c>;
    /// <see cref="SplitForeignKeys_ShouldNotChangeTheLineEndings"/> is where the line endings
    /// themselves are pinned.
    /// </remarks>
    private static string Lf(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>A real <c>SHOW CREATE TABLE</c> body, line breaks and all.</summary>
    private const string OrdersTable = """
        CREATE TABLE `orders` (
          `id` int NOT NULL AUTO_INCREMENT,
          `customer_id` int NOT NULL,
          `total` decimal(10,2) DEFAULT '0.00',
          PRIMARY KEY (`id`),
          KEY `ix_orders_customer` (`customer_id`),
          CONSTRAINT `fk_orders_customer` FOREIGN KEY (`customer_id`) REFERENCES `customer` (`id`)
        ) ENGINE=InnoDB AUTO_INCREMENT=42 DEFAULT CHARSET=utf8mb4
        """;

    [Test]
    [Arguments("CREATE DEFINER=`root`@`localhost` PROCEDURE `p`()")]
    [Arguments("CREATE DEFINER=`root`@`%` PROCEDURE `p`()")]
    [Arguments("CREATE DEFINER='root'@'localhost' PROCEDURE `p`()")]
    [Arguments("CREATE DEFINER=root@localhost PROCEDURE `p`()")]
    [Arguments("CREATE DEFINER = `root`@`localhost` PROCEDURE `p`()")]
    [Arguments("CREATE DEFINER=CURRENT_USER PROCEDURE `p`()")]
    public void StripDefiner_ShouldRemoveEverySpellingOfTheClause(string ddl)
    {
        MySqlDdlRewriter.StripDefiner(ddl)
            .Should().Be("CREATE PROCEDURE `p`()");
    }

    /// <summary>
    /// A view or routine with no <c>DEFINER=</c> defaults to whoever replays the script, which is
    /// what makes it portable. Rewriting the security clause would change what it does.
    /// </summary>
    [Test]
    public void StripDefiner_ShouldLeaveTheSecurityClauseAlone()
    {
        const string ddl = "CREATE ALGORITHM=UNDEFINED DEFINER=`root`@`localhost` SQL SECURITY DEFINER VIEW `v` AS SELECT 1";

        MySqlDdlRewriter.StripDefiner(ddl)
            .Should().Be("CREATE ALGORITHM=UNDEFINED SQL SECURITY DEFINER VIEW `v` AS SELECT 1");
    }

    /// <summary>
    /// The counter is data, not schema: leaving it in makes the generated file differ after every
    /// insert, and replaying it puts the new table's identity somewhere arbitrary.
    /// </summary>
    [Test]
    public void StripAutoIncrement_ShouldRemoveTheTableOptionButKeepTheColumnKeyword()
    {
        var stripped = MySqlDdlRewriter.StripAutoIncrement(OrdersTable);

        stripped.Should().NotContain("AUTO_INCREMENT=")
                .And.Contain("`id` int NOT NULL AUTO_INCREMENT")
                .And.Contain(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    }

    /// <summary>
    /// MySQL stores a view's definition fully qualified, so without this a script taken from one
    /// database points at that database forever.
    /// </summary>
    [Test]
    public void RemoveDatabaseQualifier_ShouldUnqualifyQuotedAndBareNames()
    {
        const string ddl = "CREATE VIEW `v` AS select `northwind`.`o`.`id` from `northwind`.`o` join northwind.`c`";

        MySqlDdlRewriter.RemoveDatabaseQualifier(ddl, "northwind")
            .Should().Be("CREATE VIEW `v` AS select `o`.`id` from `o` join `c`");
    }

    [Test]
    public void SplitForeignKeys_ShouldMoveTheConstraintIntoAnAlterTable()
    {
        // Act
        var (table, foreignKeys) = MySqlDdlRewriter.SplitForeignKeys(OrdersTable, "orders");

        // Assert
        table.Should().NotContain("CONSTRAINT `fk_orders_customer`")
             .And.Contain("KEY `ix_orders_customer` (`customer_id`)", "the backing index is reused by the ALTER");

        foreignKeys.Should().ContainSingle();
        foreignKeys[0].Name.Should().Be("fk_orders_customer");
        foreignKeys[0].Statement.Should().Be(
            "ALTER TABLE `orders` ADD CONSTRAINT `fk_orders_customer` FOREIGN KEY (`customer_id`) REFERENCES `customer` (`id`)");
    }

    /// <summary>
    /// The removed line took a comma with it - the one on the definition before it. Left in place
    /// the statement no longer parses.
    /// </summary>
    [Test]
    public void SplitForeignKeys_ShouldRepairTheTrailingComma()
    {
        var (table, _) = MySqlDdlRewriter.SplitForeignKeys(OrdersTable, "orders");

        Lf(table).Should().Contain("  KEY `ix_orders_customer` (`customer_id`)\n)");
    }

    /// <summary>
    /// The repaired line has to keep whatever ending it arrived with. Cutting the carriage return
    /// off along with the comma leaves one line of the statement broken differently from the rest,
    /// which is a miserable thing to find in a diff of a generated file.
    /// </summary>
    [Test]
    public void SplitForeignKeys_ShouldNotChangeTheLineEndings()
    {
        var crlf = OrdersTable.Replace("\r\n", "\n", StringComparison.Ordinal)
                              .Replace("\n", "\r\n", StringComparison.Ordinal);

        var (table, _) = MySqlDdlRewriter.SplitForeignKeys(crlf, "orders");

        table.Should().Contain("`customer_id`)\r\n)")
             .And.NotContain("\n\n");

        table.Replace("\r\n", string.Empty, StringComparison.Ordinal)
             .Should().NotContain("\n", "every line ending has to still be a pair");
    }

    /// <summary>
    /// The awkward case: the key was the only thing after the last column, so the comma to repair
    /// is on a column line rather than on another index.
    /// </summary>
    [Test]
    public void SplitForeignKeys_WhenTheKeyWasTheOnlyExtraDefinition_ShouldStillRepairTheComma()
    {
        const string ddl = """
            CREATE TABLE `t` (
              `id` int NOT NULL,
              CONSTRAINT `fk_t` FOREIGN KEY (`id`) REFERENCES `o` (`id`)
            ) ENGINE=InnoDB
            """;

        var (table, foreignKeys) = MySqlDdlRewriter.SplitForeignKeys(ddl, "t");

        Lf(table).Should().Be(Lf("""
            CREATE TABLE `t` (
              `id` int NOT NULL
            ) ENGINE=InnoDB
            """));

        foreignKeys.Should().ContainSingle();
    }

    [Test]
    public void SplitForeignKeys_ShouldTakeEveryKeyOut()
    {
        const string ddl = """
            CREATE TABLE `t` (
              `a` int NOT NULL,
              `b` int NOT NULL,
              CONSTRAINT `fk_a` FOREIGN KEY (`a`) REFERENCES `x` (`id`),
              CONSTRAINT `fk_b` FOREIGN KEY (`b`) REFERENCES `y` (`id`)
            ) ENGINE=InnoDB
            """;

        var (table, foreignKeys) = MySqlDdlRewriter.SplitForeignKeys(ddl, "t");

        Lf(table).Should().NotContain("FOREIGN KEY").And.Contain("`b` int NOT NULL\n)");
        foreignKeys.Select(fk => fk.Name).Should().Equal("fk_a", "fk_b");
    }

    /// <summary>A table with no foreign key must come back byte for byte as it went in.</summary>
    [Test]
    public void SplitForeignKeys_WhenThereIsNone_ShouldNotTouchTheStatement()
    {
        const string ddl = """
            CREATE TABLE `t` (
              `id` int NOT NULL,
              PRIMARY KEY (`id`)
            ) ENGINE=InnoDB
            """;

        var (table, foreignKeys) = MySqlDdlRewriter.SplitForeignKeys(ddl, "t");

        table.Should().Be(ddl);
        foreignKeys.Should().BeEmpty();
    }

    /// <summary>
    /// A CHECK is left inline on purpose: MySQL does not allow one to reference another table, so
    /// it can never make the order of the CREATE TABLE statements matter.
    /// </summary>
    [Test]
    public void SplitForeignKeys_ShouldLeaveCheckConstraintsInline()
    {
        const string ddl = """
            CREATE TABLE `t` (
              `id` int NOT NULL,
              CONSTRAINT `ck_t` CHECK ((`id` > 0)),
              CONSTRAINT `fk_t` FOREIGN KEY (`id`) REFERENCES `o` (`id`)
            ) ENGINE=InnoDB
            """;

        var (table, foreignKeys) = MySqlDdlRewriter.SplitForeignKeys(ddl, "t");

        Lf(table).Should().Contain("CONSTRAINT `ck_t` CHECK ((`id` > 0))\n)");
        foreignKeys.Should().ContainSingle();
    }
}
