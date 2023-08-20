namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public record Version(string Name, DateTimeOffset DateTime, string User, string Hash);
