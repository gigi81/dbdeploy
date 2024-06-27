using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public record Step(string Database, string Name, string Branch, bool IsInit, IDirectoryInfo Directory)
{
    public const int HashLength = 32;
    
    private string? _hash;
    private IList<IFileInfo>? _dataFiles;

    public IFileInfo DeployScript =>
        this.Directory.File(IsInit ? $"{Name}.sql" : $"{Name}.Deploy.sql");
    
    public IFileInfo RollbackScript =>
        this.Directory.File($"{Name}.Rollback.sql");
    
    public IFileInfo TestScript => this.Directory.File($"{Name}.Test.sql");

    public IList<IFileInfo> DataScripts => _dataFiles ??= GetDataFilesList();

    private IList<IFileInfo> GetDataFilesList()
    {
        return this.Directory.GetFiles($"{Name}.Data*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.Name)
            .ToList();
    }

    public async Task<string> GetStepHash() => _hash ??= await ComputeHash(this.DeployScript);
    
    private static async Task<string> ComputeHash(IFileInfo file)
    {
        using var md5 = MD5.Create();
        await using var stream = file.OpenRead();
        var data = await md5.ComputeHashAsync(stream);
        var builder = new StringBuilder(HashLength);

        foreach (var b in data)
        {
            builder.Append(b.ToString("x2")); // Convert to hexadecimal
        }

        return builder.ToString();
    }
}
