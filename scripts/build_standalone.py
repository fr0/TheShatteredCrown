"""Stage two of the turn-engine split: build the standalone "Turn-Based
Combat" mod from the same engine source that ships inside The Shattered
Crown (Source/TSC.TurnBased).

The transform is a whole-identifier rewrite, applied to copied sources only
(the repo is never touched):
    TheShatteredCrown  -> TurnBasedCombat     (namespace: GenTypes/Scribe identity)
    theshatteredcrown  -> turnbasedcombat     (harmony id)
    TSC_               -> TBC_                (classes, defNames, texture paths)
    [The Shattered Crown] -> [Turn-Based Combat]   (log prefixes)

Distinct namespaces + distinct defNames are what make co-loading safe: the
two mods share no reflection identity and no defs. The runtime guard in
TSC_TurnBasedInit (#if TBC_STANDALONE) additionally stands this mod down
entirely when TSC is active.

Output: dist/TurnBasedCombat/ - a complete, uploadable mod folder.
Run:  py scripts/build_standalone.py
"""
import os
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Source", "TSC.TurnBased")
GEN = os.path.join(ROOT, "build", "standalone-src")
OUT = os.path.join(ROOT, "dist", "TurnBasedCombat")

REWRITES = [
    ("[The Shattered Crown]", "[Turn-Based Combat]"),
    ("TheShatteredCrown", "TurnBasedCombat"),
    ("theshatteredcrown", "turnbasedcombat"),
    ("TSC_", "TBC_"),
]


def rewrite(text):
    for old, new in REWRITES:
        text = text.replace(old, new)
    return text


# ---------------------------------------------------------------- sources
if os.path.exists(GEN):
    shutil.rmtree(GEN)
os.makedirs(GEN)
for name in os.listdir(SRC):
    if not name.endswith(".cs"):
        continue
    text = open(os.path.join(SRC, name), encoding="utf-8").read()
    open(os.path.join(GEN, rewrite(name)), "w", encoding="utf-8", newline="").write(rewrite(text))
print(f"generated sources -> {os.path.relpath(GEN, ROOT)}")

open(os.path.join(GEN, "TurnBasedCombat.csproj"), "w", encoding="utf-8", newline="").write("""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>1.0.0</Version>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <RootNamespace>TurnBasedCombat</RootNamespace>
    <AssemblyName>TurnBasedCombat</AssemblyName>
    <DefineConstants>TBC_STANDALONE</DefineConstants>
    <OutputPath>$(MSBuildThisFileDirectory)..\\..\\dist\\TurnBasedCombat\\1.6\\Assemblies\\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <DebugType>none</DebugType>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <ProduceReferenceAssembly>false</ProduceReferenceAssembly>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
    <PackageReference Include="Krafs.Rimworld.Ref" Version="1.6.*" PrivateAssets="all" ExcludeAssets="runtime" />
    <PackageReference Include="Lib.Harmony" Version="2.*" PrivateAssets="all" ExcludeAssets="runtime" />
  </ItemGroup>
</Project>
""")

# ---------------------------------------------------------------- mod tree
if os.path.exists(OUT):
    shutil.rmtree(OUT)
os.makedirs(os.path.join(OUT, "About"))
os.makedirs(os.path.join(OUT, "1.6", "Defs"))
os.makedirs(os.path.join(OUT, "Textures", "UI", "TBC_Abilities"))

open(os.path.join(OUT, "About", "About.xml"), "w", encoding="utf-8", newline="").write("""<?xml version="1.0" encoding="utf-8"?>
<ModMetaData>
  <name>Turn-Based Combat</name>
  <author>fr0</author>
  <packageId>fr0.turnbasedcombat</packageId>
  <modVersion IgnoreIfNoMatchingField="True">1.0.0</modVersion>
  <supportedVersions>
    <li>1.6</li>
  </supportedVersions>
  <modDependencies>
    <li>
      <packageId>brrainz.harmony</packageId>
      <displayName>Harmony</displayName>
      <steamWorkshopUrl>steam://url/CommunityFilePage/2009463077</steamWorkshopUrl>
    </li>
  </modDependencies>
  <loadAfter>
    <li>brrainz.harmony</li>
    <li>ludeon.rimworld</li>
  </loadAfter>
  <description>XCOM-style turn-based tactical combat for RimWorld.

Safe to add or remove mid-save (turn state is transient).

Icon art by Lorc (game-icons.net, CC BY 3.0).</description>
</ModMetaData>
""")

open(os.path.join(OUT, "1.6", "Defs", "TBC_KeyBindings.xml"), "w", encoding="utf-8", newline="").write("""<?xml version="1.0" encoding="utf-8" ?>
<Defs>

  <KeyBindingCategoryDef>
    <defName>TBC_Keys</defName>
    <label>Turn-Based Combat</label>
    <description>Turn-based mode keys.</description>
  </KeyBindingCategoryDef>

  <KeyBindingDef>
    <defName>TBC_EndTurn</defName>
    <label>end turn</label>
    <category>TBC_Keys</category>
    <defaultKeyCodeA>Return</defaultKeyCodeA>
    <defaultKeyCodeB>KeypadEnter</defaultKeyCodeB>
  </KeyBindingDef>

</Defs>
""")

open(os.path.join(OUT, "1.6", "Defs", "TBC_Jobs.xml"), "w", encoding="utf-8", newline="").write("""<?xml version="1.0" encoding="utf-8" ?>
<Defs>

  <!-- Turn mode's stop-drop-and-roll: visible, and certain -->
  <JobDef>
    <defName>TBC_BeatFlames</defName>
    <driverClass>TurnBasedCombat.JobDriver_TBC_BeatFlames</driverClass>
    <reportString>rolling out the flames.</reportString>
    <casualInterruptible>false</casualInterruptible>
  </JobDef>

</Defs>
""")

# persistent About assets (icon, preview) - generated by make_tbc_icon.py
# and kept in docs/standalone because this dist folder is wiped every build
ASSETS = os.path.join(ROOT, "docs", "standalone")
# Whitelist, not copy-all: docs/standalone also holds Workshop PAGE material
# (gallery screenshots, the pasted description) which must never ship inside
# the package - only these three files belong in About/.
ABOUT_SHIP = {"ModIcon.png", "Preview.png", "PublishedFileId.txt"}
if os.path.isdir(ASSETS):
    for name in os.listdir(ASSETS):
        if name not in ABOUT_SHIP:
            continue
        shutil.copyfile(os.path.join(ASSETS, name), os.path.join(OUT, "About", name))
        print(f"About asset: {name}")

# The Workshop item id: written by RimWorld's uploader on FIRST publish into
# the INSTALLED copy's About folder. Losing it makes the next upload create a
# second Workshop item, so carry it from wherever it survives - the durable
# home is docs/standalone (covered by the asset copy above); this fallback
# rescues it from the Mods install after a first upload.
id_dst = os.path.join(OUT, "About", "PublishedFileId.txt")
if not os.path.exists(id_dst):
    installed_id = os.path.join(r"X:\SteamLibrary\steamapps\common\RimWorld\Mods",
                                "TurnBasedCombat", "About", "PublishedFileId.txt")
    if os.path.exists(installed_id):
        shutil.copyfile(installed_id, id_dst)
        shutil.copyfile(installed_id, os.path.join(ASSETS, "PublishedFileId.txt"))
        print("carried Workshop id from the installed copy (and saved to docs/standalone)")

# gizmo icons the engine looks up by (rewritten) path
for src_rel, dst_rel in [
    (os.path.join("Textures", "UI", "TSC_Abilities", "TSC_SneakHood.png"),
     os.path.join("Textures", "UI", "TBC_Abilities", "TBC_SneakHood.png")),
    (os.path.join("Textures", "UI", "TSC_Move.png"),
     os.path.join("Textures", "UI", "TBC_Move.png")),
]:
    src = os.path.join(ROOT, src_rel)
    if os.path.exists(src):
        shutil.copyfile(src, os.path.join(OUT, dst_rel))
    else:
        print(f"note: {src_rel} not found; gizmo falls back to a vanilla icon")

# ---------------------------------------------------------------- build
print("building TurnBasedCombat.dll ...")
r = subprocess.run(["dotnet", "build", os.path.join(GEN, "TurnBasedCombat.csproj"),
                    "-c", "Release", "-v", "quiet", "-nologo"],
                   capture_output=True, text=True)
if r.returncode != 0:
    print(r.stdout[-3000:])
    print(r.stderr[-2000:])
    sys.exit(1)

dll = os.path.join(OUT, "1.6", "Assemblies", "TurnBasedCombat.dll")
if not os.path.exists(dll):
    sys.exit("build succeeded but DLL not found at " + dll)
size = os.path.getsize(dll) // 1024
total = sum(os.path.getsize(os.path.join(dp, f))
            for dp, _, fns in os.walk(OUT) for f in fns)
print(f"TurnBasedCombat.dll: {size} KB")
print(f"mod folder: {os.path.relpath(OUT, ROOT)} ({total // 1024} KB total)")
if not os.path.exists(os.path.join(OUT, "About", "Preview.png")):
    print("NOTE: no Preview.png - Steam shows a blank tile without one.")
