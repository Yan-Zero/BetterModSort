using System.Reflection;
using HarmonyLib;
using Verse;

namespace BetterModSort
{
    public class BetterModSortMod : Mod
    {
        public static Harmony? HarmonyInstance { get; private set; }

        public BetterModSortMod(ModContentPack content)
            : base(content)
        {
            HarmonyInstance = new Harmony("com.bettermodsort.rimworld");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[BetterModSort] Harmony patches applied.");
        }
    }
}
