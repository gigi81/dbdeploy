using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public record class Step(string Database, string Name, IDirectoryInfo Directory)
{
    public const string InitStepName = "_Init";

    private readonly bool _isInit = Name.Equals(InitStepName);
    
    public IFileInfo DeployScript =>
        _isInit ? this.Directory.File($"{Name}.sql")
                : this.Directory.File($"{Name}.Deploy.sql");
    
    public IFileInfo RollbackScript =>
        _isInit ? this.Directory.File($"{Name}.sql")
            : this.Directory.File($"{Name}.Rollback.sql");
    
    public IFileInfo TestScript => this.Directory.File($"{Name}.Test.sql");

    public IFileInfo[] MandatoryFiles => new[] { DeployScript, RollbackScript };

    public IFileInfo[] ExtraFiles => new[] { TestScript };
}
