using UnityEngine;

namespace ProjectName.Terrain
{
    /// <summary>
    /// Advanced Heightmap Processing Engine:
    /// 1. Image loading (PNG/JPG) with resampled grayscale elevation mapping
    /// 2. Configurable Gaussian blur smoothing kernel (anti-staircasing)
    /// 3. Height remapping curves (Linear, Exponential, Ridge Peak, Basin Inversion)
    /// 4. Procedural planetary presets (Gale Crater, Olympus Mons, Shackleton, Valles Marineris, Polar Ice, Fractal)
    /// 5. Interactive 2D canvas brush painting (Raise, Lower, Smooth, Flatten)
    /// 6. Live preview texture generation with planetary tinting
    /// </summary>
    public static class HeightmapLoader
    {
        // -------------------------------------------------------------
        // Backward-compatible overload
        // -------------------------------------------------------------
        public static float[,] LoadHeightsFromImage(string filePath, int resolution)
        {
            return LoadHeightsFromImage(filePath, resolution, 0f, HeightRemapCurve.Linear);
        }

        // -------------------------------------------------------------
        // Full Image Loader with Smoothing and Remapping
        // -------------------------------------------------------------
        public static float[,] LoadHeightsFromImage(string filePath, int resolution, float smoothingFactor, HeightRemapCurve heightCurve)
        {
            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogError($"[HeightmapLoader] File not found: {filePath}");
                return null;
            }

            byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);
            Texture2D sourceTexture = new Texture2D(2, 2);
            if (!sourceTexture.LoadImage(fileBytes))
            {
                Debug.LogError($"[HeightmapLoader] Failed to decode image: {filePath}");
                return null;
            }

            float[,] heights = new float[resolution, resolution];

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = (float)x / (resolution - 1);
                    float v = (float)y / (resolution - 1);

                    Color pixelColor = sourceTexture.GetPixelBilinear(u, v);
                    float rawH = pixelColor.grayscale;

                    rawH = ApplyHeightRemap(rawH, heightCurve);
                    heights[y, x] = rawH;
                }
            }

            Object.DestroyImmediate(sourceTexture);

            if (smoothingFactor > 0.05f)
            {
                heights = ApplyGaussianSmoothing(heights, resolution, smoothingFactor);
            }

            return heights;
        }

        // -------------------------------------------------------------
        // Height Remapping Curves
        // -------------------------------------------------------------
        public static float ApplyHeightRemap(float rawH, HeightRemapCurve curve)
        {
            rawH = Mathf.Clamp01(rawH);
            switch (curve)
            {
                case HeightRemapCurve.Exponential:
                    return Mathf.Pow(rawH, 2.2f);

                case HeightRemapCurve.RidgePeak:
                    // Converts rolling hills into sharp knife-edge ridges
                    return 1.0f - Mathf.Abs(rawH * 2.0f - 1.0f);

                case HeightRemapCurve.BasinInversion:
                    // Inverts elevation to form crater basins and trenches
                    return Mathf.Pow(1.0f - rawH, 1.8f);

                case HeightRemapCurve.Linear:
                default:
                    return rawH;
            }
        }

        // -------------------------------------------------------------
        // Configurable Gaussian Blur Smoothing Filter
        // Eliminates 8-bit image staircasing and stepping artifacts
        // -------------------------------------------------------------
        public static float[,] ApplyGaussianSmoothing(float[,] input, int size, float factor)
        {
            if (factor <= 0.05f) return (float[,])input.Clone();

            float[,] result = new float[size, size];
            int iterations = Mathf.Clamp(Mathf.CeilToInt(factor * 2f), 1, 5);
            float[,] current = (float[,])input.Clone();

            for (int iter = 0; iter < iterations; iter++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float sum = 0f;
                        float weightSum = 0f;

                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = Mathf.Clamp(x + dx, 0, size - 1);
                                int ny = Mathf.Clamp(y + dy, 0, size - 1);

                                float w = (dx == 0 && dy == 0) ? 4f : ((dx == 0 || dy == 0) ? 2f : 1f);
                                sum += current[ny, nx] * w;
                                weightSum += w;
                            }
                        }

                        result[y, x] = sum / weightSum;
                    }
                }

                current = (float[,])result.Clone();
            }

            return result;
        }

        // -------------------------------------------------------------
        // Procedural Planetary Preset Heightmap Generators
        // -------------------------------------------------------------
        public static float[,] GeneratePresetHeights(HeightmapPreset preset, int resolution, float smoothingFactor = 1.0f, HeightRemapCurve curve = HeightRemapCurve.Linear)
        {
            return GeneratePresetHeights(preset, resolution, smoothingFactor, curve, 0);
        }

        public static float[,] GeneratePresetHeights(HeightmapPreset preset, int resolution, float smoothingFactor, HeightRemapCurve curve, int seed)
        {
            float[,] heights = new float[resolution, resolution];

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = (float)x / (resolution - 1);
                    float v = (float)y / (resolution - 1);

                    // Centered coordinates [-1, 1]
                    float cx = (u - 0.5f) * 2f;
                    float cy = (v - 0.5f) * 2f;
                    float r = Mathf.Sqrt(cx * cx + cy * cy);

                    float h = 0f;

                    switch (preset)
                    {
                        case HeightmapPreset.GaleCrater:
                            h = EvaluateGaleCrater(r, cx, cy, u, v);
                            break;

                        case HeightmapPreset.OlympusMons:
                            h = EvaluateOlympusMons(r, cx, cy, u, v);
                            break;

                        case HeightmapPreset.ShackletonCrater:
                            h = EvaluateShackletonCrater(r, cx, cy, u, v);
                            break;

                        case HeightmapPreset.VallesMarineris:
                            h = EvaluateVallesMarineris(cx, cy, u, v);
                            break;

                        case HeightmapPreset.PolarIce:
                            h = EvaluatePolarIce(u, v);
                            break;

                        case HeightmapPreset.Plains:
                            h = EvaluatePlains(u, v, seed);
                            break;

                        case HeightmapPreset.ProceduralFractal:
                        default:
                            h = EvaluateFractalNoise(u, v, seed);
                            break;
                    }

                    h = ApplyHeightRemap(h, curve);
                    heights[y, x] = Mathf.Clamp01(h);
                }
            }

            if (smoothingFactor > 0.05f)
            {
                heights = ApplyGaussianSmoothing(heights, resolution, smoothingFactor);
            }

            // Smoothly taper border edges to zero elevation to blend seamlessly with the horizon (fixes E9)
            heights = ApplyEdgeFade(heights, resolution, 0.08f);

            return heights;
        }

        /// <summary>
        /// Smoothly eases heights to baseline zero at the extreme outer perimeter of the terrain.
        /// Eliminates square box cliff edges on the horizon (fixes E9).
        /// </summary>
        public static float[,] ApplyEdgeFade(float[,] heights, int resolution, float edgeFraction = 0.08f)
        {
            if (heights == null || resolution <= 1 || edgeFraction <= 0f) return heights;

            int edgePixels = Mathf.Max(2, Mathf.RoundToInt(resolution * edgeFraction));

            for (int y = 0; y < resolution; y++)
            {
                int distY = Mathf.Min(y, resolution - 1 - y);
                float fadeY = distY < edgePixels ? (float)distY / edgePixels : 1.0f;
                fadeY = fadeY * fadeY * (3f - 2f * fadeY); // Smooth cubic easing

                for (int x = 0; x < resolution; x++)
                {
                    int distX = Mathf.Min(x, resolution - 1 - x);
                    float fadeX = distX < edgePixels ? (float)distX / edgePixels : 1.0f;
                    fadeX = fadeX * fadeX * (3f - 2f * fadeX); // Smooth cubic easing

                    float factor = fadeX * fadeY;
                    heights[y, x] *= factor;
                }
            }

            return heights;
        }

        private static float EvaluateGaleCrater(float r, float cx, float cy, float u, float v)
        {
            // Crater floor (flat bowl) + raised rim + central peak (Mount Sharp) + micro noise
            float basePlane = 0.25f;
            float rimRadius = 0.70f;
            float rimWidth = 0.15f;

            // Raised rim
            float rimDist = Mathf.Abs(r - rimRadius);
            float rimHeight = Mathf.Exp(-Mathf.Pow(rimDist / rimWidth, 2f)) * 0.45f;

            // Crater inner floor depression
            float bowl = (r < rimRadius) ? (1.0f - Mathf.Pow(r / rimRadius, 2f)) * -0.22f : 0f;

            // Central peak (Mount Sharp / Aeolis Mons)
            float peakRadius = 0.22f;
            float centralPeak = (r < 0.35f) ? Mathf.Exp(-Mathf.Pow(r / peakRadius, 2f)) * 0.55f : 0f;

            // Micro-rock roughness noise
            float noise = Mathf.PerlinNoise(u * 12f, v * 12f) * 0.08f + Mathf.PerlinNoise(u * 28f, v * 28f) * 0.04f;

            return Mathf.Clamp01(basePlane + rimHeight + bowl + centralPeak + noise);
        }

        private static float EvaluateOlympusMons(float r, float cx, float cy, float u, float v)
        {
            // Massive volcanic shield + basal scarp cliff + summit caldera depression
            float shield = Mathf.Exp(-Mathf.Pow(r / 0.65f, 1.8f)) * 0.85f;

            // Summit caldera (nested collapse craters)
            float caldera = (r < 0.18f) ? (1.0f - Mathf.Pow(r / 0.18f, 2f)) * -0.25f : 0f;

            // Radial lava flows
            float angle = Mathf.Atan2(cy, cx);
            float lavaRidges = Mathf.Sin(angle * 14f + r * 10f) * 0.035f * Mathf.Clamp01(r);

            // Flank micro-roughness
            float noise = Mathf.PerlinNoise(u * 10f, v * 10f) * 0.05f;

            return Mathf.Clamp01(shield + caldera + lavaRidges + noise);
        }

        private static float EvaluateShackletonCrater(float r, float cx, float cy, float u, float v)
        {
            // Steep lunar impact rim + deep bowl + micro-craters
            float rimRadius = 0.60f;
            float rimWidth = 0.12f;
            float rim = Mathf.Exp(-Mathf.Pow(Mathf.Abs(r - rimRadius) / rimWidth, 2f)) * 0.55f;
            float floor = (r < rimRadius) ? -0.35f * (1.0f - Mathf.Pow(r / rimRadius, 3f)) : 0f;

            // Impact noise & ejecta rays
            float noise = Mathf.PerlinNoise(u * 16f, v * 16f) * 0.10f + Mathf.PerlinNoise(u * 40f, v * 40f) * 0.04f;

            return Mathf.Clamp01(0.35f + rim + floor + noise);
        }

        private static float EvaluateVallesMarineris(float cx, float cy, float u, float v)
        {
            // Grand tectonic chasm running longitudinally
            float canyonCenter = Mathf.Sin(cx * 2.5f) * 0.12f;
            float distFromChasm = Mathf.Abs(cy - canyonCenter);

            // Canyon wall terraces
            float chasmWidth = 0.35f;
            float depth = 0f;
            if (distFromChasm < chasmWidth)
            {
                float t = distFromChasm / chasmWidth;
                // Stepped terrace cliff profile
                depth = -0.55f * (1.0f - Mathf.Pow(t, 2f));
                depth += Mathf.Sin(t * Mathf.PI * 4f) * 0.04f; // Terraces
            }

            // Plateau background
            float plateau = 0.65f;
            float noise = Mathf.PerlinNoise(u * 8f, v * 8f) * 0.08f + Mathf.PerlinNoise(u * 32f, v * 32f) * 0.03f;

            return Mathf.Clamp01(plateau + depth + noise);
        }

        private static float EvaluatePolarIce(float u, float v)
        {
            // Wind-sculpted sastrugi ripples and smooth glacial dunes
            float ripple1 = Mathf.Sin(u * 22f + v * 8f) * 0.08f;
            float ripple2 = Mathf.Sin(u * 14f - v * 18f) * 0.05f;
            float broadDome = Mathf.PerlinNoise(u * 3f, v * 3f) * 0.40f + 0.35f;
            float microIce = Mathf.PerlinNoise(u * 45f, v * 45f) * 0.03f;

            return Mathf.Clamp01(broadDome + ripple1 + ripple2 + microIce);
        }

        private static float EvaluatePlains(float u, float v, int seed = 0)
        {
            float sU = u + seed * 1.4142f;
            float sV = v + seed * 1.7320f;
            float lowRoll = Mathf.PerlinNoise(sU * 1.8f + 11.2f, sV * 1.8f + 13.5f) * 0.18f;
            float gentleSlope = (u * 0.05f) + 0.35f;
            float microRough = Mathf.PerlinNoise(sU * 12f, sV * 12f) * 0.03f;
            return Mathf.Clamp01(gentleSlope + lowRoll + microRough);
        }

        private static float EvaluateFractalNoise(float u, float v, int seed = 0)
        {
            // 5-octave fractal Brownian motion (fBm) with seed offset
            float total = 0f;
            float frequency = 2.5f;
            float amplitude = 0.5f;
            float maxVal = 0f;

            float sU = u + seed * 1.3719f;
            float sV = v + seed * 2.1931f;

            for (int o = 0; o < 5; o++)
            {
                total += Mathf.PerlinNoise(sU * frequency + 17.3f, sV * frequency + 31.7f) * amplitude;
                maxVal += amplitude;
                frequency *= 2.05f;
                amplitude *= 0.5f;
            }

            return total / maxVal;
        }

        // -------------------------------------------------------------
        // Interactive 2D Heightmap Canvas Brush Painting
        // -------------------------------------------------------------
        public static void ApplyBrush(float[,] heights, int resolution, Vector2 uv, BrushMode mode, float radiusNorm, float strength, float targetHeight = 0.5f)
        {
            ApplyBrush(heights, resolution, uv, mode, radiusNorm, strength, 0.5f, targetHeight, 0);
        }

        public static RectInt ApplyBrush(
            float[,] heights,
            int resolution,
            Vector2 uv,
            BrushMode mode,
            float radiusNorm,
            float strength,
            float hardness,
            float targetHeight,
            int seed = 0)
        {
            if (heights == null) return new RectInt(0, 0, 0, 0);

            int centerX = Mathf.RoundToInt(uv.x * (resolution - 1));
            int centerY = Mathf.RoundToInt(uv.y * (resolution - 1));
            int radiusPixels = Mathf.Max(1, Mathf.RoundToInt(radiusNorm * resolution));

            int minX = Mathf.Max(0, centerX - radiusPixels);
            int maxX = Mathf.Min(resolution - 1, centerX + radiusPixels);
            int minY = Mathf.Max(0, centerY - radiusPixels);
            int maxY = Mathf.Min(resolution - 1, centerY + radiusPixels);

            for (int py = minY; py <= maxY; py++)
            {
                int dy = py - centerY;
                for (int px = minX; px <= maxX; px++)
                {
                    int dx = px - centerX;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > radiusPixels) continue;

                    // Cosine falloff shaped by hardness parameter
                    float d = dist / radiusPixels; // [0, 1]
                    float falloff = 1f;
                    if (d > hardness)
                    {
                        float t = (d - hardness) / Mathf.Max(0.001f, 1f - hardness);
                        falloff = (Mathf.Cos(t * Mathf.PI) + 1f) * 0.5f;
                    }

                    float delta = falloff * strength * 0.08f;

                    switch (mode)
                    {
                        case BrushMode.Raise:
                            heights[py, px] = Mathf.Clamp01(heights[py, px] + delta);
                            break;

                        case BrushMode.Lower:
                            heights[py, px] = Mathf.Clamp01(heights[py, px] - delta);
                            break;

                        case BrushMode.Smooth:
                            float avg = 0f;
                            int count = 0;
                            for (int sy = -1; sy <= 1; sy++)
                            {
                                int ny = Mathf.Clamp(py + sy, 0, resolution - 1);
                                for (int sx = -1; sx <= 1; sx++)
                                {
                                    int nx = Mathf.Clamp(px + sx, 0, resolution - 1);
                                    avg += heights[ny, nx];
                                    count++;
                                }
                            }
                            avg /= count;
                            heights[py, px] = Mathf.Lerp(heights[py, px], avg, falloff * strength * 0.5f);
                            break;

                        case BrushMode.Flatten:
                            heights[py, px] = Mathf.Lerp(heights[py, px], targetHeight, falloff * strength * 0.5f);
                            break;

                        case BrushMode.Noise:
                            float n = (Mathf.PerlinNoise((px + seed * 17.3f) * 0.15f, (py + seed * 23.7f) * 0.15f) - 0.5f) * 2f;
                            heights[py, px] = Mathf.Clamp01(heights[py, px] + n * delta);
                            break;
                    }
                }
            }

            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        // -------------------------------------------------------------
        // Bilinear Array Resampling for Resolution Switching
        // -------------------------------------------------------------
        public static float[,] ResampleHeights(float[,] source, int newRes)
        {
            if (source == null) return null;
            int oldH = source.GetLength(0);
            int oldW = source.GetLength(1);
            if (oldH == newRes && oldW == newRes) return (float[,])source.Clone();

            float[,] resampled = new float[newRes, newRes];
            for (int y = 0; y < newRes; y++)
            {
                float v = (float)y / (newRes - 1);
                float oldY = v * (oldH - 1);
                int y0 = Mathf.FloorToInt(oldY);
                int y1 = Mathf.Min(oldH - 1, y0 + 1);
                float yf = oldY - y0;

                for (int x = 0; x < newRes; x++)
                {
                    float u = (float)x / (newRes - 1);
                    float oldX = u * (oldW - 1);
                    int x0 = Mathf.FloorToInt(oldX);
                    int x1 = Mathf.Min(oldW - 1, x0 + 1);
                    float xf = oldX - x0;

                    float h00 = source[y0, x0];
                    float h10 = source[y0, x1];
                    float h01 = source[y1, x0];
                    float h11 = source[y1, x1];

                    float h0 = Mathf.Lerp(h00, h10, xf);
                    float h1 = Mathf.Lerp(h01, h11, xf);
                    resampled[y, x] = Mathf.Lerp(h0, h1, yf);
                }
            }
            return resampled;
        }

        // -------------------------------------------------------------
        // Live Preview Texture Generation & Dirty-Rect Updates
        // -------------------------------------------------------------
        public static Texture2D CreatePreviewTexture(float[,] heights, int resolution, PlanetaryMaterialType materialType, Texture2D existingTex = null)
        {
            int previewRes = Mathf.Min(resolution, 256);
            Texture2D tex = existingTex;
            if (tex == null || tex.width != previewRes || tex.height != previewRes)
            {
                if (tex != null) Object.DestroyImmediate(tex);
                tex = new Texture2D(previewRes, previewRes, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
            }

            Color[] pixels = new Color[previewRes * previewRes];

            for (int y = 0; y < previewRes; y++)
            {
                float v = (float)y / (previewRes - 1);
                int srcY = Mathf.RoundToInt(v * (resolution - 1));

                for (int x = 0; x < previewRes; x++)
                {
                    float u = (float)x / (previewRes - 1);
                    int srcX = Mathf.RoundToInt(u * (resolution - 1));

                    float h = heights[srcY, srcX];
                    pixels[y * previewRes + x] = GetTintedPixelColor(h, srcX, srcY, heights, resolution, materialType);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false);
            return tex;
        }

        public static void UpdatePreviewTextureRect(Texture2D tex, float[,] heights, RectInt arrayRect, int resolution, PlanetaryMaterialType materialType)
        {
            if (tex == null || heights == null || arrayRect.width <= 0 || arrayRect.height <= 0) return;
            int previewRes = tex.width;
            if (previewRes <= 0) return;

            float scale = (float)(previewRes - 1) / Mathf.Max(1, resolution - 1);
            int pxMin = Mathf.Clamp(Mathf.FloorToInt(arrayRect.xMin * scale), 0, previewRes - 1);
            int pxMax = Mathf.Clamp(Mathf.CeilToInt(arrayRect.xMax * scale), 0, previewRes - 1);
            int pyMin = Mathf.Clamp(Mathf.FloorToInt(arrayRect.yMin * scale), 0, previewRes - 1);
            int pyMax = Mathf.Clamp(Mathf.CeilToInt(arrayRect.yMax * scale), 0, previewRes - 1);

            int pw = pxMax - pxMin + 1;
            int ph = pyMax - pyMin + 1;
            if (pw <= 0 || ph <= 0) return;

            Color[] blockColors = new Color[pw * ph];
            for (int py = 0; py < ph; py++)
            {
                int y = pyMin + py;
                float v = (float)y / (previewRes - 1);
                int srcY = Mathf.Clamp(Mathf.RoundToInt(v * (resolution - 1)), 0, resolution - 1);

                for (int px = 0; px < pw; px++)
                {
                    int x = pxMin + px;
                    float u = (float)x / (previewRes - 1);
                    int srcX = Mathf.Clamp(Mathf.RoundToInt(u * (resolution - 1)), 0, resolution - 1);

                    float h = heights[srcY, srcX];
                    blockColors[py * pw + px] = GetTintedPixelColor(h, srcX, srcY, heights, resolution, materialType);
                }
            }

            tex.SetPixels(pxMin, pyMin, pw, ph, blockColors);
            tex.Apply(false);
        }

        public static Color GetTintedPixelColor(float h, int srcX, int srcY, float[,] heights, int resolution, PlanetaryMaterialType materialType)
        {
            // Calculate directional topographic hillshading from height gradients
            int prevX = Mathf.Max(0, srcX - 1);
            int nextX = Mathf.Min(resolution - 1, srcX + 1);
            int prevY = Mathf.Max(0, srcY - 1);
            int nextY = Mathf.Min(resolution - 1, srcY + 1);

            float dhX = (heights[srcY, nextX] - heights[srcY, prevX]) * 8f;
            float dhY = (heights[nextY, srcX] - heights[prevY, srcX]) * 8f;
            Vector3 normal = new Vector3(-dhX, 1f, -dhY).normalized;
            Vector3 lightDir = new Vector3(-0.577f, 0.707f, 0.408f); // NW illumination
            float hillshade = Mathf.Clamp01((1f + Vector3.Dot(normal, lightDir)) * 0.5f);
            float shade = 0.60f + (hillshade * 0.50f);

            Color baseCol;
            switch (materialType)
            {
                case PlanetaryMaterialType.MartianDust:
                    baseCol = new Color(h * 0.85f + 0.15f, h * 0.40f + 0.08f, h * 0.20f + 0.04f, 1f);
                    break;

                case PlanetaryMaterialType.LunarRegolith:
                    baseCol = new Color(h * 0.65f + 0.20f, h * 0.65f + 0.20f, h * 0.70f + 0.22f, 1f);
                    break;

                case PlanetaryMaterialType.VolcanicBasalt:
                    baseCol = new Color(h * 0.35f + 0.08f, h * 0.35f + 0.08f, h * 0.40f + 0.09f, 1f);
                    break;

                case PlanetaryMaterialType.PolarIce:
                    baseCol = new Color(h * 0.50f + 0.45f, h * 0.65f + 0.35f, h * 0.80f + 0.20f, 1f);
                    break;

                case PlanetaryMaterialType.RedCanyon:
                    float band = Mathf.Sin(h * 20f) * 0.1f;
                    baseCol = new Color(h * 0.75f + 0.20f + band, h * 0.30f + 0.10f, h * 0.15f + 0.05f, 1f);
                    break;

                case PlanetaryMaterialType.TopographicWireframe:
                    float contour = Mathf.Abs(Mathf.Sin(h * 30f)) > 0.85f ? 1f : 0.15f;
                    return new Color(0f, contour, contour * 0.75f, 1f);

                case PlanetaryMaterialType.NormalInspector:
                    return new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);

                default:
                    baseCol = new Color(h, h, h, 1f);
                    break;
            }

            return new Color(baseCol.r * shade, baseCol.g * shade, baseCol.b * shade, 1f);
        }
    }
}
