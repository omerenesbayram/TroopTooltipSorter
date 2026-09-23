using System;
using System.Collections.Generic;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace TroopTooltipSorter.Patches
{
    /// <summary>
    /// The "Troops" block of a party/army tooltip summarises the roster by formation class -
    /// Infantry, Ranged, Cavalry, Horse Archer - which tells you the shape of an army but nothing
    /// about its quality. A 200-strong infantry line of recruits and one of veterans read alike.
    ///
    /// Split each of those rows in two by troop tier: tier 4 and above, and everything below.
    ///
    /// Vanilla builds these rows from the same roster we are handed, so rather than duplicate its
    /// aggregation we recompute the totals and swap each finished row for the two it should have
    /// been. Rows are matched by the exact label vanilla generated, so nothing else is touched.
    /// </summary>
    [HarmonyPatch(typeof(TooltipRefresherCollection), "AddPartyTroopProperties")]
    internal static class SplitSummaryByTierPatch
    {
        /// <summary>Lowest tier counted as a veteran. Bannerlord tiers run 1-6.</summary>
        private const int VeteranTier = 4;

        private const string VeteranSuffix = " T4+";
        private const string RecruitSuffix = " T3-";

        private static bool _loggedFailure;

        private static void Postfix(
            PropertyBasedTooltipVM propertyBasedTooltipVM,
            TroopRoster troopRoster,
            bool isInspected)
        {
            // Vanilla only emits the formation summary for a roster you are allowed to inspect.
            if (!isInspected || troopRoster == null)
            {
                return;
            }

            MBBindingList<TooltipProperty> rows = propertyBasedTooltipVM?.TooltipPropertyList;
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            try
            {
                Dictionary<string, TierTotals> byLabel = TotalsByRowLabel(troopRoster);
                if (byLabel.Count == 0)
                {
                    return;
                }

                // Walk backwards: replacing a row shifts everything after it.
                for (int i = rows.Count - 1; i >= 0; i--)
                {
                    TooltipProperty row = rows[i];
                    string label = row?.DefinitionLabel;
                    if (string.IsNullOrEmpty(label) || !byLabel.TryGetValue(label, out TierTotals totals))
                    {
                        continue;
                    }

                    Replace(rows, i, label, totals);
                }
            }
            catch (Exception e)
            {
                // Tooltips refresh constantly, so a failure here would otherwise flood the log.
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Debug.PrintError($"{SubModule.ModuleName}: could not split the troop summary by tier, leaving it as vanilla. {e}", e.StackTrace);
                }
            }
        }

        /// <summary>
        /// Aggregates the roster the way vanilla does - by <see cref="CharacterObject.DefaultFormationClass"/>,
        /// heroes included - but keeps veterans and the rest apart. Keyed by the row label vanilla
        /// would have produced, so matching a finished row needs no second lookup.
        /// </summary>
        private static Dictionary<string, TierTotals> TotalsByRowLabel(TroopRoster roster)
        {
            var totals = new Dictionary<string, TierTotals>(StringComparer.Ordinal);

            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                CharacterObject character = element.Character;
                if (character == null)
                {
                    continue;
                }

                string label = ClassLabel(character.DefaultFormationClass);
                if (string.IsNullOrEmpty(label))
                {
                    continue;
                }

                if (!totals.TryGetValue(label, out TierTotals bucket))
                {
                    bucket = new TierTotals();
                    totals[label] = bucket;
                }

                bucket.Add(character.Tier >= VeteranTier, element.Number - element.WoundedNumber, element.WoundedNumber);
            }

            return totals;
        }

        private static void Replace(MBBindingList<TooltipProperty> rows, int index, string label, TierTotals totals)
        {
            // Nothing to say about an empty class - leave vanilla's row alone.
            if (totals.VeteranTotal == 0 && totals.RecruitTotal == 0)
            {
                return;
            }

            rows.RemoveAt(index);
            int insertAt = index;

            if (totals.VeteranTotal > 0)
            {
                rows.Insert(insertAt++, MakeRow(label + VeteranSuffix, totals.VeteranHealthy, totals.VeteranWounded));
            }

            if (totals.RecruitTotal > 0)
            {
                rows.Insert(insertAt, MakeRow(label + RecruitSuffix, totals.RecruitHealthy, totals.RecruitWounded));
            }
        }

        private static TooltipProperty MakeRow(string label, int healthy, int wounded)
        {
            // Mirrors vanilla's " {PARTY_SIZE}" row, so wounded counts render the usual way.
            var value = new TextObject("{=!} {PARTY_SIZE}");
            value.SetTextVariable("PARTY_SIZE", PartyBaseHelper.GetPartySizeText(healthy, wounded, isInspected: true));
            return new TooltipProperty(label, value.ToString(), 0);
        }

        private static string ClassLabel(FormationClass formationClass)
        {
            return GameTexts.FindText("str_troop_type_name", formationClass.GetName())?.ToString();
        }

        private sealed class TierTotals
        {
            internal int VeteranHealthy;
            internal int VeteranWounded;
            internal int RecruitHealthy;
            internal int RecruitWounded;

            internal int VeteranTotal => VeteranHealthy + VeteranWounded;

            internal int RecruitTotal => RecruitHealthy + RecruitWounded;

            internal void Add(bool veteran, int healthy, int wounded)
            {
                if (veteran)
                {
                    VeteranHealthy += healthy;
                    VeteranWounded += wounded;
                }
                else
                {
                    RecruitHealthy += healthy;
                    RecruitWounded += wounded;
                }
            }
        }
    }
}
