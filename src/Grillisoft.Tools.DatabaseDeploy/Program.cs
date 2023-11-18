using System;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy;

try
{
    var fs = new FileSystem();
    var directory = fs.DirectoryInfo.New(".");
    var manager = DeployManager.Load(directory);
                
    manager.Validate();

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    return -1;
}