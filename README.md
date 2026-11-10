# MD Search to Selection

A Navisworks add-in that turns **Search Sets** into **Selection Sets** in one click: one set, many sets, whole folders, or all of them.

A search set runs its conditions again every time you click it, which is slow on big federated models. A selection set stores the items it found, so clicking it later is instant. This tool runs each chosen search once and saves what it found as a selection set, keeping your folder structure. Your search sets are never changed or deleted.

**Author:** MD (Mayank Dholariya)  
**Version:** 1.0.0  
**License:** MIT

## Screenshots

_Add screenshots of the dock pane and a typical conversion result in `docs/images/`._

## Supported Navisworks versions

Autodesk Navisworks **Manage** and **Simulate** **2017–2027** (64-bit). Each year gets its own DLL built against that year's API. Navisworks Freedom has no add-ins and is not supported.

## Install (no administrator rights)

Download the latest installer or portable zip from [Releases](../../releases).

- **Setup:** run `MD_Navisworks_SearchToSelection_Setup_v1.0.0.exe`. It installs for your Windows user only, to `%APPDATA%\Autodesk\ApplicationPlugins\MD.SearchToSelection.bundle`. No admin rights, no Program Files, no HKLM. Close Navisworks before installing.
- **Portable:** unzip `MD_Navisworks_SearchToSelection_Portable_v1.0.0.zip` and run `install.cmd`. Run `check.cmd` to see what is installed and `uninstall.cmd` to remove it.
- **Uninstall:** Windows Settings → Apps → "MD Search to Selection", or `uninstall.cmd` from the portable zip.

## Use

Open your model, then go to **Add-ins → Tool add-ins 1 → MD Search to Selection**. The button shows and hides the pane.

1. The pane lists every search set in the Sets window, inside its folders. Plain selection sets are not listed (they are already fast). Press **Refresh** after you add or rename sets, or open another model.
2. Tick the search sets you want. Ticking a folder ticks everything inside it.
3. Check the options (the defaults are fine). **Output folder** (default `Converted`) holds the new selection sets, with the same sub-folders as the originals.
4. Press **Convert checked**. The status line and the list below show OK, SKIPPED or FAILED for each set.
5. Save the model (NWF/NWD) afterwards to keep the new selection sets.

A selection set is a snapshot of what the search found at conversion time. After a model update, convert again with **Overwrite** on. The log is `%APPDATA%\MDSearchToSelection\mdsearchtoselection.log`.

See the shipped README in a Release for the full option list and known limits.

## Build from source

You need the .NET SDK (8 or later is fine for tests; plugin builds target .NET Framework 4.6–4.8).

**Do not commit Autodesk Navisworks DLLs.** They are not redistributable. Point `NwRefDir` at your own Navisworks install (or at a private local folder of API reference assemblies).

```bash
# Core unit tests (no Navisworks required)
dotnet test tests/SearchToSelection.Tests/SearchToSelection.Tests.csproj -c Release

# Plugin DLL for one year, against your Navisworks install
dotnet build src/MDSearchToSelection/MDSearchToSelection.csproj -c Release \
  -p:NwYear=2024 \
  -p:NwRefDir="C:/Program Files/Autodesk/Navisworks Manage 2024" \
  -p:NwTfm=net48
```

`NwTfm` is `net46` for 2017–2018, `net47` for 2019–2020, and `net48` for 2021–2027.

To build every year, set `NW_REFS` to a directory of per-year API folders (`$NW_REFS/2017`, `$NW_REFS/2018`, … each containing `Autodesk.Navisworks.Api.dll`) and run `./build_all.sh`. That script always runs the Core tests first.

The no-admin installer is compiled with [Inno Setup 6](https://jrsoftware.org/isinfo.php) from `installer/SearchToSelection_NoAdmin.iss` (or from `installer/Plugin_NoAdmin.iss.template` via `python3 installer/make_release.py`). Set the `ISCC` environment variable to `ISCC.exe` if it is not on your PATH. `make_release.py` also stages `PackageContents.xml`, builds the portable zip, and writes checksums. It will not overwrite an existing `release/v<VERSION>/` folder.

## Tests

```bash
dotnet test tests/SearchToSelection.Tests/SearchToSelection.Tests.csproj -c Release
```

The Core tests use a fake Sets window and do not need Navisworks.

## License

MIT © 2026 MD (Mayank Dholariya). See [LICENSE](LICENSE).
