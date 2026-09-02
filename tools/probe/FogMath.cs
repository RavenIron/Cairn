using UnityEngine;

namespace RavenIron.CairnProbe
{
    /// <summary>
    /// Unity's three fog curves, and the one question they answer for a beacon:
    /// how much of an object's contrast against the fog survives at distance d.
    ///
    /// Transmittance 1.0 = the object reads exactly as authored; 0.0 = it has become
    /// the fog colour and is invisible whatever its size or brightness.
    /// </summary>
    public static class FogMath
    {
        public static float Transmittance(FogMode mode, float density, float start, float end, float distance)
        {
            if (distance <= 0f) return 1f;

            switch (mode)
            {
                case FogMode.Linear:
                    float span = end - start;
                    if (span <= 0f) return distance < end ? 1f : 0f;
                    return Mathf.Clamp01((end - distance) / span);

                case FogMode.Exponential:
                    return Mathf.Exp(-density * distance);

                case FogMode.ExponentialSquared:
                    float x = density * distance;
                    return Mathf.Exp(-(x * x));

                default:
                    return 1f;
            }
        }

        /// <summary>
        /// The distance at which transmittance falls to <paramref name="threshold"/> —
        /// the practical horizon for a beacon. Returns float.PositiveInfinity when the
        /// curve never gets there (density zero, fog off).
        /// </summary>
        public static float Horizon(FogMode mode, float density, float start, float end, float threshold)
        {
            threshold = Mathf.Clamp(threshold, 1e-6f, 0.999999f);

            switch (mode)
            {
                case FogMode.Linear:
                    float span = end - start;
                    if (span <= 0f) return end;
                    return end - threshold * span;

                case FogMode.Exponential:
                    if (density <= 0f) return float.PositiveInfinity;
                    return -Mathf.Log(threshold) / density;

                case FogMode.ExponentialSquared:
                    if (density <= 0f) return float.PositiveInfinity;
                    return Mathf.Sqrt(-Mathf.Log(threshold)) / density;

                default:
                    return float.PositiveInfinity;
            }
        }

        /// <summary>
        /// Vertical pixels a column of the given world height covers at a given range.
        /// GameCamera.m_fov is 65 and Unity's fieldOfView is VERTICAL, so this is the
        /// number that decides whether a beacon reads or is a smudge.
        /// </summary>
        public static float PixelsTall(float worldHeight, float distance, float verticalFovDeg, int screenHeightPx)
        {
            if (distance <= 0f || verticalFovDeg <= 0f) return 0f;
            float degrees = Mathf.Atan2(worldHeight, distance) * Mathf.Rad2Deg;
            return degrees / verticalFovDeg * screenHeightPx;
        }
    }
}
