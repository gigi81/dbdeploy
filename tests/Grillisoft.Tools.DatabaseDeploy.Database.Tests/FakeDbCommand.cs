using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests;

/// <summary>
/// A <see cref="DbCommand"/> that hands back canned rows, or throws, without a database.
/// </summary>
internal sealed class FakeDbCommand : DbCommand
{
    private readonly Func<DbDataReader> _execute;
    private readonly FakeDbParameterCollection _parameters = new();

    private FakeDbCommand(Func<DbDataReader> execute) => _execute = execute;

    /// <summary>A command returning one string column, one row per value.</summary>
    public static FakeDbCommand Returning(params string[] values)
    {
        var table = new DataTable();
        table.Columns.Add("name", typeof(string));

        foreach (var value in values)
            table.Rows.Add(value);

        return new FakeDbCommand(table.CreateDataReader);
    }

    public static FakeDbCommand Throwing(Exception exception) => new(() => throw exception);

    /// <summary>What the caller bound, in the order it bound it.</summary>
    public IReadOnlyList<(string Name, object? Value)> BoundParameters =>
        _parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value)).ToList();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => _execute();

    protected override DbParameter CreateDbParameter() => new FakeDbParameter();

    protected override DbParameterCollection DbParameterCollection => _parameters;

    // the base declares these as [AllowNull] string, so the override has to say so too
    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    public override int ExecuteNonQuery() => 0;
    public override object? ExecuteScalar() => null;
    public override void Prepare() { }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        public override int Size { get; set; }
        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }

        public override void ResetDbType() { }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;
        public override object SyncRoot => _parameters;

        public override int Add(object value)
        {
            _parameters.Add((DbParameter)value);
            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
                Add(value!);
        }

        public override void Clear() => _parameters.Clear();
        public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_parameters).CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();
        public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName)
            => _parameters.FindIndex(p => string.Equals(p.ParameterName, parameterName, StringComparison.Ordinal));

        public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _parameters.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _parameters.RemoveAt(index);
        public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => _parameters[index];
        protected override DbParameter GetParameter(string parameterName) => _parameters[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) => _parameters[IndexOf(parameterName)] = value;
    }
}
