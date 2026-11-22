# CLAUDE.md

A Mount & Blade II: Bannerlord mod that improves the campaign-map party/army tooltip. See
[README.md](README.md) for what it does from a player's point of view.

## Environment

| | |
|---|---|
| Game | `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord` |
| Game version | **v1.5.7**, running on **.NET Framework 4.7.2** (not .NET Core) |
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

Three vanilla builders. Two are on `TooltipRefresherCollection`; the third is on a view model and
bypasses the tooltip machinery entirely (see below).

`AddPartyTroopProperties(propertyBasedTooltipVM, troopRoster, title, isInspected, funcToDoBeforeLambda)`
backs every party, army, garrison and prisoner tooltip.

- [SortTroopTooltipPatch](src/TroopTooltipSorter/Patches/SortTroopTooltipPatch.cs) - **prefix**.
  Swaps `troopRoster` for a sorted copy and lets vanilla render it. The copy is throwaway and used
  only for display, so there is no gameplay effect.
- [SplitSummaryByTierPatch](src/TroopTooltipSorter/Patches/SplitSummaryByTierPatch.cs) - **postfix**.
  Rewrites the finished formation-summary rows in `propertyBasedTooltipVM.TooltipPropertyList`,
  matching rows by the exact label vanilla generated. Gated on `IsExtended` (ALT held).
- [TooltipBuilderPatcher](src/TroopTooltipSorter/Patches/TooltipBuilderPatcher.cs) - finds *other*
  loaded copies of that method and applies both patches to them.

`AddEncounterParties` backs siege and field-battle tooltips - the two-column attacker/defender
layout. It has **two overloads**, one taking `MBReadOnlyList<PartyBase>` (sieges) and one taking
`MBReadOnlyList<MapEventParty>` (field battles), so the patch attribute must specify argument
types. Naval DLC does *not* duplicate this one, so the discovery scan is not involved.

- [SortEncounterTooltipPatch](src/TroopTooltipSorter/Patches/SortEncounterTooltipPatch.cs) -
  **postfix** on both overloads. The roster it renders is a local, so there is no argument to swap.
  Instead it rebuilds each side's roster the way vanilla did (the parties are parameters, so this is
  exact), derives the display order, and permutes the finished rows by swapping the private
  `definitionFunc` / `valueFunc` delegates on `TooltipProperty` and calling `RefreshDefinition()` /
  `RefreshValue()`. Each row carries one closure per column, so the columns permute independently -
  sorting the left must not drag the right along. It locates the rank rows by counting back from the
  trailing `BattleModeOver` row, and bails if that shape does not hold.

`EncounterMenuOverlayVM.GetEncounterSideTroopsTooltip` backs the battle screen's troop counts. It
builds a finished `List<TooltipProperty>` itself, so neither patch above reaches it.

- [SortEncounterOverlayPatch](src/TroopTooltipSorter/Patches/SortEncounterOverlayPatch.cs) -
  **postfix**. Rebuilds the side's roster from the view model's own `AttackerPartyList` /
  `DefenderPartyList`, reorders the trailing troop rows, and splits the summary. It verifies row N
  really is the troop at roster position N before reordering, and bails if not. Because this list
  is built once for both ALT states, the tier rows are added *alongside* the original (which is
  flagged show-only-when-not-extended) rather than replacing it - that is what the `gateOnExtended`
  flag on [TierSummarySplitter](src/TroopTooltipSorter/Patches/TierSummarySplitter.cs) selects.

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

## Reflection into vanilla internals

`SortEncounterTooltipPatch` reads two private fields on `TooltipProperty` (`definitionFunc`,
`valueFunc`). These are resolved once via `AccessTools.Field` and null-checked - if a game update
renames them the patch reports once and leaves the tooltip in vanilla order rather than throwing.
Verify patch targets resolve before testing in-game: load the built DLL in PowerShell, read the
`HarmonyPatch` attributes, and resolve each `info.declaringType.GetMethod(...)`. That catches a bad
overload signature without costing a game restart.

### The encounter tooltip printed per-party counts, not side totals

`AddEncounterParties` picks which troops get a row from a merged per-side roster, but vanilla's
label delegate scans the party list and returns on the **first party containing that character** -
so the printed number was that one party's count. With a two-party defence the two coincide; across
a fourteen-party besieging army they diverge badly (the attacker column summed to a few hundred
against a stated 1828).

`SortEncounterTooltipPatch` now re-labels troop rows with the side-wide total, reusing vanilla's
text and localisation id (`{=vLaBJFGy}`) so only the number changes. Totals are recomputed inside
the delegate on every call, not captured, so they keep moving during a battle. Hero rows keep
vanilla's delegate untouched - they show a health percentage, and the two columns word it
differently.

## A game update requires a rebuild

When 1.4.8 went to 1.5.7, `AddPartyTroopProperties` was byte-identical, every patch still bound,
and nothing was logged - yet the tier split silently stopped working. Recompiling the unchanged
source against the new assemblies fixed it; no logic changed. A member the old binary referenced
must have moved or changed signature, so it threw at the point of use, and the `Debug.PrintError`
in the `catch` evidently threw too, which is why nothing reached any log.

**So after any game update, rebuild and redeploy before debugging anything.** A mod that loads and
half-works is not evidence that the binary is current.

## There are three troop-list builders, not one

Long-running confusion came from assuming `TooltipRefresherCollection` owned every troop list:

1. `TooltipRefresherCollection.AddPartyTroopProperties` - party, army, garrison, prisoner tooltips.
2. `TooltipRefresherCollection.AddEncounterParties` - siege and field-battle tooltips (two overloads).
3. `EncounterMenuOverlayVM.GetEncounterSideTroopsTooltip` - the battle screen's troop counts.

The third builds a finished `List<TooltipProperty>` and passes it to the tooltip system directly,
so no refresher runs for it. `InformationManager.ShowTooltip(Type, object[])` is the single funnel
every tooltip passes through, and a temporary prefix logging its `Type` argument is the quickest
way to identify an unknown tooltip - a hovered type of `List<TooltipProperty>` means the rows were
built elsewhere and there is no refresher to patch. That hook is safe; the refreshers are not.

## Searching assembly bytes: identifiers are UTF-8, string literals are UTF-16

`grep -a` finds type and method names, because those live in the `#Strings` heap as UTF-8. It will
**never** find a string literal such as `"str_troop_types"`, which lives in the `#US` heap as
UTF-16, and the empty result looks exactly like a genuine absence. Search literals by reading the
bytes and decoding with `[System.Text.Encoding]::Unicode`, or just decompile the assembly to a
short path and grep the source. Decompiling and grepping is what finally found the third builder -
two call sites in one assembly, where only the first had ever been looked at.
