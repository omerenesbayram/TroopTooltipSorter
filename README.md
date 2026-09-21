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
