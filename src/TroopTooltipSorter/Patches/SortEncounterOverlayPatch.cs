using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace TroopTooltipSorter.Patches
{
    /// <summary>
    /// The battle screen's troop-count tooltip does not go through the map tooltip machinery at
    /// all. <see cref="EncounterMenuOverlayVM"/> carries its own near-copy of the builder,
    /// assembles a finished <c>List&lt;TooltipProperty&gt;</c>, and hands that straight to the
    /// tooltip system - which is why patching the shared builder never reached it.
    ///
    /// So patch that method instead. Its rows are laid out predictably: a title, the
    /// formation-class summary, a "Troop Types" header, then hero rows and finally one row per
    /// non-hero troop in roster order. We rebuild the same roster from the parties the view model
    /// holds, reorder the trailing troop rows by headcount, and split the summary by tier.
    ///
    /// Unlike a map tooltip, this list is built once and covers both ALT states - each row decides
    /// for itself via its show-when-extended flag - so the tier split is added alongside the
    /// original row rather than replacing it.
    /// </summary>
    [HarmonyPatch(typeof(EncounterMenuOverlayVM), "GetEncounterSideTroopsTooltip")]
    internal static class SortEncounterOverlayPatch
    {
        private static bool _loggedFailure;

        private static void Postfix(EncounterMenuOverlayVM __instance, BattleSideEnum side, List<TooltipProperty> __result)
        {
            if (__instance == null || __result == null || __result.Count == 0)
            {
                return;
            }

            try
            {
                TroopRoster roster = BuildSideRoster(__instance, side);
                if (roster == null || roster.Count == 0)
                {
                    return;
                }

                // Sort first: splitting the summary inserts rows above the troop block and would
                // otherwise shift the indices we are about to reorder.
                SortTroopRows(__result, roster);
                TierSummarySplitter.Split(__result, TierSummarySplitter.TotalsByRowLabel(roster), gateOnExtended: true);
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Debug.PrintError($"{SubModule.ModuleName}: could not rebuild the encounter overlay tooltip, leaving it as vanilla. {e}", e.StackTrace);
                }
            }
        }

        /// <summary>
        /// Rebuilds the side's roster exactly as the view model did, so entry N here is the troop
        /// behind row N of the troop block.
        /// </summary>
        private static TroopRoster BuildSideRoster(EncounterMenuOverlayVM overlay, BattleSideEnum side)
        {
            MBBindingList<GameMenuPartyItemVM> sideList =
                side == BattleSideEnum.Attacker ? overlay.AttackerPartyList : overlay.DefenderPartyList;
            if (sideList == null)
            {
                return null;
            }

            TroopRoster roster = TroopRoster.CreateDummyTroopRoster();
            foreach (GameMenuPartyItemVM item in sideList)
            {
                PartyBase party = item?.Party;
                TroopRoster members = party?.MemberRoster;
                if (members == null)
                {
                    continue;
                }

                for (int i = 0; i < members.Count; i++)
                {
                    TroopRosterElement element = members.GetElementCopyAtIndex(i);
                    if (element.Character == null)
                    {
                        continue;
                    }

                    roster.AddToCounts(element.Character, element.Number, insertAtFront: false, element.WoundedNumber);
                }
            }

            return roster;
        }

        /// <summary>
        /// Reorders the trailing non-hero rows by headcount. Each row carries its own closure, so
        /// the rows themselves can simply be permuted - there is no second column to keep in step.
        /// </summary>
        private static void SortTroopRows(List<TooltipProperty> rows, TroopRoster roster)
        {
            var troopIndices = new List<int>();
            for (int i = 0; i < roster.Count; i++)
            {
                CharacterObject character = roster.GetElementCopyAtIndex(i).Character;
                if (character != null && !character.IsHero)
                {
                    troopIndices.Add(i);
                }
            }

            int count = troopIndices.Count;
            int start = rows.Count - count;
            if (count < 2 || start < 0)
            {
                return;
            }

            // Guard the layout assumption: row start+j must be the troop at troopIndices[j]. If the
            // view model ever changes shape, leave the tooltip alone rather than scramble it.
            for (int j = 0; j < count; j++)
            {
                CharacterObject character = roster.GetElementCopyAtIndex(troopIndices[j]).Character;
                if (rows[start + j]?.DefinitionLabel != character?.Name?.ToString())
                {
                    return;
                }
            }

            var order = new List<int>(count);
            for (int j = 0; j < count; j++)
            {
                order.Add(j);
            }

            order.Sort((a, b) => Compare(
                roster.GetElementCopyAtIndex(troopIndices[a]),
                roster.GetElementCopyAtIndex(troopIndices[b])));

            var original = new TooltipProperty[count];
            for (int j = 0; j < count; j++)
            {
                original[j] = rows[start + j];
            }

            for (int j = 0; j < count; j++)
            {
                rows[start + j] = original[order[j]];
            }
        }

        private static int Compare(TroopRosterElement a, TroopRosterElement b)
        {
            // Descending headcount, then elites ahead of chaff, then name so the order never
            // jitters between refreshes.
            int byCount = b.Number.CompareTo(a.Number);
            if (byCount != 0)
            {
                return byCount;
            }

            int byTier = (b.Character?.Tier ?? 0).CompareTo(a.Character?.Tier ?? 0);
            if (byTier != 0)
            {
                return byTier;
            }

            return string.CompareOrdinal(a.Character?.Name?.ToString(), b.Character?.Name?.ToString());
        }
    }
}
