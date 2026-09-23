using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace TroopTooltipSorter.Patches
{
    /// <summary>
    /// Content modules can ship their own copy of TooltipRefresherCollection and re-register some
    /// tooltips to point at it - Naval DLC does exactly this for settlements, which is why patching
    /// only the TaleWorlds type left castle and town garrisons untouched.
    ///
    /// The attribute patches cover the TaleWorlds copy; this finds every other loaded copy and
    /// applies the same pair. Candidates are matched on the exact parameter signature rather than
    /// the name alone, so an unrelated type is left alone. Naval DLC is not a dependency: without it
    /// installed the scan simply finds nothing.
    /// </summary>
    internal static class TooltipBuilderPatcher
    {
        private const string TargetMethodName = "AddPartyTroopProperties";

        /// <summary>Guards against patching the same method twice across repeated scans.</summary>
        private static readonly HashSet<MethodBase> AlreadyPatched = new HashSet<MethodBase>();

        internal static void PatchAdditionalBuilders(Harmony harmony)
        {
            var prefix = new HarmonyMethod(AccessTools.Method(typeof(SortTroopTooltipPatch), "Prefix"));
            var postfix = new HarmonyMethod(AccessTools.Method(typeof(SplitSummaryByTierPatch), "Postfix"));
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
                    if (target == null || !HasExpectedSignature(target) || !AlreadyPatched.Add(target))
                    {
                        continue;
                    }

                    try
                    {
                        harmony.Patch(target, prefix: prefix, postfix: postfix);
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
    }
}
