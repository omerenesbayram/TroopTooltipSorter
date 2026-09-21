using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TroopTooltipSorter.Diagnostics;

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

        private const string TargetMethodName = "AddPartyTroopProperties";

        /// <summary>Guards against patching the same method twice across repeated scans.</summary>
        private static readonly HashSet<MethodBase> PatchedExtras = new HashSet<MethodBase>();

        /// <summary>
        /// Content modules can ship their own copy of TooltipRefresherCollection and re-register
        /// some tooltips to point at it - Naval DLC does exactly this for settlements, which is why
        /// patching only the TaleWorlds type leaves castle and town garrisons unsorted.
        ///
        /// So find every loaded copy and patch it too. Candidates are matched on the exact parameter
        /// signature rather than the name alone, so an unrelated type is left alone. Nothing here is
        /// a hard dependency: without Naval DLC installed this simply finds nothing.
        /// </summary>
        internal static void PatchAdditionalRefreshers(Harmony harmony)
        {
            MethodInfo prefix = AccessTools.Method(typeof(SortTroopTooltipPatch), nameof(Prefix));
            Type vanilla = typeof(TooltipRefresherCollection);

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (IsSystemAssembly(assembly))
                {
                    continue;
                }

                foreach (Type type in SafeGetTypes(assembly))
                {
                    if (type == null || type == vanilla ||
                        !type.Name.EndsWith("TooltipRefresherCollection", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    MethodInfo target = AccessTools.Method(type, TargetMethodName);
                    if (target == null || !HasExpectedSignature(target) || !PatchedExtras.Add(target))
                    {
                        continue;
                    }

                    try
                    {
                        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                        DiagnosticLog.Write($"[patch] also patched {type.FullName}.{TargetMethodName}");
                    }
                    catch (Exception e)
                    {
                        Debug.PrintError($"{SubModule.ModuleName}: could not patch {type.FullName}. {e}", e.StackTrace);
                    }
                }
            }
        }

        private static bool HasExpectedSignature(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 5
                && parameters[1].ParameterType == typeof(TroopRoster)
                && parameters[1].Name == "troopRoster"
                && parameters[2].ParameterType == typeof(TextObject)
                && parameters[3].ParameterType == typeof(bool);
        }

        private static bool IsSystemAssembly(Assembly assembly)
        {
            string name = assembly.GetName().Name;
            return name.StartsWith("System", StringComparison.Ordinal)
                || name.StartsWith("Microsoft", StringComparison.Ordinal)
                || name == "mscorlib"
                || name == "netstandard";
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                // A partially loadable assembly still tells us about the types that did resolve.
                return e.Types;
            }
            catch
            {
                return new Type[0];
            }
        }

        private static void Prefix(
            ref TroopRoster troopRoster,
            TextObject title,
            bool isInspected,
            Func<TroopRoster> funcToDoBeforeLambda)
        {
            if (troopRoster == null || troopRoster.Count < 2)
            {
                return;
            }

            try
            {
                TroopRoster sorted = SortedByHeadcount(troopRoster);

                if (DiagnosticLog.Enabled)
                {
                    TroopRoster before = troopRoster;
                    string caller = DescribeCaller(funcToDoBeforeLambda);
                    DiagnosticLog.WriteOnce(
                        $"sort:{caller}:{title}:{before.Count}",
                        () => $"[sort] title='{title}' isInspected={isInspected} count={before.Count}{Environment.NewLine}" +
                              $"        from   {caller}{Environment.NewLine}" +
                              $"        before {Describe(before)}{Environment.NewLine}" +
                              $"        after  {Describe(sorted)}");
                }

                troopRoster = sorted;
            }
            catch (Exception e)
            {
                // Tooltips refresh constantly, so a failure here would otherwise flood the log.
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    DiagnosticLog.Write($"[sort] FAILED: {e}");
                    Debug.PrintError($"{SubModule.ModuleName}: could not sort troop tooltip, falling back to vanilla order. {e}", e.StackTrace);
                }
            }
        }

        /// <summary>
        /// Names the refresher we were called from, without walking the stack: the roster factory
        /// is a lambda compiled into a closure named after its enclosing method, so its metadata
        /// reads like "&lt;RefreshSettlementTooltip&gt;b__0".
        /// </summary>
        private static string DescribeCaller(Func<TroopRoster> rosterSource)
        {
            MethodInfo method = rosterSource?.Method;
            if (method == null)
            {
                return "(no roster factory supplied)";
            }

            return (method.DeclaringType?.Name ?? "?") + "." + method.Name;
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

        private static string Describe(TroopRoster roster, int maxEntries = 8)
        {
            var builder = new StringBuilder();

            for (int i = 0; i < roster.Count && i < maxEntries; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(element.Character?.Name).Append(' ').Append(element.Number);
            }

            if (roster.Count > maxEntries)
            {
                builder.Append(", ... (+").Append(roster.Count - maxEntries).Append(')');
            }

            return builder.ToString();
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
