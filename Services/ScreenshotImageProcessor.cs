using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace StreamGuard.Services
{
    public static class ScreenshotImageProcessor
    {
        public const int MinDimension = 4;
        public const int MaxDimension = 7680;

        public static Bitmap Crop(Bitmap source, Rectangle cropRect)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var bounds = new Rectangle(0, 0, source.Width, source.Height);
            cropRect.Intersect(bounds);

            if (cropRect.Width < MinDimension || cropRect.Height < MinDimension)
                throw new ArgumentException("Crop region must be at least 4x4 pixels.", nameof(cropRect));

            return source.Clone(cropRect, PixelFormat.Format32bppArgb);
        }

        public static Bitmap RotateFlip(Bitmap source, RotateFlipType rotateFlipType)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy))
            {
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }
            copy.RotateFlip(rotateFlipType);
            return copy;
        }

        public static Bitmap Resize(Bitmap source, int targetWidth, int targetHeight)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            int width = Math.Clamp(targetWidth, MinDimension, MaxDimension);
            int height = Math.Clamp(targetHeight, MinDimension, MaxDimension);

            var dest = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            dest.SetResolution(source.HorizontalResolution, source.VerticalResolution);
            using (var g = Graphics.FromImage(dest))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using var wrapMode = new ImageAttributes();
                wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrapMode);
            }
            return dest;
        }

        public static Bitmap ApplyRegionBlur(Bitmap source, Rectangle region, int strength = 12, bool pixelate = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var result = CloneAsArgb32(source);
            var bounds = new Rectangle(0, 0, result.Width, result.Height);
            region.Intersect(bounds);

            if (region.Width < 2 || region.Height < 2)
                return result;

            int clampedStrength = Math.Clamp(strength, 2, 60);
            if (pixelate)
            {
                ApplyPixelateInPlace(result, region, clampedStrength);
            }
            else
            {
                ApplyBoxBlurInPlace(result, region, clampedStrength);
            }

            return result;
        }

        public static Bitmap ApplyFullBlur(Bitmap source, int strength = 10, bool pixelate = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return ApplyRegionBlur(source, new Rectangle(0, 0, source.Width, source.Height), strength, pixelate);
        }

        public static Bitmap CloneAsArgb32(Bitmap source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            copy.SetResolution(source.HorizontalResolution, source.VerticalResolution);
            using (var g = Graphics.FromImage(copy))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }
            return copy;
        }

        private static void ApplyPixelateInPlace(Bitmap bitmap, Rectangle region, int blockSize)
        {
            var data = bitmap.LockBits(region, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int byteCount = stride * region.Height;
                byte[] buffer = new byte[byteCount];
                Marshal.Copy(data.Scan0, buffer, 0, byteCount);

                for (int by = 0; by < region.Height; by += blockSize)
                {
                    int maxY = Math.Min(by + blockSize, region.Height);
                    for (int bx = 0; bx < region.Width; bx += blockSize)
                    {
                        int maxX = Math.Min(bx + blockSize, region.Width);
                        long sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                        int count = 0;

                        for (int y = by; y < maxY; y++)
                        {
                            int rowOffset = y * stride;
                            for (int x = bx; x < maxX; x++)
                            {
                                int idx = rowOffset + x * 4;
                                sumB += buffer[idx];
                                sumG += buffer[idx + 1];
                                sumR += buffer[idx + 2];
                                sumA += buffer[idx + 3];
                                count++;
                            }
                        }

                        if (count == 0) continue;
                        byte avgB = (byte)(sumB / count);
                        byte avgG = (byte)(sumG / count);
                        byte avgR = (byte)(sumR / count);
                        byte avgA = (byte)(sumA / count);

                        for (int y = by; y < maxY; y++)
                        {
                            int rowOffset = y * stride;
                            for (int x = bx; x < maxX; x++)
                            {
                                int idx = rowOffset + x * 4;
                                buffer[idx] = avgB;
                                buffer[idx + 1] = avgG;
                                buffer[idx + 2] = avgR;
                                buffer[idx + 3] = avgA;
                            }
                        }
                    }
                }

                Marshal.Copy(buffer, 0, data.Scan0, byteCount);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static void ApplyBoxBlurInPlace(Bitmap bitmap, Rectangle region, int radius)
        {
            var data = bitmap.LockBits(region, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int w = region.Width;
                int h = region.Height;
                int stride = data.Stride;
                int byteCount = stride * h;
                byte[] src = new byte[byteCount];
                byte[] tmp = new byte[byteCount];
                byte[] dst = new byte[byteCount];
                Marshal.Copy(data.Scan0, src, 0, byteCount);

                int[] colSumB = new int[w];
                int[] colSumG = new int[w];
                int[] colSumR = new int[w];
                int[] colSumA = new int[w];

                // O(W*H) sliding-window separable box blur (2 passes approximate Gaussian cleanly without O(R) inner loops)
                for (int pass = 0; pass < 2; pass++)
                {
                    // Horizontal sliding-window pass: src -> tmp
                    int initXEnd = Math.Min(w - 1, radius);
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * stride;
                        int sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                        for (int kx = 0; kx <= initXEnd; kx++)
                        {
                            int idx = row + kx * 4;
                            sumB += src[idx];
                            sumG += src[idx + 1];
                            sumR += src[idx + 2];
                            sumA += src[idx + 3];
                        }

                        int count = initXEnd + 1;
                        tmp[row] = (byte)(sumB / count);
                        tmp[row + 1] = (byte)(sumG / count);
                        tmp[row + 2] = (byte)(sumR / count);
                        tmp[row + 3] = (byte)(sumA / count);

                        for (int x = 1; x < w; x++)
                        {
                            int enterX = x + radius;
                            if (enterX < w)
                            {
                                int eIdx = row + enterX * 4;
                                sumB += src[eIdx];
                                sumG += src[eIdx + 1];
                                sumR += src[eIdx + 2];
                                sumA += src[eIdx + 3];
                                count++;
                            }

                            int leaveX = x - radius - 1;
                            if (leaveX >= 0)
                            {
                                int lIdx = row + leaveX * 4;
                                sumB -= src[lIdx];
                                sumG -= src[lIdx + 1];
                                sumR -= src[lIdx + 2];
                                sumA -= src[lIdx + 3];
                                count--;
                            }

                            int outIdx = row + x * 4;
                            tmp[outIdx] = (byte)(sumB / count);
                            tmp[outIdx + 1] = (byte)(sumG / count);
                            tmp[outIdx + 2] = (byte)(sumR / count);
                            tmp[outIdx + 3] = (byte)(sumA / count);
                        }
                    }

                    // Row-major sequential vertical sliding-window pass: tmp -> dst
                    Array.Clear(colSumB, 0, w);
                    Array.Clear(colSumG, 0, w);
                    Array.Clear(colSumR, 0, w);
                    Array.Clear(colSumA, 0, w);

                    int initYEnd = Math.Min(h - 1, radius);
                    for (int ky = 0; ky <= initYEnd; ky++)
                    {
                        int row = ky * stride;
                        for (int x = 0; x < w; x++)
                        {
                            int idx = row + x * 4;
                            colSumB[x] += tmp[idx];
                            colSumG[x] += tmp[idx + 1];
                            colSumR[x] += tmp[idx + 2];
                            colSumA[x] += tmp[idx + 3];
                        }
                    }

                    int yCount = initYEnd + 1;
                    for (int x = 0; x < w; x++)
                    {
                        int outIdx = x * 4;
                        dst[outIdx] = (byte)(colSumB[x] / yCount);
                        dst[outIdx + 1] = (byte)(colSumG[x] / yCount);
                        dst[outIdx + 2] = (byte)(colSumR[x] / yCount);
                        dst[outIdx + 3] = (byte)(colSumA[x] / yCount);
                    }

                    for (int y = 1; y < h; y++)
                    {
                        int enterY = y + radius;
                        if (enterY < h)
                        {
                            int eRow = enterY * stride;
                            for (int x = 0; x < w; x++)
                            {
                                int eIdx = eRow + x * 4;
                                colSumB[x] += tmp[eIdx];
                                colSumG[x] += tmp[eIdx + 1];
                                colSumR[x] += tmp[eIdx + 2];
                                colSumA[x] += tmp[eIdx + 3];
                            }
                            yCount++;
                        }

                        int leaveY = y - radius - 1;
                        if (leaveY >= 0)
                        {
                            int lRow = leaveY * stride;
                            for (int x = 0; x < w; x++)
                            {
                                int lIdx = lRow + x * 4;
                                colSumB[x] -= tmp[lIdx];
                                colSumG[x] -= tmp[lIdx + 1];
                                colSumR[x] -= tmp[lIdx + 2];
                                colSumA[x] -= tmp[lIdx + 3];
                            }
                            yCount--;
                        }

                        int outRow = y * stride;
                        for (int x = 0; x < w; x++)
                        {
                            int outIdx = outRow + x * 4;
                            dst[outIdx] = (byte)(colSumB[x] / yCount);
                            dst[outIdx + 1] = (byte)(colSumG[x] / yCount);
                            dst[outIdx + 2] = (byte)(colSumR[x] / yCount);
                            dst[outIdx + 3] = (byte)(colSumA[x] / yCount);
                        }
                    }

                    if (pass == 0)
                    {
                        Buffer.BlockCopy(dst, 0, src, 0, byteCount);
                    }
                }

                Marshal.Copy(dst, 0, data.Scan0, byteCount);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
