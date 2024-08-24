namespace Grillisoft.Tools.DatabaseDeploy.Cli;

public class ConsoleProgress : IProgress<int>
{
    public void Report(int value)
    {
        if (value < 0 || value > 100)
            return;

        Console.Write($"\x1b]9;4;1;{value}\x07");
    }
}