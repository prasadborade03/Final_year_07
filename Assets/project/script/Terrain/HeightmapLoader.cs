using UnityEngine;

namespace ProjectName.Terrain
{
    /// <summary>
    /// Job: Load an image file and convert it into a height array
    /// that Unity's Terrain system can understand.
    ///
    /// This script does NOT know about UI, file browsers, or the Terrain
    /// GameObject itself. It only converts: image -> float[,] heights.
    /// Keeping it this "dumb" makes it easy to test and reuse later.
    /// </summary>
    public static class HeightmapLoader
    {
        /// <summary>
        /// Reads a PNG/JPG file from disk and returns a 2D array of
        /// height values between 0 and 1 (0 = black = lowest point,
        /// 1 = white = highest point).
        /// </summary>
        /// <param name="filePath">Full path to the image file on disk.</param>
        /// <param name="resolution">
        /// The size we want the height array to be (must be a valid
        /// Unity terrain resolution, e.g. 513 or 1025).
        /// </param>
        public static float[,] LoadHeightsFromImage(string filePath, int resolution)
        {
            // Step 1: Read the raw file bytes from disk.
            byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);

            // Step 2: Create an empty texture, then fill it with the image data.
            // The size (2,2) here is a placeholder -- LoadImage() will
            // automatically resize it to match the actual image.
            Texture2D sourceTexture = new Texture2D(2, 2);
            sourceTexture.LoadImage(fileBytes);

            // Step 3: If the image isn't already the resolution we want,
            // we resample it. For V1 we keep this simple: we just sample
            // proportionally, which is good enough at this stage.
            float[,] heights = new float[resolution, resolution];

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    // Map our (x,y) in the height array to a pixel
                    // coordinate in the source image, even if sizes differ.
                    float normalizedX = (float)x / (resolution - 1);
                    float normalizedY = (float)y / (resolution - 1);

                    int pixelX = Mathf.RoundToInt(normalizedX * (sourceTexture.width - 1));
                    int pixelY = Mathf.RoundToInt(normalizedY * (sourceTexture.height - 1));

                    Color pixelColor = sourceTexture.GetPixel(pixelX, pixelY);

                    // grayscale brightness: since it's black & white,
                    // R, G, and B are the same value, so reading just
                    // .grayscale gives us a clean 0-1 height value.
                    heights[y, x] = pixelColor.grayscale;
                }
            }

            return heights;
        }
    }
}