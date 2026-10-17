# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 0.1.x   | ✅        |

## Reporting a vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Report privately via GitHub Security Advisories ("Report a vulnerability" on the
repository's Security tab). Include:

- The affected package(s): `WpfDesignGuard.Analyzers`, `WpfDesignGuard.Cli`, or `WpfDesignGuard.Core`.
- A minimal reproduction (XAML / `designguard.json` / command line).
- The impact you observed.

You will receive an acknowledgement within 5 business days and a fix or mitigation
plan within 30 days for confirmed issues. Fixes are released as a patch version and
disclosed in the advisory once a fixed version is published.

## Scope

WpfDesignGuard runs **no user code** and loads **no user assemblies** at analysis
time (see ADR-0002 / the design notes on the CLI "no code execution" principle).
Reports about dependency CVEs should go through standard `dotnet list package
--vulnerable` tooling first; only report here if a dependency vulnerability is
actually exploitable through this tool's usage patterns.
