using UnityEngine;

namespace SmokeVolumetric
{
    /// <summary>
    /// Generates (once) a tileable 3D fbm texture used as the smoke density field.
    /// Kept procedural so the demo ships without binary noise assets.
    /// </summary>
    public static class SmokeNoise
    {
        const int Size = 64;

        static Texture3D s_Texture;

        public static Texture3D Get()
        {
            if (s_Texture != null)
                return s_Texture;

            var tex = new Texture3D(Size, Size, Size, TextureFormat.R8, true)
            {
                name = "SmokeNoise3D",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };

            var voxels = new Color32[Size * Size * Size];
            for (int z = 0; z < Size; z++)
            {
                float fz = (z + 0.5f) / Size;
                for (int y = 0; y < Size; y++)
                {
                    float fy = (y + 0.5f) / Size;
                    for (int x = 0; x < Size; x++)
                    {
                        float fx = (x + 0.5f) / Size;

                        // three octaves, each tileable over the same [0,1) cube
                        float v = 0.55f * TileableNoise(fx, fy, fz, 4, 1337)
                                + 0.30f * TileableNoise(fx, fy, fz, 8, 7919)
                                + 0.15f * TileableNoise(fx, fy, fz, 16, 3571);

                        // contrast: keep mid-tones, push toward billowy blobs
                        v = Mathf.Clamp01((v - 0.30f) * 1.85f);
                        v = Mathf.SmoothStep(0f, 1f, v);

                        byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
                        voxels[(z * Size + y) * Size + x] = new Color32(b, b, b, 255);
                    }
                }
            }

            tex.SetPixels32(voxels);
            tex.Apply(false, false);

            s_Texture = tex;
            return s_Texture;
        }

        static float TileableNoise(float x, float y, float z, int period, int seed)
        {
            float px = x * period, py = y * period, pz = z * period;

            int ix = Mathf.FloorToInt(px), iy = Mathf.FloorToInt(py), iz = Mathf.FloorToInt(pz);
            float fx = Fade(px - ix), fy = Fade(py - iy), fz = Fade(pz - iz);

            float c000 = Hash(ix, iy, iz, period, seed);
            float c100 = Hash(ix + 1, iy, iz, period, seed);
            float c010 = Hash(ix, iy + 1, iz, period, seed);
            float c110 = Hash(ix + 1, iy + 1, iz, period, seed);
            float c001 = Hash(ix, iy, iz + 1, period, seed);
            float c101 = Hash(ix + 1, iy, iz + 1, period, seed);
            float c011 = Hash(ix, iy + 1, iz + 1, period, seed);
            float c111 = Hash(ix + 1, iy + 1, iz + 1, period, seed);

            float x00 = Mathf.Lerp(c000, c100, fx);
            float x10 = Mathf.Lerp(c010, c110, fx);
            float x01 = Mathf.Lerp(c001, c101, fx);
            float x11 = Mathf.Lerp(c011, c111, fx);

            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }

        static float Fade(float t) => t * t * (3f - 2f * t);

        static float Hash(int x, int y, int z, int period, int seed)
        {
            // wrap the lattice so the texture tiles seamlessly
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            z = ((z % period) + period) % period;

            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647 + seed * 1442695040);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFF) / 16777215f;
            }
        }
    }
}
