using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Library;

namespace TroopTooltipSorter.Patches
{
    /// <summary>
    /// Vanilla builds the ALT-extended troop breakdown of a party/army map tooltip by walking the
    /// troop roster in storage order, so the list arrives unsorted - and because the tooltip clips
    /// after a fixed number of lines, the stacks worth knowing about can fall off the bottom.
    ///
    /// Rather than reorder the finished tooltip rows, hand the vanilla builder a roster that is
    /// already in the order we want. Everything downstream (the troop lines, the hero lines, the
    /// infantry/archer/cavalry summary) is derived from that roster, so one substitution is enough.
    ///
    /// This is a throwaway copy used only to render the tooltip - the party's real roster is
    /// untouched, so there is no gameplay effect.
    /// </summary>
    [HarmonyPatch(typeof(TooltipRefresherCollection), "AddPartyTroopProperties")]
    internal static class SortTroopTooltipPatch
    {
        private static bool _loggedFailure;

        private static void Prefix(ref TroopRoster troopRoster)
        {
            if (troopRoster == null || troopRoster.Count < 2)
            {
                return;
            }

            try
            {
                troopRoster = SortedByHeadcount(troopRoster);
            }
            catch (Exception e)
            {
                // Tooltips refresh constantly, so a failure here would otherwise flood the log.
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Debug.PrintError($"{SubModule.ModuleName}: could not sort troop tooltip, falling back to vanilla order. {e}", e.StackTrace);
                }
            }
        }

        private static TroopRoster SortedByHeadcount(TroopRoster source)
        {
            var heroes = new List<TroopRosterElement>();
            var troops = new List<TroopRosterElement>();

            for (int i = 0; i < source.Count; i++)
            {
                TroopRosterElement element = source.GetElementCopyAtIndex(i);
                if (element.Character == null)
                {
                    continue;
                }

                if (element.Character.IsHero)
                {
                    heroes.Add(element);
                }
                else
                {
                    troops.Add(element);
                }
            }

            troops.Sort(CompareByHeadcountThenTier);

            TroopRoster sorted = TroopRoster.CreateDummyTroopRoster();

            // Heroes render in their own block above the troop list and vanilla puts the party
            // leader first, so leave their relative order alone.
            foreach (TroopRosterElement hero in heroes)
            {
                Append(sorted, hero);
            }

            foreach (TroopRosterElement troop in troops)
            {
                Append(sorted, troop);
            }

            return sorted;
        }

        private static void Append(TroopRoster roster, TroopRosterElement element)
        {
            // removeDepleted: false so a zero-count entry survives the copy exactly as vanilla had it.
            roster.AddToCounts(
                element.Character,
                element.Number,
                insertAtFront: false,
                woundedCount: element.WoundedNumber,
                xpChange: element.Xp,
                removeDepleted: false);
        }

        private static int CompareByHeadcountThenTier(TroopRosterElement a, TroopRosterElement b)
        {
            // Descending headcount, then elites ahead of chaff at equal counts.
            int byCount = b.Number.CompareTo(a.Number);
            if (byCount != 0)
            {
                return byCount;
            }

            int byTier = b.Character.Tier.CompareTo(a.Character.Tier);
            if (byTier != 0)
            {
                return byTier;
            }

            // List.Sort is unstable; break the last tie by name so the order never jitters
            // between refreshes of the same tooltip.
            return string.CompareOrdinal(a.Character.Name?.ToString(), b.Character.Name?.ToString());
        }
    }
}
