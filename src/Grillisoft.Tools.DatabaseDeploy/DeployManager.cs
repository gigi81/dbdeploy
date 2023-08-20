using System.Collections.Generic;
using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy
{
    public sealed class DeployManager
    {
        public static DeployManager Load(IDirectoryInfo directory)
        {
            var manager = new DeployManager(directory);
            manager.Load();
            return manager;
        }

        private readonly IDirectoryInfo _directory;
        private readonly Dictionary<string, Branch> _branches = new Dictionary<string, Branch>();

        private DeployManager(IDirectoryInfo directory)
        {
            _directory = directory;
        }

        public IReadOnlyDictionary<string, Branch> Branches => _branches;

        private void Load()
        {
            _directory.ThrowIfNotFound();
            _directory.ForEachFile((f, _) => {
                var branch = Branch.Load(f);
                _branches.Add(branch.Name, branch);
            }, null, false, "*.csv");
        }
    }
}
