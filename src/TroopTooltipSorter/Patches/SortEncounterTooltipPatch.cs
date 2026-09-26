using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace TroopTooltipSorter.Patches
{
    /// <summary>
    /// Sieges and field battles use a different builder from the ordinary party tooltip:
    /// AddEncounterParties, which lays the two sides out side by side, one row per rank with the
    /// attacker on the left and the defender on the right. This patch does two things to it.
    ///
    /// **Side-wide totals.** Vanilla picks which troops get a row from a merged per-side roster,
    /// but each row's label delegate scans the party list and returns on the first party holding
    /// that character - so the number printed is that one party's count. Across a fourteen-party
    /// besieging army the column's numbers sum to a few hundred against a stated 1828, which tells
    /// you very little. Troop rows are re-labelled with the whole side's count for that troop.
    ///
    /// **Sorting.** The rows are then ordered by that count, largest first.
    ///
    /// The roster vanilla renders from is a local, so there is no argument to swap the way
    /// <see cref="SortTroopTooltipPatch"/> does. Instead we rebuild each side from the parties we
    /// were handed - they are parameters, so the reconstruction is exact - and rewrite the finished
    /// rows. Each row carries one closure per column, so the columns are handled independently:
    /// sorting the attackers must not reorder the defenders alongside them.
    ///
    /// Hero rows are left exactly as vanilla wrote them. They show a health percentage rather than
    /// a count, there is nothing to total or sort, and the two columns word them differently.
    /// </summary>
    internal static class EncounterTooltipSorter
    {
        private static readonly FieldInfo DefinitionFuncField = AccessTools.Field(typeof(TooltipProperty), "definitionFunc");
        private static readonly FieldInfo ValueFuncField = AccessTools.Field(typeof(TooltipProperty), "valueFunc");

        private static bool _loggedFailure;

        internal static void Reorder(
            PropertyBasedTooltipVM tooltip,
            List<PartyBase> leftParties,
            List<PartyBase> rightParties,
            bool isExtended)
        {
            // The per-troop rows only exist in the extended (ALT) layout.
            if (!isExtended || tooltip == null || leftParties == null || rightParties == null)
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
                TroopRoster left = BuildSide(leftParties);
                TroopRoster right = BuildSide(rightParties);

                MBBindingList<TooltipProperty> rows = tooltip.TooltipPropertyList;
                int rowCount = Math.Max(left.Count, right.Count);
                if (rows == null || rowCount < 1 || rows.Count < rowCount + 1)
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

                // Resolve every label before writing any: the permutation reads rows we go on to
                // overwrite.
                var definitions = new Func<string>[rowCount];
                var values = new Func<string>[rowCount];
                for (int i = 0; i < rowCount; i++)
                {
                    definitions[i] = Label(rows, start, orderLeft[i], left, leftParties, DefinitionFuncField);
                    values[i] = Label(rows, start, orderRight[i], right, rightParties, ValueFuncField);
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
                ReportOnce($"could not rebuild the encounter tooltip. {e}");
            }
        }

        /// <summary>
        /// The label for one slot: a troop gets a fresh one reporting the whole side's count, while
        /// heroes and vanilla's blank filler rows keep the delegate vanilla built.
        /// </summary>
        private static Func<string> Label(
            MBBindingList<TooltipProperty> rows,
            int start,
            int index,
            TroopRoster roster,
            List<PartyBase> parties,
            FieldInfo field)
        {
            if (index < roster.Count)
            {
                CharacterObject character = roster.GetElementCopyAtIndex(index).Character;
                if (character != null && !character.IsHero)
                {
                    return () => RenderTotal(character, parties);
                }
            }

            return (Func<string>)field.GetValue(rows[start + index]);
        }

        /// <summary>
        /// Totals one troop across the whole side. Recomputed on each call rather than captured, so
        /// the numbers keep moving while a battle runs, exactly as vanilla's own labels do.
        /// </summary>
        private static string RenderTotal(CharacterObject character, List<PartyBase> parties)
        {
            int healthy = 0;
            int wounded = 0;
            bool found = false;

            foreach (PartyBase party in parties)
            {
                TroopRoster members = party?.MemberRoster;
                if (members == null)
                {
                    continue;
                }

                int index = members.FindIndexOfTroop(character);
                if (index < 0)
                {
                    continue;
                }

                TroopRosterElement element = members.GetElementCopyAtIndex(index);
                healthy += element.Number - element.WoundedNumber;
                wounded += element.WoundedNumber;
                found = true;
            }

            if (!found)
            {
                // The troop left the field between building the row and rendering it.
                return new TextObject("{=!} ").ToString();
            }

            // Vanilla's own text and localisation id, so the row renders identically in any
            // language - only the number differs, being the side's rather than one party's.
            var text = new TextObject("{=vLaBJFGy} {PARTY_MEMBER.LINK} ({PARTY_SIZE})");
            text.SetTextVariable("PARTY_SIZE", PartyBaseHelper.GetPartySizeText(healthy, wounded, isInspected: true));
            StringHelpers.SetCharacterProperties("PARTY_MEMBER", character, text);
            return text.ToString();
        }

        /// <summary>
        /// The parties vanilla counts on one side. Sieges pass them straight through; map events
        /// wrap them and leave out any whose strength the player is not allowed to see.
        /// </summary>
        internal static List<PartyBase> Involved(MBReadOnlyList<PartyBase> parties)
        {
            var involved = new List<PartyBase>();

            if (parties != null)
            {
                foreach (PartyBase party in parties)
                {
                    if (party != null)
                    {
                        involved.Add(party);
                    }
                }
            }

            return involved;
        }

        internal static List<PartyBase> Involved(MBReadOnlyList<MapEventParty> parties)
        {
            var involved = new List<PartyBase>();

            if (parties != null)
            {
                foreach (MapEventParty item in parties)
                {
                    PartyBase party = item?.Party;
                    if (party == null)
                    {
                        continue;
                    }

                    MobileParty mobileParty = party.MobileParty;
                    if (mobileParty != null && mobileParty.IsInfoHidden)
                    {
                        continue;
                    }

                    involved.Add(party);
                }
            }

            return involved;
        }

        /// <summary>
        /// Rebuilds one side exactly as vanilla does: heroes first in encounter order, then troops,
        /// with the same cross-party merging. Reproducing it is what makes row index N here mean
        /// the same troop as row index N in the tooltip.
        /// </summary>
        private static TroopRoster BuildSide(List<PartyBase> parties)
        {
            TroopRoster roster = TroopRoster.CreateDummyTroopRoster();
            int lastHeroIndex = 0;

            foreach (PartyBase party in parties)
            {
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

            return roster;
        }

        /// <summary>
        /// Row indices for one column in display order. Heroes keep vanilla's leading block;
        /// the troops behind them are sorted by headcount. Indices past this side's roster are
        /// vanilla's blank filler rows and stay where they are.
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
            // Descending headcount - the side-wide count, which is now what the row prints - then
            // elites ahead of chaff, then name so the order never jitters between refreshes.
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
            Debug.PrintError($"{SubModule.ModuleName}: {message} Leaving the encounter tooltip as vanilla built it.", Environment.StackTrace);
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
                EncounterTooltipSorter.Involved(parties1),
                EncounterTooltipSorter.Involved(parties2),
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
                EncounterTooltipSorter.Involved(parties1),
                EncounterTooltipSorter.Involved(parties2),
                isExtended);
        }
    }
}
