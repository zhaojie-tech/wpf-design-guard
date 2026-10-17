namespace WpfDesignGuard.Cli;

/// <summary>Exit-code contract: 0 green; 1 config error; 2 violations; 3 engine error.</summary>
internal static class ExitCodes
{
    public const int Green = 0;
    public const int ConfigError = 1;
    public const int Violations = 2;
    public const int EngineError = 3;
}

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                PrintUsage();
                return args.Length == 0 ? ExitCodes.ConfigError : ExitCodes.Green;
            }

            return args[0] switch
            {
                "init" => Commands.InitCommand.Run(args.Skip(1).ToArray()),
                "verify" => Commands.VerifyCommand.Run(args.Skip(1).ToArray()),
                "baseline" => Commands.BaselineCommand.Run(args.Skip(1).ToArray()),
                "fix" => Commands.FixCommand.Run(args.Skip(1).ToArray()),
                "report" => Commands.ReportCommand.Run(args.Skip(1).ToArray()),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"designguard: engine error: {ex.GetType().Name}: {ex.Message}");
            return ExitCodes.EngineError;
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"designguard: unknown command '{command}'.");
        PrintUsage();
        return ExitCodes.ConfigError;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("designguard — WPF design-system governance CLI (dual-layer ratchet gate)");
        Console.WriteLine();
        Console.WriteLine("usage:");
        Console.WriteLine("  designguard init [--root <dir>]                create designguard.json, stock findings enter the baseline");
        Console.WriteLine("  designguard verify [--root <dir>] [--project <proj>]");
        Console.WriteLine("                                                 full evaluation; hard-zero AND exemptions AND ratchet");
        Console.WriteLine("  designguard baseline --update [--root <dir>]   rewrite the global baseline from current counts");
        Console.WriteLine("  designguard fix --rule DGxxxx [--dry-run|--apply]   (ships with v0.1 M3)");
        Console.WriteLine("  designguard report --format sarif              (ships with v0.1 M3)");
        Console.WriteLine();
        Console.WriteLine("exit codes: 0 green | 1 configuration error | 2 violations/over baseline | 3 engine error");
    }
}
