# Contributing

Thank you for helping improve Internet Meter.

## Before you start

- Search existing issues before opening a duplicate report.
- Keep changes focused and avoid committing generated files from `bin/`, `obj/`, `.dotnet/`, `.tools/`, or `artifacts/`.
- Discuss large behavior or architecture changes in an issue before investing in an implementation.

## Development setup

Use Windows 10/11 x64 with the .NET 8 SDK, PowerShell 5.1 or later, and an x64 MinGW-w64 `g++` compiler. See the [README](README.md#build-and-run) for supported compiler locations and build commands.

Restore and test from the repository root:

```powershell
dotnet restore InternetMeter.sln
dotnet test InternetMeter.sln -c Release
```

To exercise the complete packaging path:

```powershell
.\scripts\build.ps1 -Configuration Release
```

Inno Setup is optional unless the change affects installer behavior.

## Pull requests

- Add or update tests when behavior changes.
- Update the README, user guide, or architecture notes when the user experience or design changes.
- Confirm that `dotnet test InternetMeter.sln -c Release` passes.
- Describe Windows-version-specific behavior, especially changes to taskbar integration or elevation.
- Do not include personal application data, databases, logs, credentials, packaged binaries, or local toolchains.

## Bug reports

Include the Windows version, Internet Meter version or commit, selected adapter type, expected behavior, actual behavior, and steps to reproduce. Attach only the relevant portion of a diagnostic log and remove personal paths, process names, addresses, or other sensitive data first.

For vulnerabilities, follow the private process in [SECURITY.md](SECURITY.md) instead of opening a public issue.
