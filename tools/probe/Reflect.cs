using System;
using System.Reflection;

namespace RavenIron.CairnProbe
{
    /// <summary>
    /// Every game member here is reached by name, never by reference. Two reasons:
    /// the probe carries no assembly_valheim reference at all (so it cannot be broken
    /// by the publicized-at-compile-time / private-at-runtime trap it exists to help
    /// design around), and a member that moves in a Valheim patch degrades this to a
    /// blank column instead of a crash.
    ///
    /// Nothing latches a failure. A null is "not resolved YET" and is retried on the
    /// next sample, because EnvMan and ZoneSystem do not exist until a world loads.
    /// </summary>
    internal static class Reflect
    {
        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static Type _envMan;
        private static Type _zoneSystem;

        internal static Type EnvManType => _envMan ?? (_envMan = FindType("EnvMan"));
        internal static Type ZoneSystemType => _zoneSystem ?? (_zoneSystem = FindType("ZoneSystem"));

        internal static Type FindType(string name)
        {
            Type t = Type.GetType(name + ", assembly_valheim");
            if (t != null) return t;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(name, false);
                    if (t != null) return t;
                }
                catch
                {
                    // A dynamic or partially-loaded assembly. Not our problem.
                }
            }
            return null;
        }

        /// <summary>The singleton, however this particular class spells it.</summary>
        internal static object Singleton(Type t)
        {
            if (t == null) return null;

            FieldInfo f = t.GetField("m_instance", AnyStatic) ?? t.GetField("instance", AnyStatic);
            object value = f?.GetValue(null);
            if (value == null)
            {
                PropertyInfo p = t.GetProperty("instance", AnyStatic);
                value = p?.GetValue(null, null);
            }

            // Unity's fake-null: a destroyed object is not a real reference.
            if (value is UnityEngine.Object uo && uo == null) return null;
            return value;
        }

        internal static bool TryCall<T>(object target, string method, out T value)
        {
            value = default;
            if (target == null) return false;

            try
            {
                MethodInfo m = target.GetType().GetMethod(method, AnyInstance, null, Type.EmptyTypes, null);
                if (m == null) return false;

                object result = m.Invoke(target, null);
                if (!(result is T typed)) return false;

                value = typed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryField<T>(object target, string field, out T value)
        {
            value = default;
            if (target == null) return false;

            try
            {
                FieldInfo f = target.GetType().GetField(field, AnyInstance);
                if (f == null) return false;

                object result = f.GetValue(target);
                if (!(result is T typed)) return false;

                value = typed;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
