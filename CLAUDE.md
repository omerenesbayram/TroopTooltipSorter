# CLAUDE.md

A Mount & Blade II: Bannerlord mod that improves the campaign-map party/army tooltip. See
[README.md](README.md) for what it does from a player's point of view.

## Environment

| | |
|---|---|
| Game | `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord` |
| Game version | **v1.4.8**, running on **.NET Framework 4.7.2** (not .NET Core) |
| Deploy target | `<game>\Modules\TroopTooltipSorter` |
| Build | .NET SDK 8. No Visual Studio - `net472` reference assemblies come from NuGet |
| Decompiler | `%USERPROFILE%\.dotnet\tools\ilspycmd.exe` |

Also installed: VS Code + C# extension, Git, Python 3.14. The game already has Harmony,
ButterLib, UIExtenderEx, MCM and BLSE as modules, plus **Naval DLC**, which matters (below).

## Build

```
dotnet build src/TroopTooltipSorter/TroopTooltipSorter.csproj -c Release
```

A successful build copies the DLL and `SubModule.xml` straight into the game, so building *is*
deploying. `-p:SkipDeploy=true` builds without touching the game; `-p:BannerlordDir="..."`
relocates the game. Paths live in [Directory.Build.props](Directory.Build.props).

**The game locks the deployed DLL.** If Bannerlord or its launcher is running, the build compiles
but the copy fails with MSB3021/MSB3027. Close the game, then rebuild.

## Architecture

Everything hangs off one vanilla method,
`TooltipRefresherCollection.AddPartyTroopProperties(propertyBasedTooltipVM, troopRoster, title, isInspected, funcToDoBeforeLambda)`.
Every party, army, garrison and prisoner tooltip funnels through it.

- [SortTroopTooltipPatch](src/TroopTooltipSorter/Patches/SortTroopTooltipPatch.cs) - **prefix**.
  Swaps `troopRoster` for a sorted copy and lets vanilla render it. The copy is throwaway and used
  only for display, so there is no gameplay effect.
- [SplitSummaryByTierPatch](src/TroopTooltipSorter/Patches/SplitSummaryByTierPatch.cs) - **postfix**.
  Rewrites the finished formation-summary rows in `propertyBasedTooltipVM.TooltipPropertyList`,
  matching rows by the exact label vanilla generated. Gated on `IsExtended` (ALT held).
- [TooltipBuilderPatcher](src/TroopTooltipSorter/Patches/TooltipBuilderPatcher.cs) - finds *other*
  loaded copies of that method and applies both patches to them.

### Naval DLC ships a second copy of the tooltip builder

`NavalDLC.View.NavalTooltipRefresherCollection` (in `NavalDLC.ViewModelCollection.dll`) has its own
`AddPartyTroopProperties`, and `NavalDLC.View` re-registers the **settlement** tooltip to point at
it. Patching only the TaleWorlds type leaves castle and town garrisons untouched, which cost real
debugging time. `TooltipBuilderPatcher` discovers copies by name plus exact parameter signature, so
Naval DLC is not a hard dependency.

## Things that bite

- **Do not patch `RefreshSettlementTooltip` / `RefreshMobilePartyTooltip` / `RefreshArmyTooltip`.**
  `SandBox.View` registers them as delegates at startup via `InformationManager.RegisterTooltip`,
  and patching them crashes the game during save loading. This was diagnosed by elimination, not
  from a stack trace - do not reintroduce them casually.
- **Troop quality is `CharacterObject.Tier`** (1-6), *not* `.Level`, which is on the hero-level
  scale.
- Patches must fail safe. Each one wraps its work in try/catch, logs once via `Debug.PrintError`,
  and falls through to vanilla behaviour rather than breaking a tooltip that redraws every frame.
- Tooltips rebuild constantly, so never log per call.

## Investigating vanilla behaviour

The game ships as compiled DLLs. To read a method:

```
& "$env:USERPROFILE\.dotnet\tools\ilspycmd.exe" `
    "<game>\bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.ViewModelCollection.dll" `
    -r "<game>\bin\Win64_Shipping_Client" `
    -t "TaleWorlds.CampaignSystem.ViewModelCollection.TooltipRefresherCollection"
```

Write output to a **short path** - ILSpy silently produces nothing when the output path exceeds
Windows' limit. PowerShell reflection with an `AssemblyResolve` handler over the game's `bin` and
each module's `bin` is the quickest way to confirm a type name or signature before writing a patch.

`strings` is **not** available in this Git Bash; use `grep -a -o` on binaries instead.

## Verifying a change

There are no automated tests - the only real verification is in-game, and it costs a game restart,
so make changes count. Crash logs are in `C:\ProgramData\Mount and Blade II Bannerlord\logs`
(`rgl_log_<pid>.txt`, `watchdog_log_<pid>.txt`). The `crashes\` folder is emptied once a report is
uploaded, so the dump is usually gone; comparing a working session's log against a crashing one is
more productive.
