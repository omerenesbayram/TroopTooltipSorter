using System;
using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace TroopTooltipSorter.Patches
{
    /// <summary>
    /// Splitting a formation-class summary row ("Infantry 172") into tier buckets
    /// ("Infantry T4+ 13" / "Infantry T3- 159").
    ///
    /// Two different tooltips need this and they are built very differently, so the logic lives
    /// here rather than in either patch. See <see cref="Split"/> for why they are handled in two
    /// modes.
    /// </summary>
    internal static class TierSummarySplitter
    {
        /// <summary>Lowest tier counted as a veteran. Bannerlord tiers run 1-6.</summary>
        private const int VeteranTier = 4;

        private const string VeteranSuffix = " T4+";
        private const string RecruitSuffix = " T3-";

        /// <summary>
        /// Aggregates a roster the way the game does - by <see cref="CharacterObject.DefaultFormationClass"/>,
        /// heroes included - but keeps veterans and the rest apart. Keyed by the row label the game
        /// would have produced, so matching a finished row needs no second lookup.
        /// </summary>
        internal static Dictionary<string, TierTotals> TotalsByRowLabel(TroopRoster roster)
        {
            var totals = new Dictionary<string, TierTotals>(StringComparer.Ordinal);
            if (roster == null)
            {
                return totals;
            }

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

        /// <summary>
        /// Replaces each formation-class row with its two tier rows.
        ///
        /// <paramref name="gateOnExtended"/> picks between the two shapes of tooltip. A map tooltip
        /// is rebuilt whenever ALT is pressed or released, and its patch only runs in the extended
        /// pass, so the row can simply be swapped. The encounter overlay instead builds one list
        /// covering both states and lets each row decide via its own show-when-extended flag, so
        /// there we keep the original row for the un-extended view and add the split alongside it.
        /// </summary>
        internal static void Split(IList<TooltipProperty> rows, Dictionary<string, TierTotals> byLabel, bool gateOnExtended)
        {
            if (rows == null || byLabel == null || byLabel.Count == 0)
            {
                return;
            }

            // Walk backwards: inserting rows shifts everything after the insertion point.
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                TooltipProperty row = rows[i];
                string label = row?.DefinitionLabel;
                if (string.IsNullOrEmpty(label) || !byLabel.TryGetValue(label, out TierTotals totals))
                {
                    continue;
                }

                // Nothing to say about an empty class - leave the original row alone.
                if (totals.VeteranTotal == 0 && totals.RecruitTotal == 0)
                {
                    continue;
                }

                int insertAt;
                if (gateOnExtended)
                {
                    // Keep the original, but only while ALT is not held.
                    row.OnlyShowWhenNotExtended = true;
                    row.OnlyShowWhenExtended = false;
                    insertAt = i + 1;
                }
                else
                {
                    rows.RemoveAt(i);
                    insertAt = i;
                }

                if (totals.VeteranTotal > 0)
                {
                    rows.Insert(insertAt++, MakeRow(label + VeteranSuffix, totals.VeteranHealthy, totals.VeteranWounded, gateOnExtended));
                }

                if (totals.RecruitTotal > 0)
                {
                    rows.Insert(insertAt, MakeRow(label + RecruitSuffix, totals.RecruitHealthy, totals.RecruitWounded, gateOnExtended));
                }
            }
        }

        private static TooltipProperty MakeRow(string label, int healthy, int wounded, bool onlyWhenExtended)
        {
            // Mirrors the game's " {PARTY_SIZE}" row, so wounded counts render the usual way.
            var value = new TextObject("{=!} {PARTY_SIZE}");
            value.SetTextVariable("PARTY_SIZE", PartyBaseHelper.GetPartySizeText(healthy, wounded, isInspected: true));
            return new TooltipProperty(label, value.ToString(), 0, onlyWhenExtended);
        }

        private static string ClassLabel(FormationClass formationClass)
        {
            return GameTexts.FindText("str_troop_type_name", formationClass.GetName())?.ToString();
        }

        internal sealed class TierTotals
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
