using System;

namespace ASCOM.ScdouglasFujifilm.Camera.Core
{
    internal static class ActiveFrameCropper
    {
        // Keep Bayer phase unchanged when trimming padding around the active frame.
        internal static int EvenCenteredOffset(int sourceSize, int targetSize)
        {
            if (targetSize <= 0 || sourceSize < targetSize)
                throw new InvalidOperationException($"Cannot crop {sourceSize} pixels to {targetSize} pixels.");

            return ((sourceSize - targetSize) / 2) & ~1;
        }

        internal static int[,] ToAscomArray(ushort[,] source, int targetWidth, int targetHeight,
            out int left, out int top)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            int sourceHeight = source.GetLength(0);
            int sourceWidth = source.GetLength(1);
            left = EvenCenteredOffset(sourceWidth, targetWidth);
            top = EvenCenteredOffset(sourceHeight, targetHeight);

            var result = new int[targetWidth, targetHeight];
            for (int y = 0; y < targetHeight; y++)
                for (int x = 0; x < targetWidth; x++)
                    result[x, y] = source[top + y, left + x];

            return result;
        }
    }
}
