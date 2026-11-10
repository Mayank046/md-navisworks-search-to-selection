#!/bin/bash
# Build MDSearchToSelection.dll for every Navisworks year (2017-2027).
# Author: MD (Mayank Dholariya)
#
# Usage:
#   ./build_all.sh                         # run Core unit tests only
#   NW_REFS=/path/to/refs ./build_all.sh   # tests + plugin DLLs for 2017-2027
#
# NW_REFS is a folder of Autodesk Navisworks API assemblies that you provide.
# Do not commit those DLLs; they are Autodesk's and are not redistributable.
# Accepted layouts (first match wins for each year):
#   $NW_REFS/<year>/Autodesk.Navisworks.Api.dll
#   $NW_REFS/pkgs/Navisworks-<year>-x64-API.*/lib/<tfm>/   (2017-2019)
#   $NW_REFS/pkgs/Speckle.Navisworks.API.<year>.0.0/lib/net48/
#   $NW_REFS/merged-<year>/
#
# Or build a single year against your own Navisworks install:
#   dotnet build src/MDSearchToSelection/MDSearchToSelection.csproj -c Release \
#     -p:NwYear=2024 \
#     -p:NwRefDir="C:/Program Files/Autodesk/Navisworks Manage 2024" \
#     -p:NwTfm=net48
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

tfm(){ case $1 in 2017|2018) echo net46;; 2019|2020) echo net47;; *) echo net48;; esac; }

resolve_ref() {
  local y="$1" t
  t=$(tfm "$y")
  if [[ -f "$NW_REFS/$y/Autodesk.Navisworks.Api.dll" ]]; then
    echo "$NW_REFS/$y"; return 0
  fi
  if [[ -f "$NW_REFS/merged-$y/Autodesk.Navisworks.Api.dll" ]]; then
    echo "$NW_REFS/merged-$y"; return 0
  fi
  local p="$NW_REFS/pkgs"
  local d
  if [[ "$y" == "2017" || "$y" == "2018" || "$y" == "2019" ]]; then
    for d in "$p"/Navisworks-"$y"-x64-API.*/lib/"$t"; do
      if [[ -f "$d/Autodesk.Navisworks.Api.dll" ]]; then echo "$d"; return 0; fi
    done
  fi
  if [[ -f "$p/Speckle.Navisworks.API.$y.0.0/lib/net48/Autodesk.Navisworks.Api.dll" ]]; then
    echo "$p/Speckle.Navisworks.API.$y.0.0/lib/net48"; return 0
  fi
  return 1
}

echo "== Unit tests (core, no Navisworks) =="
dotnet test "tests/SearchToSelection.Tests/SearchToSelection.Tests.csproj" -c Release -nologo -v q --nologo

if [[ -z "${NW_REFS:-}" ]]; then
  echo
  echo "Skipping plugin DLL builds: set NW_REFS to a directory of Navisworks API"
  echo "references (see the comments at the top of this script), or build one year"
  echo "with -p:NwYear -p:NwRefDir -p:NwTfm as shown in the README."
  exit 0
fi

rm -rf build
mkdir -p build
FAILED=""
for y in 2017 2018 2019 2020 2021 2022 2023 2024 2025 2026 2027; do
  if ! REFDIR=$(resolve_ref "$y"); then
    echo "MISSING API refs for $y under $NW_REFS"
    FAILED="$FAILED $y"
    continue
  fi
  echo "== $y ($(tfm "$y")) -> $REFDIR"
  if [[ ! -f "$REFDIR/Autodesk.Navisworks.Api.dll" ]]; then
    echo "MISSING Autodesk.Navisworks.Api.dll for $y"; FAILED="$FAILED $y"; continue
  fi

  rm -rf src/MDSearchToSelection/obj src/SearchToSelection.Core/obj
  if ! dotnet build src/MDSearchToSelection/MDSearchToSelection.csproj -c Release -nologo -v q -p:NuGetAudit=false \
    -p:NwYear="$y" -p:NwRefDir="$REFDIR" -p:NwTfm="$(tfm "$y")"; then
    echo "BUILD FAILED $y"; FAILED="$FAILED $y"; continue
  fi
  if [[ ! -f build/$y/MDSearchToSelection.dll ]]; then
    echo "BUILD FAILED $y (dll missing)"; FAILED="$FAILED $y"
  fi
done
echo "Built:"
ls -la build/*/MDSearchToSelection.dll 2>/dev/null || true
if [[ -n "$FAILED" ]]; then
  echo "FAILED years:$FAILED"
  exit 1
fi
echo "All years OK."
