using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TroopTooltipSorter.Diagnostics;
using TroopTooltipSorter.Patches;

namespace TroopTooltipSorter
{
    public class SubModule : MBSubModuleBase
    {
        public const string ModuleId = "TroopTooltipSorter";
        public const string ModuleName = "Troop Tooltip Sorter";

        private Harmony _harmony;
        private bool _announced;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            DiagnosticLog.Initialize();

            try
            {
                _harmony = new Harmony("omerb." + ModuleId);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());

                // Naval DLC and friends carry their own copy of the tooltip builder; attribute
                // patching only reaches the TaleWorlds one.
                TooltipBuilderPatcher.PatchAdditionalBuilders(_harmony);
            }
            catch (Exception e)
            {
                // A failed patch must never take the game down with it - run unpatched instead.
                Debug.PrintError($"{ModuleName}: failed to apply Harmony patches. {e}", e.StackTrace);
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            if (_announced)
            {
                return;
            }

            // Re-scan in case a content module's assembly had not loaded yet at OnSubModuleLoad.
            // Already-patched methods are skipped, so running twice is harmless.
            try
            {
                if (_harmony != null)
                {
                    TooltipBuilderPatcher.PatchAdditionalBuilders(_harmony);
                }
            }
            catch (Exception e)
            {
                Debug.PrintError($"{ModuleName}: re-scan for additional tooltip builders failed. {e}", e.StackTrace);
            }

            _announced = true;
            InformationManager.DisplayMessage(
                new InformationMessage($"{ModuleName} loaded.", Colors.Green));
        }
    }
}
