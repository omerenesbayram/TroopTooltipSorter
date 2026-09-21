using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

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

            try
            {
                _harmony = new Harmony("omerb." + ModuleId);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
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

            _announced = true;
            InformationManager.DisplayMessage(
                new InformationMessage($"{ModuleName} loaded.", Colors.Green));
        }
    }
}
