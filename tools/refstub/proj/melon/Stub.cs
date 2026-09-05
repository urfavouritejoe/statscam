// Compile-verification stub only. Mirrors just the MelonLoader surface StatsCam uses.
using System;
using HarmonyLib;

namespace MelonLoader
{
    public abstract class MelonBase
    {
        public Harmony HarmonyInstance { get; protected set; }
    }

    public class MelonMod : MelonBase
    {
        public virtual void OnInitializeMelon() { }
        public virtual void OnLateInitializeMelon() { }
        public virtual void OnUpdate() { }
        public virtual void OnGUI() { }
        public virtual void OnApplicationQuit() { }
        public virtual void OnSceneWasLoaded(int buildIndex, string sceneName) { }
    }

    public static class MelonLogger
    {
        public static void Msg(string txt) { }
        public static void Warning(string txt) { }
        public static void Error(string txt) { }
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public class MelonInfoAttribute : Attribute
    {
        public MelonInfoAttribute(Type type, string name, string version, string author, string dl = null) { }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class MelonGameAttribute : Attribute
    {
        public MelonGameAttribute(string developer = null, string gameName = null) { }
    }
}
