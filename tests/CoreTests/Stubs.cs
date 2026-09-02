// Hand-written stand-ins for the handful of game / BepInEx types the tested source
// mentions in its signatures. Deliberately minimal: the stub surface is almost always
// smaller than it looks. Nothing here needs to behave like Valheim — it only needs to
// compile and let the real logic run.

using System;
using System.Collections.Generic;

// ---- UnityEngine ------------------------------------------------------------------
// Must live in the real namespace: the shipping source has `using UnityEngine;`, and the
// whole point of this harness is to compile that source unmodified.

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public override string ToString() => $"({x},{y},{z})";
    }
}

// ---- Valheim ----------------------------------------------------------------------
// Global-namespace types in the real game, so the stubs are too.

// Mirrors assembly_utils. Persistence passes Local explicitly: Auto and Cloud resolve to a
// RELATIVE cloud path, which is not a filesystem location.
public static class FileHelpers
{
    public enum FileSource { Auto = 0, Local = 1, Cloud = 2, Legacy = 3 }
}

public class World
{
    // long, not ulong — matches the real assembly. Persistence casts on the way out, and
    // asking FieldRefAccess for the wrong one throws rather than converting.
    public long m_uid;

    // Tests always set Persistence.OverrideDirectory, so this is never the path taken.
    public static string GetWorldSavePath(FileHelpers.FileSource fileSource)
        => System.IO.Path.GetTempPath();
}

public class ZNet
{
    // Null means "not the host". Tests override the uid, so this stays null and the
    // reflection path below is never exercised here — it is covered in-game instead.
    public static World GetWorldIfIsHost() => null;
}

// ---- Harmony ----------------------------------------------------------------------

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static TField FieldRefAccess<TObject, TField>(TObject instance, string fieldName)
            => default;
    }
}

// ---- BepInEx ----------------------------------------------------------------------

namespace BepInEx.Configuration
{
    public class AcceptableValueRange<T>
    {
        public AcceptableValueRange(T min, T max) { }
    }

    public class ConfigDescription
    {
        public ConfigDescription(string description, object acceptableValues = null) { }
    }

    public class ConfigEntry<T>
    {
        public T Value { get; set; }
        public ConfigEntry(T value) { Value = value; }
    }

    public class ConfigFile
    {
        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description = null)
            => new ConfigEntry<T>(defaultValue);

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description)
            => new ConfigEntry<T>(defaultValue);
    }
}

// ---- The plugin's log surface ------------------------------------------------------
// Core reaches the logger as `Cairn.Log`. The real type is a BaseUnityPlugin, which cannot
// compile here, so this stands in for it and CAPTURES what was logged — several tests
// assert that a failure was reported at all, which is the behaviour that matters when a
// store degrades quietly.

namespace RavenIron.Cairn
{
    public sealed class TestLog
    {
        public readonly List<string> Info = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public void LogInfo(object data) => Info.Add(data?.ToString() ?? "");
        public void LogWarning(object data) => Warnings.Add(data?.ToString() ?? "");
        public void LogError(object data) => Errors.Add(data?.ToString() ?? "");

        public void Clear() { Info.Clear(); Warnings.Clear(); Errors.Clear(); }
    }

    public static class Cairn
    {
        public static readonly TestLog Log = new TestLog();
    }
}
