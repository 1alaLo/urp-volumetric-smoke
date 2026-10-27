using System.Collections.Generic;
using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// Coarse voxel model of the static scene, plus a per-burst "diffusion" field.
    ///
    /// The raymarcher cannot march the real scene, so we hand it two numbers per
    /// cell, packed in one 3D texture:
    ///   R = distance to the nearest solid cell  (0 at a wall, saturating at MaxSolid)
    ///   G = shortest path length from the burst point *through free space*
    ///       (0 at the burst, saturating at MaxSpread)
    ///
    /// Smoke density is gated by both. The second channel is a 26-neighbour
    /// chamfer transform over free cells, so it is not a straight-line distance:
    /// a wall between the burst and a point pushes the value up instead of
    /// letting the smoke teleport through it, and a doorway lets it creep around
    /// at full speed. That is what makes the cloud hug geometry and pour through
    /// openings. (A plain 6-neighbour BFS would do the same, but its Manhattan
    /// metric slices the cloud into an octahedron; chamfer keeps it round.)
    /// </summary>
    public static class SmokeField
    {
        public const float CellSize = 0.4f;
        public const float MaxSpread = 6.0f;   // metres, encoded in G
        public const float MaxSolid = 1.2f;    // metres, encoded in R

        public struct Reach
        {
            public Texture3D texture;
            public Vector3 gridMin;
            public Vector3 gridInv;   // 1 / world size
        }

        static bool s_Built;
        static bool s_Warned;
        static Vector3Int s_Res;
        static float[] s_Solid;       // 1 = blocked
        static float[] s_SolidDist;   // metres, baked once
        static Vector3 s_Min;
        static Vector3 s_Size;

        public static bool Ready => s_Built && s_Solid != null;

        /// <summary>World AABB covered by the voxel grid (empty before the first build).</summary>
        public static Bounds GridBounds => new Bounds(s_Min + s_Size * 0.5f, s_Size);

        public static void Invalidate() => s_Built = false;

        // ------------------------------------------------------------------
        // build
        // ------------------------------------------------------------------
        static void EnsureBuilt()
        {
            if (s_Built)
                return;
            s_Built = true;

            var boxes = CollectStaticBounds(out Bounds bounds);
            if (boxes.Count == 0)
            {
                if (!s_Warned)
                    Debug.LogWarning("[SmokeField] no static colliders found - smoke will ignore scene geometry");
                s_Warned = true;
                s_Solid = null;
                return;
            }

            bounds.Expand(1.0f);
            s_Min = bounds.min;
            s_Size = bounds.size;

            int rx = Mathf.Max(4, Mathf.CeilToInt(s_Size.x / CellSize));
            int ry = Mathf.Max(4, Mathf.CeilToInt(s_Size.y / CellSize));
            int rz = Mathf.Max(4, Mathf.CeilToInt(s_Size.z / CellSize));

            // keep the whole thing under ~420k cells so a build stays a few ms
            const int MaxCells = 420000;
            while (rx * ry * rz > MaxCells)
            {
                rx = Mathf.Max(4, rx * 3 / 4);
                ry = Mathf.Max(4, ry * 3 / 4);
                rz = Mathf.Max(4, rz * 3 / 4);
            }

            s_Res = new Vector3Int(rx, ry, rz);
            s_Solid = new float[rx * ry * rz];

            // rasterise collider AABBs: every cell whose box touches one is solid.
            // X/Z get half a cell of slack so thin walls cannot slip between
            // samples; Y only grows downwards, otherwise a floor plane would
            // claim a whole extra layer of air above it and the smoke would float.
            var cell = new Vector3(s_Size.x / rx, s_Size.y / ry, s_Size.z / rz);

            foreach (var b in boxes)
            {
                Vector3 lo = b.min;
                Vector3 hi = b.max;
                lo.x -= cell.x * 0.5f; hi.x += cell.x * 0.5f;
                lo.z -= cell.z * 0.5f; hi.z += cell.z * 0.5f;
                lo.y -= cell.y * 0.5f;

                int x0 = Mathf.Clamp(Mathf.FloorToInt((lo.x - s_Min.x) / cell.x), 0, rx - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((hi.x - s_Min.x) / cell.x), 0, rx);
                int y0 = Mathf.Clamp(Mathf.FloorToInt((lo.y - s_Min.y) / cell.y), 0, ry - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt((hi.y - s_Min.y) / cell.y), 0, ry);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((lo.z - s_Min.z) / cell.z), 0, rz - 1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt((hi.z - s_Min.z) / cell.z), 0, rz);

                for (int z = z0; z < z1; z++)
                for (int y = y0; y < y1; y++)
                {
                    int row = rx * (y + ry * z);
                    for (int x = x0; x < x1; x++)
                        s_Solid[row + x] = 1f;
                }
            }

            BuildSolidDistance();
        }

        static List<Bounds> CollectStaticBounds(out Bounds total)
        {
            var list = new List<Bounds>(64);
            var colliders = Object.FindObjectsByType<Collider>();

            total = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;

            foreach (var c in colliders)
            {
                if (c == null || !c.enabled)
                    continue;

                var go = c.gameObject;
                // static geometry only: props the player carries or throws must
                // not carve permanent holes in the field
                if (!go.isStatic && go.GetComponentInParent<Rigidbody>() != null)
                    continue;

                var b = c.bounds;
                if (b.size.sqrMagnitude < 1e-6f)
                    continue;

                list.Add(b);
                if (!any)
                {
                    total = b;
                    any = true;
                }
                else
                {
                    total.Encapsulate(b);
                }
            }

            return list;
        }

        static void BuildSolidDistance()
        {
            int cells = s_Solid.Length;
            s_SolidDist = new float[cells];
            var queue = new int[cells];
            int head = 0, tail = 0;

            for (int i = 0; i < cells; i++)
                s_SolidDist[i] = float.MaxValue;

            for (int i = 0; i < cells; i++)
            {
                if (s_Solid[i] > 0.5f)
                {
                    s_SolidDist[i] = 0f;
                    queue[tail++] = i;
                }
            }

            FloodFill(queue, ref head, ref tail);

            for (int i = 0; i < cells; i++)
                if (s_SolidDist[i] == float.MaxValue)
                    s_SolidDist[i] = MaxSolid;
        }

        static void FloodFill(int[] queue, ref int head, ref int tail)
        {
            int rx = s_Res.x, ry = s_Res.y, rz = s_Res.z;
            float step = Mathf.Min(s_Size.x / rx, Mathf.Min(s_Size.y / ry, s_Size.z / rz));

            while (head < tail)
            {
                int i = queue[head++];
                float d = s_SolidDist[i] + step;
                if (d > MaxSolid * 1.5f)
                    continue;

                int x = i % rx;
                int y = (i / rx) % ry;
                int z = i / (rx * ry);

                if (x > 0) Relax(i - 1, d, queue, ref tail);
                if (x < rx - 1) Relax(i + 1, d, queue, ref tail);
                if (y > 0) Relax(i - rx, d, queue, ref tail);
                if (y < ry - 1) Relax(i + rx, d, queue, ref tail);
                if (z > 0) Relax(i - rx * ry, d, queue, ref tail);
                if (z < rz - 1) Relax(i + rx * ry, d, queue, ref tail);
            }
        }

        static void Relax(int index, float dist, int[] queue, ref int tail)
        {
            if (dist >= s_SolidDist[index])
                return;
            s_SolidDist[index] = dist;
            queue[tail++] = index;
        }

        // ------------------------------------------------------------------
        // per-burst field
        // ------------------------------------------------------------------
        /// <summary>
        /// BFS the free space around <paramref name="burst"/> and return a texture
        /// the shader can sample. Never returns null: if the field could not be
        /// built the caller simply gets no scene interaction.
        /// </summary>
        public static Reach BuildReach(Vector3 burst)
        {
            EnsureBuilt();
            var reach = new Reach { texture = null };
            if (s_Solid == null)
                return reach;

            int rx = s_Res.x, ry = s_Res.y, rz = s_Res.z;
            int cells = s_Solid.Length;
            var spread = new float[cells];

            // seed with every free cell near the burst point
            Vector3 local = burst - s_Min;
            int sx = Mathf.Clamp(Mathf.FloorToInt(local.x / Mathf.Max(1e-3f, s_Size.x / rx)), 0, rx - 1);
            int sy = Mathf.Clamp(Mathf.FloorToInt(local.y / Mathf.Max(1e-3f, s_Size.y / ry)), 0, ry - 1);
            int sz = Mathf.Clamp(Mathf.FloorToInt(local.z / Mathf.Max(1e-3f, s_Size.z / rz)), 0, rz - 1);

            int seed = sx + rx * (sy + ry * sz);
            if (s_Solid[seed] > 0.5f)
                seed = NearestFreeCell(sx, sy, sz);

            if (seed < 0)
                return reach; // burst is buried in geometry: no spread field

            ChamferSpread(spread, seed,
                s_Size.x / rx, s_Size.y / ry, s_Size.z / rz);

            var px = new Color32[cells];
            for (int i = 0; i < cells; i++)
            {
                float sd = s_Solid[i] > 0.5f ? 0f : Mathf.Min(s_SolidDist[i], MaxSolid);
                float pd = float.IsInfinity(spread[i]) ? MaxSpread : Mathf.Min(spread[i], MaxSpread);
                px[i] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(sd / MaxSolid) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(pd / MaxSpread) * 255f),
                    0, 255);
            }

            var tex = new Texture3D(rx, ry, rz, TextureFormat.RGBA32, false)
            {
                name = "SmokeReach",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);

            reach.texture = tex;
            reach.gridMin = s_Min;
            reach.gridInv = new Vector3(1f / s_Size.x, 1f / s_Size.y, 1f / s_Size.z);
            return reach;
        }

        /// <summary>
        /// Two-pass 26-neighbour chamfer transform over the free cells.
        ///
        /// The diffusion front must not be measured with a 6-neighbour BFS: that
        /// metric is a diamond (Manhattan) distance, so gating density by it
        /// slices the cloud into an octahedron. Diagonal steps make the measure
        /// close to Euclidean while still routing *around* solids, which is the
        /// property the doorway/wall behaviour depends on.
        /// </summary>
        static void ChamferSpread(float[] spread, int seed, float cx, float cy, float cz)
        {
            int rx = s_Res.x, ry = s_Res.y, rz = s_Res.z;
            int rxry = rx * ry;

            for (int i = 0; i < spread.Length; i++)
                spread[i] = float.PositiveInfinity;
            spread[seed] = 0f;

            // 13 offsets in the "negative" half-space + their metric weights
            var ox = new int[13];
            var oy = new int[13];
            var oz = new int[13];
            var off = new int[13];
            var wgt = new float[13];
            int n = 0;

            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0)
                    continue;
                bool negative = dz < 0 || (dz == 0 && (dy < 0 || (dy == 0 && dx < 0)));
                if (!negative)
                    continue;

                ox[n] = dx; oy[n] = dy; oz[n] = dz;
                off[n] = dx + rx * dy + rxry * dz;
                wgt[n] = Mathf.Sqrt(dx * cx * dx * cx + dy * cy * dy * cy + dz * cz * dz * cz);
                n++;
            }

            // forward sweep: relax from already-visited cells
            for (int z = 0; z < rz; z++)
            for (int y = 0; y < ry; y++)
            {
                int row = rx * (y + ry * z);
                for (int x = 0; x < rx; x++)
                {
                    int i = row + x;
                    if (s_Solid[i] > 0.5f)
                        continue;

                    float best = spread[i];
                    for (int k = 0; k < n; k++)
                    {
                        int nx = x + ox[k], ny = y + oy[k], nz = z + oz[k];
                        if ((uint)nx >= rx || (uint)ny >= ry || (uint)nz >= rz)
                            continue;
                        float cand = spread[i + off[k]] + wgt[k];
                        if (cand < best)
                            best = cand;
                    }
                    spread[i] = best;
                }
            }

            // backward sweep closes the diagonals the forward pass cannot see
            for (int z = rz - 1; z >= 0; z--)
            for (int y = ry - 1; y >= 0; y--)
            {
                int row = rx * (y + ry * z);
                for (int x = rx - 1; x >= 0; x--)
                {
                    int i = row + x;
                    if (s_Solid[i] > 0.5f)
                        continue;

                    float best = spread[i];
                    for (int k = 0; k < n; k++)
                    {
                        int nx = x - ox[k], ny = y - oy[k], nz = z - oz[k];
                        if ((uint)nx >= rx || (uint)ny >= ry || (uint)nz >= rz)
                            continue;
                        float cand = spread[i - off[k]] + wgt[k];
                        if (cand < best)
                            best = cand;
                    }
                    spread[i] = best;
                }
            }
        }

        static int NearestFreeCell(int sx, int sy, int sz)
        {
            int rx = s_Res.x, ry = s_Res.y, rz = s_Res.z;
            for (int r = 1; r < 6; r++)
            {
                for (int z = sz - r; z <= sz + r; z++)
                for (int y = sy - r; y <= sy + r; y++)
                for (int x = sx - r; x <= sx + r; x++)
                {
                    if (x < 0 || y < 0 || z < 0 || x >= rx || y >= ry || z >= rz)
                        continue;
                    int i = x + rx * (y + ry * z);
                    if (s_Solid[i] < 0.5f)
                        return i;
                }
            }
            return -1;
        }
    }
}
