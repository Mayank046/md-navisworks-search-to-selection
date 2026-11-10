# Contributing

Bug reports, fixes and small improvements are welcome.

## How to help

1. Open an issue or a pull request against this repository.
2. Keep changes focused. Do not mix formatting-only edits with behaviour changes.
3. Match the style of the surrounding C# (existing naming, no unused abstractions).
4. Run the Core unit tests and keep them passing:

```bash
dotnet test tests/SearchToSelection.Tests/SearchToSelection.Tests.csproj -c Release
```

5. If you touch joint counting, Excel I/O, snap math or set conversion, add or update a test in the Core project.

## Please do not

- Commit Autodesk Navisworks DLLs, `bin/`, `obj/`, installers, zips, or anything from a Navisworks install. Point `NwRefDir` at your own copy instead.
- Commit secrets, machine paths, client models, or real project documents.
- Change Inno Setup `AppId`, PackageContents `ProductCode` or `UpgradeCode` unless you intend to break upgrades of existing installs.

## Pull requests

Describe what you changed and how you tested it. Screenshots of the dock pane help for UI changes.
