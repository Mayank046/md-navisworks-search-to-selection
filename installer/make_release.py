#!/usr/bin/env python3
"""MD plugin release builder - MD Search to Selection.
stage bundle -> PackageContents.xml -> .iss from Plugin_NoAdmin.iss.template -> Inno compile (Wine) -> portable zip -> SHA256SUMS.
Author: MD (Mayank Dholariya)

Fill the CONSTANTS once per plugin. Generate the three GUIDs ONCE (python -c "import uuid;print(uuid.uuid4())")
and never change them afterwards: they are how upgrades find the old install."""
import hashlib, os, re, shutil, subprocess, sys, zipfile
from xml.sax.saxutils import quoteattr as qa

# ---------------- CONSTANTS (edit) ----------------
VER = open(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "VERSION")).read().strip()
NAME = "MDSearchToSelection"                 # assembly / DLL name, icon base name
APP = "MD Search to Selection"                 # display name
SHORT = "SearchToSelection"                  # used in file names
BUNDLE = f"MD.{SHORT}.bundle"      # %APPDATA%\Autodesk\ApplicationPlugins\<BUNDLE>
AUTHOR = "MD (Mayank Dholariya)"
DESC = "Converts Navisworks Search Sets into fast Selection Sets in one click"
APPID = "0014CDE4-62A3-4312-8F4D-A200E3743A78"     # Inno AppId, no braces  (GENERATE ONCE)
PRODUCT = "{89AE62CA-0E01-434B-BA48-77E114635519}" # PackageContents ProductCode (GENERATE ONCE)
UPGRADE = "{FB2F0DB9-0F77-499A-B30A-3AB3919ED36D}" # PackageContents UpgradeCode (GENERATE ONCE)
EXTRA_RELEASE_FILES = []   # no rules.json: this tool has no project rules (see README)
# ---------------------------------------------------
YEARS = range(2017, 2028)
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def find_iscc():
    cands = []
    if os.environ.get("ISCC"):
        cands.append(os.environ["ISCC"])
    local = os.environ.get("LOCALAPPDATA") or ""
    if local:
        cands.append(os.path.join(local, "Programs", "Inno Setup 6", "ISCC.exe"))
    cands += [
        r"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        r"C:\Program Files\Inno Setup 6\ISCC.exe",
        os.path.expanduser("~/.wine-inno/drive_c/Program Files (x86)/Inno Setup 6/ISCC.exe"),
        os.path.expanduser("~/.wine-inno/drive_c/Program Files/Inno Setup 6/ISCC.exe"),
    ]
    for c in cands:
        if c and os.path.isfile(c):
            return c
    sys.exit("ISCC.exe not found. Install Inno Setup 6, or set the ISCC environment variable to ISCC.exe.")

def rel_iss(p):
    """Path relative to installer/, using backslashes, for the committed .iss file."""
    return os.path.relpath(p, os.path.join(ROOT, "installer")).replace("/", "\\")

def wine_path(p):
    if os.name == "nt":
        return p
    p = os.path.abspath(p)
    return "Z:" + p.replace("/", "\\")

TEMPLATE = os.path.join(ROOT, "installer", "Plugin_NoAdmin.iss.template")
REL = os.path.join(ROOT, "release", "v" + VER)      # one folder per version; older folders are never touched
STAGE = os.path.join(ROOT, "stage"); B = os.path.join(STAGE, BUNDLE)
SETUP = f"MD_Navisworks_{SHORT}_Setup_v{VER}"; ZIPN = f"MD_Navisworks_{SHORT}_Portable_v{VER}.zip"
def z(p): return "Z:" + p.replace("/", "\\")

def stage():
    shutil.rmtree(STAGE, ignore_errors=True); os.makedirs(os.path.join(B, "Contents"))
    shutil.copy(os.path.join(ROOT, "icon", NAME + ".ico"), os.path.join(B, "Contents"))
    comps = []
    for y in YEARS:
        d = os.path.join(B, "Contents", str(y)); os.makedirs(os.path.join(d, "Images"))
        dll = os.path.join(ROOT, "build", str(y), NAME + ".dll")
        if not os.path.isfile(dll): sys.exit("missing " + dll + "  (run ./build_all.sh first)")
        shutil.copy(dll, d)
        for s in (16, 32): shutil.copy(os.path.join(ROOT, "icon", f"{NAME}_{s}.png"), os.path.join(d, "Images"))
        comps.append(f'  <Components Description="Navisworks {y}">\n'
                     f'    <RuntimeRequirements OS="Win64" Platform="NAVMAN|NAVSIM" SeriesMin="Nw{y-2003}" SeriesMax="Nw{y-2003}" />\n'
                     f'    <ComponentEntry AppName={qa(APP)} AppType="ManagedPlugin" ModuleName="./Contents/{y}/{NAME}.dll" AppDescription={qa(DESC)} />\n'
                     f'  </Components>')
    xml = ('<?xml version="1.0" encoding="utf-8"?>\n'
           f'<ApplicationPackage SchemaVersion="1.0" AutodeskProduct="Navisworks" ProductType="Application" Name={qa(APP)} '
           f'AppVersion="{VER}" Description={qa(DESC.rstrip(".") + ". Author: " + AUTHOR)} Author={qa(AUTHOR)} ProductCode="{PRODUCT}" UpgradeCode="{UPGRADE}" '
           f'Icon="./Contents/{NAME}.ico">\n  <CompanyDetails Name={qa(AUTHOR)} />\n' + "\n".join(comps) + "\n</ApplicationPackage>\n")
    open(os.path.join(B, "PackageContents.xml"), "w", encoding="utf-8").write(xml)

def iss():
    s = open(TEMPLATE, encoding="utf-8-sig").read()
    tok = {"__APP__": APP, "__VER__": VER, "__APPID__": APPID, "__REL__": rel_iss(REL), "__SETUP__": SETUP,
           "__ICON__": rel_iss(os.path.join(ROOT, "icon", NAME + ".ico")),
           "__WIZL__": rel_iss(os.path.join(ROOT, "icon", "wizard_large.bmp")),
           "__WIZS__": rel_iss(os.path.join(ROOT, "icon", "wizard_small.bmp")),
           "__NAME__": NAME, "__BUNDLE__": BUNDLE, "__STAGE__": rel_iss(B)}
    for k, v in tok.items():
        if k not in s: sys.exit("token not in template: " + k)
        s = s.replace(k, v)
    left = re.findall(r"__[A-Z]+__", s)
    if left: sys.exit("unfilled tokens: " + ", ".join(sorted(set(left))))
    p = os.path.join(ROOT, "installer", f"{SHORT}_NoAdmin.iss")
    open(p, "w", encoding="utf-8-sig", newline="\r\n").write(s)
    return p

def compile_(p):
    iscc = find_iscc()
    if os.name == "nt":
        r = subprocess.run([iscc, "/Q", p], capture_output=True, text=True)
    else:
        env = dict(os.environ, WINEPREFIX=os.environ.get("WINEPREFIX", os.path.expanduser("~/.wine-inno")), WINEDEBUG="-all")
        r = subprocess.run(["wine", wine_path(iscc), "/Q", wine_path(p)], env=env, capture_output=True, text=True)
    if r.returncode != 0:
        print((r.stdout or "")[-3000:], (r.stderr or "")[-3000:])
        sys.exit("ISCC failed")

def portable():
    out = os.path.join(REL, ZIPN); top = f"MD_{SHORT}_Portable_v{VER}/"
    ps1 = f"md_{SHORT.lower()}.ps1"
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        for dp, _, fs in os.walk(B):
            for f in sorted(fs):
                full = os.path.join(dp, f); zf.write(full, top + "bundles/" + os.path.relpath(full, STAGE).replace(os.sep, "/"))
        for f in ("install.cmd", "uninstall.cmd", "check.cmd", ps1):
            zf.write(os.path.join(ROOT, "installer", "portable", f), top + f)
        zf.write(os.path.join(ROOT, "README.md"), top + "README.md")
        for src, name in EXTRA_RELEASE_FILES:
            if os.path.isfile(os.path.join(ROOT, src)): zf.write(os.path.join(ROOT, src), top + name)

def main():
    if "00000000-0000-0000-0000" in APPID + PRODUCT + UPGRADE or DESC.startswith("__"):
        sys.exit("Fill DESC and generate APPID / PRODUCT / UPGRADE GUIDs first (see CONSTANTS).")
    if os.path.isdir(REL) and os.listdir(REL):
        sys.exit(f"{REL} already exists. Bump VERSION - never overwrite a delivered release.")
    os.makedirs(REL, exist_ok=True)
    stage(); compile_(iss()); portable()
    shutil.copy(os.path.join(ROOT, "README.md"), os.path.join(REL, f"README_MD_{SHORT}_v{VER}.md"))
    for src, name in EXTRA_RELEASE_FILES:
        if os.path.isfile(os.path.join(ROOT, src)): shutil.copy(os.path.join(ROOT, src), os.path.join(REL, name))
    lines = []
    for f in sorted(os.listdir(REL)):
        if f == "SHA256SUMS": continue
        lines.append(hashlib.sha256(open(os.path.join(REL, f), "rb").read()).hexdigest() + "  " + f)
    open(os.path.join(REL, "SHA256SUMS"), "w").write("\n".join(lines) + "\n")
    print("\n".join(lines))

if __name__ == "__main__": main()
