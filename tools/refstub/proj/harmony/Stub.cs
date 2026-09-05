// Compile-verification stub only. Not shipped, not a reimplementation.
using System.Reflection;

namespace HarmonyLib
{
    public class HarmonyMethod
    {
        public HarmonyMethod(MethodInfo method) { }
    }

    public class Harmony
    {
        public Harmony(string id) { }
        public MethodInfo Patch(MethodBase original,
                                HarmonyMethod prefix = null,
                                HarmonyMethod postfix = null,
                                HarmonyMethod transpiler = null,
                                HarmonyMethod finalizer = null) => null;
    }
}
