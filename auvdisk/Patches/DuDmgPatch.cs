using System.Diagnostics;
using System.Reflection;
using DiscUtils.HfsPlus;
using DiscUtils.Vfs;

using HarmonyLib;

namespace auvdisk.Patches
{
    public class DuDmgPatch
    {
        public static void ApplyDmgPatch(Harmony harmony)
        {
            var assembly = typeof(HfsPlusFileSystem).Assembly;

            var fsType = assembly.GetType("DiscUtils.HfsPlus.HfsPlusFileSystemImpl");

            var original = fsType!.GetRuntimeMethods().First(x => x.Name == "ResolveSymlink").GetDeclaredMember();
            var prefix = typeof(DuDmgPatch).GetMethod("ResolveSymlinkPrefixPatch");

            harmony.Patch(original, new HarmonyMethod(prefix));

        }

        // Currently handling of the symlinks in DU sometimes goes into stackoverflow
        // This crude hack disables symlink handling on HFS filesystems
        // Needed until this https://github.com/LTRData/DiscUtils/commit/82c295e27a7728f5029b8d231824eee2021edd41
        // will go into the next version of the nuget package
        public static bool ResolveSymlinkPrefixPatch(ref object __result, object entry, string path)
        {
            var dirType = entry.GetType();

            var tupleDefinition = typeof(ValueTuple<,>);

            var specificTupleType = tupleDefinition.MakeGenericType(dirType, path.GetType());

            ConstructorInfo ctor = specificTupleType.GetConstructor([dirType, path.GetType()])!;
            object instance = ctor.Invoke([entry, path]);

            __result = instance;
            return false;
        }
    }
}