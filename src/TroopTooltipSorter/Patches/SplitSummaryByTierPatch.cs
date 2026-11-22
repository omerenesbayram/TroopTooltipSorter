using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace TroopTooltipSorter.Patches
{
    /// <summary>
    /// The "Troops" block of a party/army tooltip summarises the roster by formation class -
    /// Infantry, Ranged, Cavalry, Horse Archer - which tells you the shape of an army but nothing
    /// about its quality. A 200-strong infantry line of recruits and one of veterans read alike.
    ///
    /// While ALT is held, split each of those rows in two by troop tier. Without ALT the tooltip
    /// keeps vanilla's single number per class.
    ///
    /// Vanilla builds these rows from the same roster we are handed, so rather than duplicate its
    /// aggregation we recompute the totals and swap each finished row for the two it should have
    /// been. Rows are matched by the exact label vanilla generated, so nothing else is touched.
    /// </summary>
    [HarmonyPatch(typeof(TooltipRefresherCollection), "AddPartyTroopProperties")]
    internal static class SplitSummaryByTierPatch
    {
        private static bool _loggedFailure;

        private static void Postfix(
            PropertyBasedTooltipVM propertyBasedTooltipVM,
            TroopRoster troopRoster,
            bool isInspected)
        {
            // Vanilla only emits the formation summary for a roster you are allowed to inspect.
            //
            // The split is extra detail, so it is gated on ALT the same way the troop breakdown is.
            // This tooltip is rebuilt when ALT is pressed or released, so the rows can simply be
            // swapped rather than carrying both states.
            if (!isInspected || troopRoster == null || propertyBasedTooltipVM == null || !propertyBasedTooltipVM.IsExtended)
            {
                return;
            }

            MBBindingList<TooltipProperty> rows = propertyBasedTooltipVM.TooltipPropertyList;
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            try
            {
                Dictionary<string, TierSummarySplitter.TierTotals> byLabel =
                    TierSummarySplitter.TotalsByRowLabel(troopRoster);

                TierSummarySplitter.Split(rows, byLabel, gateOnExtended: false);
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
    }
}
