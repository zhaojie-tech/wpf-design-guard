# WpfDesignGuard.Analyzers

Compile-time guard for WPF design systems. Add this package to a WPF project
(or a project referencing one transitively) and design-system violations surface
as compiler diagnostics.

## Requirements

- Roslyn compiler 4.11 or newer (.NET 8 SDK or newer; the package ships tier
  directories for both Roslyn 4.11 and 4.14).

## What it needs

- `designguard.json` at the project root (create it with `designguard init`,
  from the WpfDesignGuard dotnet tool). Without it the generator disables itself
  with a build warning — installing this package never breaks your build.
- Nothing else. The package injects the compiled XAML surface itself.

## Opt-out

Set `<DesignGuardDisableXaml>true</DesignGuardDisableXaml>` to turn the XAML
surface off for a project (the governance CLI is unaffected).
