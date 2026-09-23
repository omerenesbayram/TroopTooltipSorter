# Troop Tooltip Sorter

A Mount & Blade II: Bannerlord mod. When you hold **ALT** over a party or army on the campaign
map, the detailed troop breakdown is sorted by headcount (largest stack first) instead of raw
roster order.

Ties are broken by troop tier, elites first, then by name so the order never jitters between
tooltip refreshes.

Built against **Bannerlord v1.4.8**.

## How it works

`TooltipRefresherCollection.AddPartyTroopProperties` is the private helper that every party,
army, garrison and prisoner tooltip funnels through. A Harmony **prefix** swaps its `troopRoster`
argument for a sorted copy, and vanilla then renders the list in that order.

The copy is a throwaway used only for rendering — the party's real roster is never touched, so
there is no gameplay effect. If sorting ever throws, the prefix logs once and falls through to the
vanilla order rather than breaking the tooltip.

Because all of those tooltips share the one helper, garrison and prisoner lists get sorted too.

## Layout

| Path | What |
|---|---|
| `src/TroopTooltipSorter/` | The C# project |
| `src/TroopTooltipSorter/SubModule.cs` | Entry point; applies the Harmony patches |
| `src/TroopTooltipSorter/Patches/` | The Harmony patches |
| `module/SubModule.xml` | Module manifest deployed to the game |
| `Directory.Build.props` | Game path and module name |

## Building

```
dotnet build src/TroopTooltipSorter/TroopTooltipSorter.csproj -c Release
```

The build copies the DLL and `SubModule.xml` straight into
`<Bannerlord>/Modules/TroopTooltipSorter/`, so a successful build is immediately playable.

If the game is not at the default Steam location, override the path:

```
dotnet build src/TroopTooltipSorter/TroopTooltipSorter.csproj -c Release -p:BannerlordDir="D:\Games\Bannerlord"
```

Pass `-p:SkipDeploy=true` to build without touching the game folder.

## Requirements

- .NET SDK 8 (builds the `net472` target; no Visual Studio needed)
- The `Bannerlord.Harmony` module installed in the game

## Testing it

1. Build.
2. Enable **Troop Tooltip Sorter** in the launcher, below Harmony.
3. Load a campaign. A green "Troop Tooltip Sorter loaded." message confirms the module started.
4. Hover a party on the map and hold **ALT**. The troop list should run largest stack first.

## Naval DLC

Naval DLC ships its own `NavalTooltipRefresherCollection` and re-registers the **settlement**
tooltip to point at it, so patching only the TaleWorlds type leaves castle and town garrisons
unsorted. The mod therefore scans loaded assemblies for every copy of `AddPartyTroopProperties`
and patches each one, matching on the exact parameter signature. Naval DLC is not a dependency:
without it the scan simply finds nothing.

## Troop quality in the summary

The "Troops" block summarises a roster by formation class (Infantry, Ranged, Cavalry, Horse
Archer), which shows an army's shape but nothing about its quality - 200 infantry of recruits and
200 of veterans read alike. Each of those rows is split in two by troop tier:

```
Infantry T4+      13
Infantry T3-     159
Ranged T4+        38
Ranged T3-       137
Cavalry T4+        6
Cavalry T3-        5
Horse Archer T4+  36
Horse Archer T3-  31
```

`T4+` is tier 4 and above, `T3-` everything below. Tier is the troop's tier as the game shows it,
not `CharacterObject.Level`, which is on the hero-level scale and would make a cutoff of 4
meaningless.

A bucket with nobody in it is omitted rather than printed as zero. The totals are recomputed from
the same roster vanilla used, so each pair still adds up to the single number vanilla showed. The
suffixes are appended to whatever label the game produced, so this stays correct in any language.

## Packaging a release

```
powershell -ExecutionPolicy Bypass -File pack.ps1
```

Builds Release without touching the installed copy and writes
`dist/TroopTooltipSorter-v<version>.zip`, holding the module folder at the zip root:

```
TroopTooltipSorter/SubModule.xml
TroopTooltipSorter/bin/Win64_Shipping_Client/TroopTooltipSorter.dll
```

Extract that into the game's `Modules` folder, or hand the zip to a mod manager. The version comes
from `module/SubModule.xml`, so bump it there (and in the `.csproj`) before packing.
