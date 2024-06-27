namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public record DatabaseMigration
{
    public DatabaseMigration(string name, string user, string hash)
        : this(name, System.DateTime.UtcNow, user, hash)
    {
    }
    
    public DatabaseMigration(string name, DateTime dateTime, string user, string hash)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Null or empty name", nameof(name));

        if(dateTime.Kind != DateTimeKind.Utc)
            throw new ArgumentException($"Invalid datetime kind specified, expected utc was {dateTime.Kind}", nameof(name));
        
        if (string.IsNullOrEmpty(user))
            throw new ArgumentException("Null or empty user", nameof(user));
        
        if (string.IsNullOrEmpty(hash))
            throw new ArgumentException("Null or empty hash", nameof(hash));

        if (hash.Length != Step.HashLength)
            throw new ArgumentException($"Invalid hash length, expected {Step.HashLength} but was {hash.Length}", nameof(hash));
        
        this.Name = name;
        this.DateTime = ((DateTimeOffset)dateTime).TrimToSeconds();
        this.User = user;
        this.Hash = hash;
    }
    
    public string Name { get; }
    public DateTimeOffset DateTime { get; }
    public string User { get; }
    public string Hash { get; }

    public virtual bool Equals(DatabaseMigration? other)
    {
        if (ReferenceEquals(null, other))
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return this.Name == other.Name &&
               this.DateTime.Equals(other.DateTime) &&
               this.User == other.User &&
               this.Hash == other.Hash;
    }

    public override int GetHashCode() => HashCode.Combine(Name, DateTime, User, Hash);
}
