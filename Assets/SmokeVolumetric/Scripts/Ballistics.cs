using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>Ballistic helpers shared by the throwables.</summary>
    public static class Ballistics
    {
        /// <summary>
        /// Launch velocity that puts a projectile at <paramref name="to"/> after
        /// exactly <paramref name="flightTime"/> seconds. Solving for the time
        /// instead of the speed is what lets a grenade pop in mid-air at a
        /// predictable distance instead of whenever it happens to land.
        /// </summary>
        public static Vector3 VelocityFor(Vector3 from, Vector3 to, float flightTime)
        {
            flightTime = Mathf.Max(0.2f, flightTime);
            Vector3 d = to - from;

            return new Vector3(
                d.x / flightTime,
                d.y / flightTime - 0.5f * Physics.gravity.y * flightTime,
                d.z / flightTime);
        }

        public static float FlightTime(Vector3 from, Vector3 to, float speed)
        {
            return Mathf.Max(0.35f, Vector3.Distance(from, to) / Mathf.Max(1f, speed));
        }
    }
}
