namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public record DatabaseMigration(string Name, DateTimeOffset DateTime, string User, string Hash);
