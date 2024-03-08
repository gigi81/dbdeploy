using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public record Step(string Database, string Name, string Branch, IDirectoryInfo Directory)
{
    public const string InitStepName = "_Init";

    private readonly bool _isInit = Name.Equals(InitStepName, StringComparison.InvariantCultureIgnoreCase);

    public bool IsInit => _isInit;
    
    public IFileInfo DeployScript =>
        this.Directory.File(_isInit ? $"{Name}.sql" : $"{Name}.Deploy.sql");
    
    public IFileInfo RollbackScript =>
        this.Directory.File($"{Name}.Rollback.sql");
    
    public IFileInfo TestScript => this.Directory.File($"{Name}.Test.sql");

    public IFileInfo[] MandatoryFiles =>
        _isInit ? [DeployScript] : [DeployScript, RollbackScript];

    public IFileInfo[] ExtraFiles =>
        _isInit ? [RollbackScript, TestScript] : [TestScript];
}
