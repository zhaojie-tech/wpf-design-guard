using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Workspace;
using WpfDesignGuard.Core.Xaml.Fixes;

namespace WpfDesignGuard.Cli.Commands;

/// <summary>
/// `designguard fix --rule DGxxxx [--dry-run|--apply]`: span-level text
/// editing with the fix directions fixed by R3. Dry-run prints a line diff; --apply
/// writes the file back and echoes the same diff. Self-check rolls back fixes that
/// would trip an already-shipped rule.
/// </summary>
internal static class FixCommand
{
    public static int Run(string[] args)
    {
        var rule = ArgumentParser.ValueOf(args, "--rule");
        if (rule == null)
        {
            Console.Error.WriteLine("designguard fix: --rule is required (e.g. --rule DG0002).");
            return ExitCodes.ConfigError;
        }

        if (!XamlFixEngine.Supports(rule))
        {
            Console.Error.WriteLine($"designguard fix: rule {rule} has no span-level fix in this build (supported: DG0002, DG0003, DG0007, DG0015).");
            return ExitCodes.ConfigError;
        }

        var apply = ArgumentParser.Has(args, "--apply");
        var root = ArgumentParser.ValueOf(args, "--root") ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(root, "designguard.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"designguard fix: no designguard.json at '{root}' — run `designguard init` first.");
            return ExitCodes.ConfigError;
        }

        var load = ConfigLoader.LoadFromText(File.ReadAllText(configPath));
        if (load.HasErrors || load.Config == null)
        {
            Console.Error.WriteLine("designguard fix: designguard.json is invalid:");
            foreach (var e in load.Errors)
            {
                Console.Error.WriteLine($"  {e}");
            }

            return ExitCodes.ConfigError;
        }

        var config = load.Config;
        var scanRoot = Path.GetFullPath(Path.Combine(root, config.Scope.Root));
        var files = WorkspaceScanner.ScanXamlFiles(scanRoot, config.Scope);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("designguard fix: scan surface is empty (anti-idle self-check).");
            return ExitCodes.ConfigError;
        }

        var totalApplied = 0;
        var allUnfixed = new List<string>();
        foreach (var file in files)
        {
            var fullPath = Path.Combine(scanRoot, file);
            var text = File.ReadAllText(fullPath);
            var application = XamlFixEngine.FixFile(text, file, rule, config);
            allUnfixed.AddRange(application.Unfixed);

            if (application.AppliedCount == 0)
            {
                continue;
            }

            Console.WriteLine($"{file}: {application.AppliedCount} edit(s)");
            PrintLineDiff(text, application.NewText);

            if (apply)
            {
                File.WriteAllText(fullPath, application.NewText, new UTF8Encoding(false));
            }

            totalApplied += application.AppliedCount;
        }

        foreach (var u in allUnfixed)
        {
            Console.WriteLine($"  not auto-fixed: {u}");
        }

        Console.WriteLine(apply
            ? $"designguard fix: {totalApplied} edit(s) applied for {rule}."
            : $"designguard fix (dry run): {totalApplied} edit(s) available for {rule}. Re-run with --apply to write.");
        return ExitCodes.Green;
    }

    private static void PrintLineDiff(string before, string after)
    {
        var oldLines = before.Replace("\r\n", "\n").Split('\n');
        var newLines = after.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < Math.Max(oldLines.Length, newLines.Length); i++)
        {
            var o = i < oldLines.Length ? oldLines[i] : null;
            var n = i < newLines.Length ? newLines[i] : null;
            if (!string.Equals(o, n, StringComparison.Ordinal))
            {
                if (o != null)
                {
                    Console.WriteLine($"  - {o.TrimStart()}");
                }

                if (n != null)
                {
                    Console.WriteLine($"  + {n.TrimStart()}");
                }
            }
        }
    }
}
