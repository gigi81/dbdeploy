using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy
{
    public class Branch
    {
        public static Branch Load(IFileInfo file)
        {
            var branch = new Branch(file);
            branch.Load();
            return branch;
        }

        private readonly IFileInfo _file;
        private readonly string _name;

        internal Branch(IFileInfo file)
        {
            _file = file;
            _name = file.Name;
        }

        public string Name => _name;

        public void Load()
        {
            _file.ThrowIfNotFound();
        }
    }
}
