using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace TroopTooltipSorter.Patches
{
    /// <summary>
    /// Sieges and field battles use a different builder from the ordinary party tooltip:
    /// AddEncounterParties, which lays the two sides out side by side, one row per rank with the
    /// attacker on the left and the defender on the right. Its troop list is unsorted for the same
    /// reason the party one was.
    ///
    /// The roster it renders from is a local, so there is no argument to swap the way
    /// <see cref="SortTroopTooltipPatch"/> does. Instead we rebuild both side rosters the way
    /// vanilla did - the parties are parameters, so this is exact - work out what order each column
    /// should be in, and permute the finished rows.
    ///
    /// Each row holds two independent closures, one per column, so the columns are permuted
    /// separately: sorting the left must not drag the right along with it.
    /// </summary>
    internal static class EncounterTooltipSorter
    {
        private static readonly FieldInfo DefinitionFuncField = AccessTools.Field(typeof(TooltipProperty), "definitionFunc");
        private static readonly FieldInfo ValueFuncField = AccessTools.Field(typeof(TooltipProperty), "valueFunc");

        private static bool _loggedFailure;

        internal static void Reorder(PropertyBasedTooltipVM tooltip, TroopRoster left, TroopRoster right, bool isExtended)
        {
            // The per-troop rows only exist in the extended (ALT) layout.
            if (!isExtended || tooltip == null || left == null || right == null)
            {
                return;
            }

            if (DefinitionFuncField == null || ValueFuncField == null)
            {
                ReportOnce("TooltipProperty no longer exposes its label delegates.");
                return;
            }

            try
            {
                MBBindingList<TooltipProperty> rows = tooltip.TooltipPropertyList;
                int rowCount = Math.Max(left.Count, right.Count);
                if (rows == null || rowCount < 2 || rows.Count < rowCount + 1)
                {
                    return;
                }

                // Vanilla emits one row per rank and then closes the block with a BattleModeOver
                // row, so the ranks are the rowCount rows immediately before the last one. If that
                // does not hold, the layout changed under us - leave it alone.
                int last = rows.Count - 1;
                int start = last - rowCount;
                if (!HasFlag(rows[last], TooltipProperty.TooltipPropertyFlags.BattleModeOver))
                {
                    return;
                }

                int[] orderLeft = SortedOrder(left, rowCount);
                int[] orderRight = SortedOrder(right, rowCount);

                // Snapshot before writing: the permutation reads rows we are about to overwrite.
                var definitions = new Func<string>[rowCount];
                var values = new Func<string>[rowCount];
                for (int i = 0; i < rowCount; i++)
                {
                    definitions[i] = (Func<string>)DefinitionFuncField.GetValue(rows[start + orderLeft[i]]);
                    values[i] = (Func<string>)ValueFuncField.GetValue(rows[start + orderRight[i]]);
                }

                for (int i = 0; i < rowCount; i++)
                {
                    TooltipProperty row = rows[start + i];
                    DefinitionFuncField.SetValue(row, definitions[i]);
                    ValueFuncField.SetValue(row, values[i]);
                    row.RefreshDefinition();
                    row.RefreshValue();
                }
            }
            catch (Exception e)
            {
                ReportOnce($"could not sort the encounter tooltip. {e}");
            }
        }

        /// <summary>
        /// Rebuilds one side exactly as vanilla does: heroes first in encounter order, then troops,
        /// with the same cross-party merging. Reproducing it is what makes row index N here mean
        /// the same troop as row index N in the tooltip.
        /// </summary>
        internal static TroopRoster BuildSide(MBReadOnlyList<PartyBase> parties)
        {
            TroopRoster roster = TroopRoster.CreateDummyTroopRoster();
            int lastHeroIndex = 0;

            if (parties != null)
            {
                foreach (PartyBase party in parties)
                {
                    AddParty(roster, party, ref lastHeroIndex);
                }
            }

            return roster;
        }

        internal static TroopRoster BuildSide(MBReadOnlyList<MapEventParty> parties)
        {
            TroopRoster roster = TroopRoster.CreateDummyTroopRoster();
            int lastHeroIndex = 0;

            if (parties != null)
            {
                foreach (MapEventParty item in parties)
                {
                    PartyBase party = item?.Party;

                    // Vanilla leaves out parties whose strength you are not allowed to see.
                    MobileParty mobileParty = party?.MobileParty;
                    if (mobileParty != null && mobileParty.IsInfoHidden)
                    {
                        continue;
                    }

                    AddParty(roster, party, ref lastHeroIndex);
                }
            }

            return roster;
        }

        private static void AddParty(TroopRoster roster, PartyBase party, ref int lastHeroIndex)
        {
            TroopRoster members = party?.MemberRoster;
            if (members == null)
            {
                return;
            }

            for (int i = 0; i < members.Count; i++)
            {
                TroopRosterElement element = members.GetElementCopyAtIndex(i);
                if (element.Character == null)
                {
                    continue;
                }

                if (element.Character.IsHero)
                {
                    roster.AddToCounts(element.Character, element.Number, insertAtFront: false, element.WoundedNumber, 0, removeDepleted: true, lastHeroIndex);
                    lastHeroIndex++;
                }
                else
                {
                    roster.AddToCounts(element.Character, element.Number, insertAtFront: false, element.WoundedNumber);
                }
            }
        }

        /// <summary>
        /// Row indices for one column, in the order they should be displayed. Heroes keep vanilla's
        /// leading block; the troops behind them are sorted by headcount. Indices past this side's
        /// roster are vanilla's blank filler rows and stay where they are.
        /// </summary>
        private static int[] SortedOrder(TroopRoster roster, int rowCount)
        {
            var heroes = new List<int>();
            var troops = new List<int>();

            for (int i = 0; i < roster.Count; i++)
            {
                CharacterObject character = roster.GetElementCopyAtIndex(i).Character;
                if (character != null && character.IsHero)
                {
                    heroes.Add(i);
                }
                else
                {
                    troops.Add(i);
                }
            }

            troops.Sort((a, b) => Compare(roster.GetElementCopyAtIndex(a), roster.GetElementCopyAtIndex(b)));

            var order = new int[rowCount];
            int next = 0;

            foreach (int index in heroes)
            {
                order[next++] = index;
            }

            foreach (int index in troops)
            {
                order[next++] = index;
            }

            for (int i = roster.Count; i < rowCount; i++)
            {
                order[next++] = i;
            }

            return order;
        }

        private static int Compare(TroopRosterElement a, TroopRosterElement b)
        {
            // Descending headcount, then elites ahead of chaff, then name so the order never jitters.
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

        private static bool HasFlag(TooltipProperty row, TooltipProperty.TooltipPropertyFlags flag)
        {
            return row != null && (row.PropertyModifier & (int)flag) != 0;
        }

        private static void ReportOnce(string message)
        {
            if (_loggedFailure)
            {
                return;
            }

            _loggedFailure = true;
            Debug.PrintError($"{SubModule.ModuleName}: {message} Leaving the encounter tooltip in vanilla order.", Environment.StackTrace);
        }
    }

    /// <summary>Siege tooltips, which pass the involved parties directly.</summary>
    [HarmonyPatch(typeof(TooltipRefresherCollection), "AddEncounterParties",
        new[] { typeof(PropertyBasedTooltipVM), typeof(MBReadOnlyList<PartyBase>), typeof(MBReadOnlyList<PartyBase>), typeof(bool) })]
    internal static class SortSiegeTooltipPatch
    {
        private static void Postfix(
            PropertyBasedTooltipVM propertyBasedTooltipVM,
            MBReadOnlyList<PartyBase> parties1,
            MBReadOnlyList<PartyBase> parties2,
            bool isExtended)
        {
            if (!isExtended)
            {
                return;
            }

            EncounterTooltipSorter.Reorder(
                propertyBasedTooltipVM,
                EncounterTooltipSorter.BuildSide(parties1),
                EncounterTooltipSorter.BuildSide(parties2),
                isExtended);
        }
    }

    /// <summary>Field-battle tooltips, which pass map-event participants instead.</summary>
    [HarmonyPatch(typeof(TooltipRefresherCollection), "AddEncounterParties",
        new[] { typeof(PropertyBasedTooltipVM), typeof(MBReadOnlyList<MapEventParty>), typeof(MBReadOnlyList<MapEventParty>), typeof(bool) })]
    internal static class SortMapEventTooltipPatch
    {
        private static void Postfix(
            PropertyBasedTooltipVM propertyBasedTooltipVM,
            MBReadOnlyList<MapEventParty> parties1,
            MBReadOnlyList<MapEventParty> parties2,
            bool isExtended)
        {
            if (!isExtended)
            {
                return;
            }

            EncounterTooltipSorter.Reorder(
                propertyBasedTooltipVM,
                EncounterTooltipSorter.BuildSide(parties1),
                EncounterTooltipSorter.BuildSide(parties2),
                isExtended);
        }
    }
}
