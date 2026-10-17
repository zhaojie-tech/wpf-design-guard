namespace WpfDesignGuard.Cli.Commands;

internal static class NotYetShipped
{
    public static int Run(string command, string note)
    {
        Console.Error.WriteLine($"{command}: not available in this build — {note}.");
        return ExitCodes.ConfigError;
    }
}
