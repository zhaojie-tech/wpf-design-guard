using System;

namespace WpfDesignGuard.Cli.Commands;

internal static class ArgumentParser
{
    /// <summary>Returns the value following <paramref name="name"/> (either "--name value" or "--name=value"), or null.</summary>
    public static string? ValueOf(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
            {
                return args[i].Substring(name.Length + 1);
            }
        }

        return null;
    }

    public static bool Has(string[] args, string name)
    {
        foreach (var a in args)
        {
            if (a == name)
            {
                return true;
            }
        }

        return false;
    }
}
