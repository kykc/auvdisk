using HarmonyLib;

namespace auvdisk.Patches
{
    public static class PatchManager
    {
        public static void ApplyPatches()
        {
            var harmony = new Harmony("com.automatl.auvdisk");

            //DuDmgPatch.ApplyDmgPatch(harmony);
        }
    }
}