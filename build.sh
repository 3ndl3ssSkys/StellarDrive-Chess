#!/usr/bin/env bash
# Build + deploy the StellarDrive DEMO mods (the Sep-2026 demo build in this
# folder - Unity 6000.3, save format v15, FishNet networking).
# Run from Git Bash. The game MUST be closed (MelonLoader locks Mods/*.dll).
#
#   bash _Mods/build.sh               # build + deploy every mod
#   bash _Mods/build.sh graves        # one of: graves chess perf tweaks harness
#                                     #   zoom unlock ship modlist pick   (the Demo* mods)
#                                     #   demo = all five Demo* mods
#                                     #   server = DedicatedServer (not in the default set)
#                                     #   rails = SafetyRails (not in the default set)
#                                     #   solar = SolarPanels (not in the default set)
#                                     #   station = StationModels (not in the default set)
#                                     #   lighting = StationLighting (Krunk's; never deployed)
#   bash _Mods/build.sh --no-deploy   # compile only (safe while the game runs)
#
# Each mod is its own DLL. Shared helpers live in src/Common and are compiled
# INTO each mod under that mod's own namespace (see Common/*.cs, the #if block
# at the top), so no mod depends on another and no two mods ever share a type
# name - FishNet derives broadcast ids from type names, so a shared message
# type would have two mods reading each other's traffic.

set -u
export PATH="$PATH:/c/Program Files/Mono/bin"

DEV="$(cd "$(dirname "$0")" && pwd)"
GAME="$(cd "$DEV/.." && pwd)"
M="$GAME/StellarDrive_Data/Managed"
ML="$GAME/MelonLoader/net35"
SRC="$DEV/src"
OUT="$DEV/out"
mkdir -p "$OUT"

DEPLOY=1
TARGETS=()
for a in "$@"; do
  case "$a" in
    --no-deploy) DEPLOY=0 ;;
    *) TARGETS+=("$a") ;;
  esac
done
[ ${#TARGETS[@]} -eq 0 ] && TARGETS=(graves chess perf tweaks momentum harness)

if [ $DEPLOY -eq 1 ] && tasklist 2>/dev/null | grep -qiE "^StellarDrive"; then
  echo "!! StellarDrive is RUNNING - close it first (MelonLoader locks Mods/*.dll),"
  echo "   or pass --no-deploy to compile only."
  exit 1
fi

# Every managed assembly the game ships, so a mod can touch any system without
# the build script needing to know which. mcs only records the ones actually
# used, so this costs nothing at runtime.
REFS=()
for f in "$M"/*.dll; do
  REFS+=("-r:$f")
done
REFS+=("-r:$ML/MelonLoader.dll" "-r:$ML/0Harmony.dll")

COMMON=(-target:library -nostdlib -noconfig -langversion:9.0 -optimize+ -debug- -nowarn:0618,0649,0169,0414,0219,0162,8632,0436 "${REFS[@]}")

fail=0

build_one() {   # target  out-file  define  src-dir
  local key="$1" outfile="$2" define="$3" dir="$4"
  echo "== $key -> $outfile =="
  rm -f "$OUT/$outfile"
  local files=()
  while IFS= read -r -d '' f; do files+=("$f"); done < <(find "$SRC/Common" "$SRC/$dir" -name '*.cs' -print0)
  # pictures in the mod's Art folder go inside the DLL, as <dir>.<file name>
  local res=()
  if [ -d "$SRC/$dir/Art" ]; then
    while IFS= read -r -d '' f; do res+=("-resource:$f,$dir.$(basename "$f")"); done < <(find "$SRC/$dir/Art" -name '*.png' -print0)
  fi
  if ! csc -nologo "${COMMON[@]}" -define:"$define" "${res[@]}" -out:"$OUT/$outfile" "${files[@]}"; then
    fail=1; echo "!! $key FAILED"; return 1
  fi
  if [ $DEPLOY -eq 1 ]; then
    cp "$OUT/$outfile" "$GAME/Mods/$outfile" && echo "   deployed $outfile"
  fi
}

# The Demo* mods (Zoom, Unlock All, Ship Tools, Mod List, Build Pick) came
# over from the playtest build. They are standalone: of Common they take only
# ModKeys.cs (the "Mods" section of the Keybindings page), and only the ones
# with a key - under their own namespace, like every other mod.
build_plain() {   # target  name  [define]
  local key="$1" name="$2" define="${3:-}"
  echo "== $key -> $name.dll =="
  rm -f "$OUT/$name.dll"
  local files=() extra=()
  while IFS= read -r -d '' f; do files+=("$f"); done < <(find "$SRC/$name" -name '*.cs' -print0)
  if [ -n "$define" ]; then files+=("$SRC/Common/ModKeys.cs"); extra=(-define:"$define"); fi
  if ! csc -nologo "${COMMON[@]}" "${extra[@]}" -out:"$OUT/$name.dll" "${files[@]}"; then
    fail=1; echo "!! $key FAILED"; return 1
  fi
  if [ $DEPLOY -eq 1 ]; then
    cp "$OUT/$name.dll" "$GAME/Mods/$name.dll" && echo "   deployed $name.dll"
  fi
}

for t in "${TARGETS[@]}"; do
  case "$t" in
    graves)  build_one graves  "Graves.dll"            MOD_GRAVES  Graves ;;
    chess)   build_one chess   "Chess.dll"             MOD_CHESS   Chess ;;
    perf)    build_one perf    "Performance+.dll"      MOD_PERF    PerformancePlus ;;
    tweaks)  # GameTweaks: two builds. out/GameTweaks.dll is the release (no probes, no TestBench);
             # out/test/GameTweaks.dll has them (GTTEST) and is what verify.py tests.
             echo "== tweaks -> GameTweaks.dll (+ test build in out/test) =="
             rm -f "$OUT/GameTweaks.dll" "$OUT/test/GameTweaks.dll"; mkdir -p "$OUT/test"
             tfiles=(); while IFS= read -r -d '' f; do tfiles+=("$f"); done < <(find "$SRC/Common" "$SRC/GameTweaks" -name '*.cs' -print0)
             rfiles=(); for f in "${tfiles[@]}"; do case "$(basename "$f")" in *Probe*.cs|TestBench.cs) ;; *) rfiles+=("$f") ;; esac; done
             tres=(); while IFS= read -r -d '' f; do tres+=("-resource:$f,GameTweaks.$(basename "$f")"); done < <(find "$SRC/GameTweaks/Art" -name '*.png' -print0 2>/dev/null)
             if ! csc -nologo "${COMMON[@]}" -define:MOD_TWEAKS "${tres[@]}" -out:"$OUT/GameTweaks.dll" "${rfiles[@]}"; then fail=1; echo "!! tweaks FAILED"
             elif ! csc -nologo "${COMMON[@]}" -define:MOD_TWEAKS -define:GTTEST "${tres[@]}" -out:"$OUT/test/GameTweaks.dll" "${tfiles[@]}"; then fail=1; echo "!! tweaks (test build) FAILED"
             elif [ $DEPLOY -eq 1 ]; then cp "$OUT/GameTweaks.dll" "$GAME/Mods/GameTweaks.dll" && echo "   deployed GameTweaks.dll"; fi ;;
    momentum) build_one momentum "Momentum.dll"       MOD_MOMENTUM Momentum ;;
    cameras) build_one cameras "ExtraCameras.dll"      MOD_CAMERAS ExtraCameras ;;
    gravity) build_one gravity "Gravity.dll"           MOD_GRAVITY Gravity ;;
    rails)   build_one rails   "SafetyRails.dll"       MOD_RAILS   SafetyRails ;;
    solar)   build_one solar   "SolarPanels.dll"       MOD_SOLAR   SolarPanels ;;
    station) build_one station "StationModels.dll"     MOD_STATION StationModels ;;
    harness) build_one harness "DemoVerifyHarness.dll" MOD_HARNESS Harness ;;
    lighting) # StationLighting: Krunk's mod (decompiled, fixed). Standalone - no Common. Needs SDModKit +
             # StationKit, referenced from out/ (copies of the ones in Mods/). Two builds, like fixes:
             # out/StationLighting.dll is the release; out/test/ adds the probes (SLTEST), which verify.py prefers.
             echo "== lighting -> StationLighting.dll (+ test build) =="
             rm -f "$OUT/StationLighting.dll" "$OUT/test/StationLighting.dll"; mkdir -p "$OUT/test"
             files=(); while IFS= read -r -d '' f; do files+=("$f"); done < <(find "$SRC/StationLighting" \( -path "$SRC/StationLighting/Shaders" -o -path "$SRC/StationLighting/Original" -o -path "$SRC/StationLighting/tools" \) -prune -o -name '*.cs' -print0)
             lres=(-r:"$OUT/SDModKit.dll" -r:"$OUT/StationKit.dll"
                   -resource:"$SRC/StationLighting/Resources/stationlighting.json,stationlighting.json"
                   -resource:"$SRC/StationLighting/Resources/stationlight.bundle,stationlight.bundle")
             [ -f "$SRC/StationLighting/Resources/stationlighting-relight.bundle" ] && lres+=(-resource:"$SRC/StationLighting/Resources/stationlighting-relight.bundle,stationlighting-relight.bundle")
             if ! csc -nologo "${COMMON[@]}" "${lres[@]}" -out:"$OUT/StationLighting.dll" "${files[@]}"; then fail=1; echo "!! lighting FAILED"
             elif ! csc -nologo "${COMMON[@]}" -define:SLTEST "${lres[@]}" -out:"$OUT/test/StationLighting.dll" "${files[@]}" "$SRC/Common/ModCommon.cs" "$SRC/Common/TestBench.cs"; then fail=1; echo "!! lighting (test build) FAILED"
             elif [ $DEPLOY -eq 1 ]; then echo "   (lighting is never deployed by build.sh - Krunk's mod; Ethan decides when it replaces Mods/StationLighting.dll)"; fi ;;
    fixes)   # NecessaryFixes: two builds, like server. out/NecessaryFixes.dll is the release (no probes,
             # no host-and-guest test); out/test/NecessaryFixes.dll has them (NFTEST) and is what verify.py tests.
             # Its Common copy is the plain SDMod.Common namespace (no MOD_ define; no other mod uses it).
             echo "== fixes -> NecessaryFixes.dll (+ test build in out/test) =="
             rm -f "$OUT/NecessaryFixes.dll" "$OUT/test/NecessaryFixes.dll"; mkdir -p "$OUT/test"
             files=(); while IFS= read -r -d '' f; do files+=("$f"); done < <(find "$SRC/Common" "$SRC/NecessaryFixes" -name '*.cs' -not -name '*.py' -print0)
             if ! csc -nologo "${COMMON[@]}" -out:"$OUT/NecessaryFixes.dll" "${files[@]}"; then fail=1; echo "!! fixes FAILED"
             elif ! csc -nologo "${COMMON[@]}" -define:NFTEST -out:"$OUT/test/NecessaryFixes.dll" "${files[@]}"; then fail=1; echo "!! fixes (test build) FAILED"
             elif [ $DEPLOY -eq 1 ]; then cp "$OUT/NecessaryFixes.dll" "$GAME/Mods/NecessaryFixes.dll" && echo "   deployed NecessaryFixes.dll"; fi ;;
    vr)      # StellarDriveVR: two builds, like fixes. out/StellarDriveVR.dll is the release; out/test/ adds the
             # probes (SDVRTEST), which verify.py prefers. Unsafe code (OpenXR interop). The test OpenXR runtime
             # (out/test/vrmock) is built by src/StellarDriveVR/MockRuntime/build_mock.bat.
             echo "== vr -> StellarDriveVR.dll (+ test build in out/test) =="
             rm -f "$OUT/StellarDriveVR.dll" "$OUT/test/StellarDriveVR.dll"; mkdir -p "$OUT/test"
             files=(); while IFS= read -r -d '' f; do files+=("$f"); done < <(find "$SRC/Common" "$SRC/StellarDriveVR" -name '*.cs' -not -path '*/Test/*' -print0)
             tfiles=(); while IFS= read -r -d '' f; do tfiles+=("$f"); done < <(find "$SRC/Common" "$SRC/StellarDriveVR" -name '*.cs' -print0)
             if ! csc -nologo "${COMMON[@]}" -unsafe -define:MOD_VR -out:"$OUT/StellarDriveVR.dll" "${files[@]}"; then fail=1; echo "!! vr FAILED"
             elif ! csc -nologo "${COMMON[@]}" -unsafe -define:MOD_VR -define:SDVRTEST -out:"$OUT/test/StellarDriveVR.dll" "${tfiles[@]}"; then fail=1; echo "!! vr (test build) FAILED"
             elif [ $DEPLOY -eq 1 ]; then cp "$OUT/StellarDriveVR.dll" "$GAME/Mods/StellarDriveVR.dll" && echo "   deployed StellarDriveVR.dll"; fi ;;
    quiet)   build_plain quiet   DemoVerifyQuiet ;;   # a PLUGIN: verify.py puts it in Plugins/, not Mods/
    zoom)    build_plain zoom    DemoZoom      MOD_DZOOM ;;
    unlock)  build_plain unlock  DemoUnlockAll ;;
    ship)    build_plain ship    DemoShipTools MOD_DSHIP ;;
    modlist) build_plain modlist DemoModList ;;
    pick)    build_plain pick    DemoBuildPick MOD_DPICK ;;
    server)  # DedicatedServer: standalone, no Common, and not its Manager/ app (built by `manager`).
             # Two builds: out/DedicatedServer-<version>.dll is the release (no probes, no test commands);
             # out/test/DedicatedServer.dll has them (DSTEST) and is what verify.py tests.
             DSVER=$(grep -o 'Version = "[0-9.]*"' "$SRC/DedicatedServer/DedicatedServerMod.cs" | head -1 | grep -o '[0-9][0-9.]*')
             DSREL="DedicatedServer-$DSVER.dll"
             echo "== server -> $DSREL (+ test build in out/test) =="
             rm -f "$OUT"/DedicatedServer*.dll "$OUT/test/DedicatedServer.dll"; mkdir -p "$OUT/test"
             files=(); while IFS= read -r -d '' f; do files+=("$f"); done < <(find "$SRC/DedicatedServer" -name '*.cs' -not -path '*/Manager/*' \
                 -not -name 'Probe*.cs' -not -name 'Test.cs' -not -name 'TestLedger.cs' -not -name 'ShipTest.cs' -print0)
             tfiles=(); while IFS= read -r -d '' f; do tfiles+=("$f"); done < <(find "$SRC/DedicatedServer" -name '*.cs' -not -path '*/Manager/*' -print0)
             if ! csc -nologo "${COMMON[@]}" -out:"$OUT/$DSREL" "${files[@]}"; then fail=1; echo "!! server FAILED"
             elif ! csc -nologo "${COMMON[@]}" -define:DSTEST -out:"$OUT/test/DedicatedServer.dll" "${tfiles[@]}"; then fail=1; echo "!! server (test build) FAILED"
             elif [ $DEPLOY -eq 1 ]; then
               # any older copy goes (two copies of one mod both load)
               for old in "$GAME/Mods"/DedicatedServer*.dll; do [ -f "$old" ] && [ "$(basename "$old")" != "$DSREL" ] && rm -f "$old" && echo "   removed $(basename "$old")"; done
               cp "$OUT/$DSREL" "$GAME/Mods/$DSREL" && echo "   deployed $DSREL"
             fi ;;
    manager) # the Server Manager app (.NET 8 WinForms) -> _Mods/out/ServerManager/ (release) and
             # _Mods/out/ServerManager-test/ (with the --selftest the harness drives), each with its DedicatedServer.dll
             echo "== manager -> out/ServerManager/StellarDriveServerManager.exe (+ out/ServerManager-test) =="
             if ! dotnet publish "$SRC/DedicatedServer/Manager/StellarDriveServerManager.csproj" -c Release -r win-x64 --self-contained false \
                  -o "$OUT/ServerManager" -nologo -v q; then fail=1; echo "!! manager FAILED"
             elif ! dotnet publish "$SRC/DedicatedServer/Manager/StellarDriveServerManager.csproj" -c Release -r win-x64 --self-contained false \
                  -p:DefineConstants=SDSMTEST -p:IntermediateOutputPath=obj/test/ -o "$OUT/ServerManager-test" -nologo -v q; then fail=1; echo "!! manager (test build) FAILED"
             else
               rm -f "$OUT/ServerManager"/DedicatedServer*.dll
               for rel in "$OUT"/DedicatedServer-*.dll; do [ -f "$rel" ] && cp "$rel" "$OUT/ServerManager/"; done
               [ -f "$OUT/test/DedicatedServer.dll" ] && cp "$OUT/test/DedicatedServer.dll" "$OUT/ServerManager-test/DedicatedServer.dll"
             fi ;;
    demo)    build_plain zoom DemoZoom MOD_DZOOM; build_plain unlock DemoUnlockAll; build_plain ship DemoShipTools MOD_DSHIP
             build_plain modlist DemoModList; build_plain pick DemoBuildPick MOD_DPICK ;;
    *) echo "unknown target '$t' (graves|chess|perf|tweaks|momentum|cameras|gravity|rails|solar|station|lighting|harness|fixes|zoom|unlock|ship|modlist|pick|server|manager|demo)"; exit 2 ;;
  esac
done

exit $fail
