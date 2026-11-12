# Troop Tooltip Sorter

A Mount & Blade II: Bannerlord mod that makes campaign-map tooltips readable at a glance.

Vanilla lists troops in whatever order they happen to sit in the roster, and clips the list after
a fixed number of lines — so the stacks that decide a fight can fall off the bottom while a single
scout sits at the top. This mod sorts those lists by headcount and adds a little more detail about
what you are actually looking at.

Everything it adds appears only while **ALT** is held. Without ALT, tooltips look exactly as they
always did.

Built against **Bannerlord v1.4.8**.

---

## Features

### 1. Troop lists sorted by headcount

Hold **ALT** over a party, army, settlement or garrison and the troop breakdown runs largest stack
first, instead of raw roster order.

Ties break by troop tier, elites ahead of chaff, then by name — that last step matters, because
without it the order would jitter as the tooltip redraws. Heroes stay in their own block at the
top, where vanilla puts the party leader first.

This covers party, army, garrison and prisoner lists.

### 2. Troop quality in the Troops summary

The Troops block summarises a roster by formation class, which tells you an army's shape but
nothing about its quality — 200 infantry of recruits and 200 of veterans read alike.

Holding **ALT** splits each row by troop tier:

```
Infantry T4+       13
Infantry T3-      159
Ranged T4+         38
Ranged T3-        137
Cavalry T4+         6
Cavalry T3-         5
Horse Archer T4+   36
Horse Archer T3-   31
```

`T4+` is tier 4 and above, `T3-` everything below. Without ALT you get vanilla's single number per
class, so this is detail you ask for rather than clutter you always carry.

Each pair still adds up to the number vanilla showed. An empty bucket is left out rather than
printed as a zero, and the suffixes are appended to whatever label the game produced, so this stays
correct in any language.

### 3. Siege and battle tooltips

Sieges and field battles use a different tooltip — the two-column attacker-versus-defender layout.
Holding **ALT** sorts each column by headcount, independently of the other.

It also fixes something misleading in vanilla. Each row's number was **the first party's count, not
the side's**. For a two-party garrison nobody notices, but a fourteen-party besieging army would
report a few hundred troops against a stated total of 1828. Rows now show the **whole side's** count
for that troop, so the column adds up to the number in the header.

Counts stay live as a battle resolves, the same way vanilla's do.

---

## Requirements

- Mount & Blade II: Bannerlord **v1.4.8**
- [Bannerlord.Harmony](https://www.nexusmods.com/mountandblade2bannerlord/mods/2006)

Naval DLC is **supported but not required** — see [Compatibility](#compatibility).

## Installation

Drop the `TroopTooltipSorter` folder into your game's `Modules` folder, so you end up with:

```
Mount & Blade II Bannerlord/
└── Modules/
    └── TroopTooltipSorter/
        ├── SubModule.xml
        └── bin/Win64_Shipping_Client/TroopTooltipSorter.dll
```

Then enable **Troop Tooltip Sorter** in the launcher, ordered below Harmony. A green
"Troop Tooltip Sorter loaded." message on the main menu confirms it started.

Safe to add or remove mid-campaign: it only changes how tooltips are drawn and never touches a
party's real roster, so nothing is written to your save.

## Compatibility

Naval DLC ships its **own copy** of the tooltip builder and re-points settlement tooltips at it,
which is why patching only the TaleWorlds one leaves castle and town garrisons unsorted. Rather
than depend on the DLC, the mod scans loaded assemblies for every copy of that method and patches
each, matching on exact parameter signature. Without Naval DLC installed the scan simply finds
nothing.

Every patch fails safe. If one cannot do its job it reports once to the game log and lets vanilla
draw the tooltip, rather than breaking something that redraws constantly.

Because the patches hook methods TaleWorlds never promised to keep, a future game update could stop
them working. They would quietly do nothing rather than crash.

## Building from source

Requires the **.NET SDK 8**. Visual Studio is not needed — the `net472` reference assemblies come
from NuGet.

```
dotnet build src/TroopTooltipSorter/TroopTooltipSorter.csproj -c Release
```

A successful build copies the DLL and `SubModule.xml` straight into the game, so building is also
deploying. Useful switches:

| Switch | Effect |
|---|---|
| `-p:SkipDeploy=true` | Build without touching the game folder |
| `-p:BannerlordDir="D:\..."` | Point at a non-default install |

The game locks the deployed DLL, so close Bannerlord and its launcher before building.

To produce a release zip:

```
powershell -ExecutionPolicy Bypass -File pack.ps1
```

## How it works

Harmony patches on two vanilla tooltip builders, both on `TooltipRefresherCollection`.

`AddPartyTroopProperties` backs party, army, garrison and prisoner tooltips. A **prefix** hands it a
sorted copy of the roster and lets vanilla render that; a **postfix** rewrites the formation-summary
rows into tier buckets. The sorted roster is a throwaway used only for drawing — the real one is
never touched.

`AddEncounterParties` backs siege and battle tooltips. It renders from a local, so there is no
argument to swap. Instead the patch rebuilds each side from the parties it was handed, works out the
display order, and rewrites the finished rows. Each row carries one closure per column, so the two
columns are handled independently — sorting the attackers must not reorder the defenders alongside
them.

| Path | What |
|---|---|
| `src/TroopTooltipSorter/Patches/` | The Harmony patches |
| `src/TroopTooltipSorter/SubModule.cs` | Entry point |
| `module/SubModule.xml` | Module manifest deployed to the game |
| `pack.ps1` | Builds a release zip |
| `CLAUDE.md` | Notes for working on the code |
