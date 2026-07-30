using System.Collections.Frozen;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

/// <summary>
/// The keyword sets every dialect starts from. A provider unions these with its own words rather
/// than restating the standard ones.
/// </summary>
public static class SqlKeywords
{
    /// <summary>
    /// Builds a lookup that ignores case, which is the only way keywords are ever compared.
    /// </summary>
    public static FrozenSet<string> Set(params IEnumerable<string>[] sets) =>
        sets.SelectMany(s => s).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <c>CREATE OR REPLACE</c> has to be here as one phrase, or the <c>OR</c> in it is mistaken
    /// for a boolean connective and thrown onto a line of its own.
    /// </summary>
    public static readonly string[] Statement =
    [
        "CREATE", "CREATE OR REPLACE", "ALTER", "DROP", "TRUNCATE", "GRANT", "REVOKE", "DECLARE",
        "SET", "INSERT", "INSERT INTO", "DELETE", "DELETE FROM", "MERGE", "CALL", "EXEC",
        "EXECUTE", "USE", "COMMIT", "ROLLBACK", "SAVEPOINT", "RETURN", "OPEN", "CLOSE", "COMMENT"
    ];

    public static readonly string[] Clause =
    [
        "WITH", "SELECT", "FROM", "WHERE", "GROUP BY", "HAVING", "ORDER BY", "VALUES",
        "UPDATE", "SET", "ADD", "RETURNING", "PARTITION BY"
    ];

    /// <summary>Own line, value alongside.</summary>
    public static readonly string[] Line = ["LIMIT", "OFFSET", "FOR UPDATE"];

    /// <summary>
    /// <c>SET</c> only lays out as a clause inside an <c>UPDATE</c>; anywhere else it is a
    /// statement of its own. <c>ADD</c> likewise belongs to <c>ALTER TABLE</c>.
    /// </summary>
    public static readonly KeyValuePair<string, string>[] ContextualClause =
    [
        new("SET", "UPDATE"),
        new("ADD", "ALTER")
    ];

    public static readonly string[] Continuation =
    [
        "AND", "OR", "JOIN", "INNER JOIN", "LEFT JOIN", "LEFT OUTER JOIN", "RIGHT JOIN",
        "RIGHT OUTER JOIN", "FULL JOIN", "FULL OUTER JOIN", "CROSS JOIN", "NATURAL JOIN"
    ];

    public static readonly string[] SetOperator =
    [
        "UNION", "UNION ALL", "EXCEPT", "INTERSECT"
    ];

    /// <summary>
    /// Words cased as keywords, on top of everything in the layout sets above.
    /// </summary>
    public static readonly string[] Reserved =
    [
        "ALL", "ANY", "AS", "ASC", "BEGIN", "BETWEEN", "BOTH", "BY", "CASE", "CAST", "CHECK",
        "COLLATE", "COLUMN", "CONSTRAINT", "CROSS", "CURRENT_DATE", "CURRENT_TIME",
        "CURRENT_TIMESTAMP", "CURRENT_USER", "CURSOR", "DEFAULT", "DEFERRABLE", "DESC",
        "DISTINCT", "DO", "ELSE", "END", "END IF", "END LOOP", "ESCAPE", "EXCLUDE", "EXISTS",
        "FALSE", "FETCH", "FILTER", "FOR", "FOREIGN", "FULL", "FUNCTION", "IF", "ILIKE", "IN",
        "INDEX", "INNER", "INTO", "IS", "KEY", "LEADING", "LEFT", "LIKE", "LOOP", "NATURAL",
        "NOT", "NULL", "NULLS", "ON", "ONLY", "OUTER", "OVER", "PRIMARY", "PROCEDURE", "REFERENCES",
        "RIGHT", "ROW", "ROWS", "SCHEMA", "SIMILAR", "SOME", "TABLE", "THEN", "TO", "TRAILING",
        "TRIGGER", "TRUE", "UNIQUE", "UNKNOWN", "USING", "VIEW", "WHEN", "WHILE", "WINDOW"
    ];

    public static readonly string[] DataTypes =
    [
        "BIGINT", "BINARY", "BIT", "BLOB", "BOOL", "BOOLEAN", "CHAR", "CHARACTER", "CLOB", "DATE",
        "DATETIME", "DECIMAL", "DOUBLE", "FLOAT", "INT", "INTEGER", "INTERVAL", "MONEY", "NCHAR",
        "NUMERIC", "NVARCHAR", "REAL", "SMALLINT", "TEXT", "TIME", "TIMESTAMP", "TINYINT",
        "UUID", "VARBINARY", "VARCHAR", "XML"
    ];

    /// <summary>Block openers shared by every dialect that has procedural code.</summary>
    public static readonly string[] BlockOpen = ["BEGIN", "CASE", "LOOP"];

    /// <summary>
    /// Built-in functions, which take the function casing. A call to anything not listed here is
    /// assumed to be the author's own routine and is left exactly as written.
    /// </summary>
    public static readonly string[] Functions =
    [
        "ABS", "AVG", "CAST", "CEIL", "CEILING", "CHAR_LENGTH", "COALESCE", "CONCAT", "CONVERT",
        "COUNT", "CURRENT_DATE", "CURRENT_TIMESTAMP", "DENSE_RANK", "EXTRACT", "FLOOR", "GREATEST",
        "LEAST", "LENGTH", "LOWER", "LTRIM", "MAX", "MIN", "MOD", "NULLIF", "POWER", "RANK",
        "REPLACE", "ROUND", "ROW_NUMBER", "RTRIM", "SQRT", "SUBSTR", "SUBSTRING", "SUM", "TRIM",
        "UPPER"
    ];
}
