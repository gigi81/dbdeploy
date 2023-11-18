namespace Grillisoft.Tools.DatabaseDeploy.Contracts;
public record Branch(string Name, IReadOnlyCollection<Step> Steps);