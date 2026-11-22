using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CraftGate.Services
{
    public static class SkinService
    {
        public static async Task<ImageSource> GetSkinAsync(string username, string localSkinPath, string version = "无可用版本")
        {
            if (string.IsNullOrWhiteSpace(username)) username = "Steve";

            if (!string.IsNullOrEmpty(localSkinPath) && File.Exists(localSkinPath))
            {
                try
                {
                    using (var stream = new FileStream(localSkinPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        var frame = decoder.Frames[0];
                        frame.Freeze();
                        if (frame.PixelWidth == 64 && (frame.PixelHeight == 64 || frame.PixelHeight == 32))
                        {
                            return ScaleBitmapNearestNeighbor(frame, 20);
                        }
                    }
                }
                catch { }
            }

            return await LoadNetworkOrDefaultAsync(username).ConfigureAwait(false);
        }

        public static async Task GenerateGameSkinAsync(string originalSkinPath, string savePath, string baseSkinPath = null)
        {
            if (!File.Exists(originalSkinPath)) return;

            try
            {
                BitmapSource overlay = null;
                using (var stream = new FileStream(originalSkinPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    overlay = decoder.Frames[0];
                    overlay.Freeze();
                }

                if (overlay != null && overlay.PixelWidth == 64 && (overlay.PixelHeight == 64 || overlay.PixelHeight == 32))
                {
                    BitmapSource baseSkin = null;
                    if (!string.IsNullOrEmpty(baseSkinPath) && File.Exists(baseSkinPath))
                    {
                        using (var stream = new FileStream(baseSkinPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                            baseSkin = decoder.Frames[0];
                            baseSkin.Freeze();
                        }
                    }

                    BitmapSource finalImage = baseSkin != null ? MergeSkin(baseSkin, overlay) : ForceSolidBackground(overlay);

                    Directory.CreateDirectory(Path.GetDirectoryName(savePath));

                    // 👇 终极修复：强制转换为 Bgr24（完全丢弃透明通道，避免渲染成黑炭）
                    var formatted = new FormatConvertedBitmap(finalImage, PixelFormats.Bgr24, null, 0);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(formatted));
                    using (var stream = new FileStream(savePath, FileMode.Create))
                    {
                        encoder.Save(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("生成游戏皮肤失败: " + ex.Message);
            }
        }

        private static BitmapSource MergeSkin(BitmapSource baseSkin, BitmapSource overlaySkin)
        {
            int width = baseSkin.PixelWidth;
            int height = baseSkin.PixelHeight;
            int stride = width * 4;

            var baseFormatted = new FormatConvertedBitmap(baseSkin, PixelFormats.Bgra32, null, 0);
            var overlayFormatted = new FormatConvertedBitmap(overlaySkin, PixelFormats.Bgra32, null, 0);

            byte[] basePixels = new byte[height * stride];
            byte[] overlayPixels = new byte[height * stride];
            byte[] finalPixels = new byte[height * stride];

            baseFormatted.CopyPixels(basePixels, stride, 0);
            overlayFormatted.CopyPixels(overlayPixels, stride, 0);

            for (int i = 0; i < finalPixels.Length; i += 4)
            {
                byte baseB = basePixels[i];
                byte baseG = basePixels[i + 1];
                byte baseR = basePixels[i + 2];

                byte overB = overlayPixels[i];
                byte overG = overlayPixels[i + 1];
                byte overR = overlayPixels[i + 2];
                byte overA = overlayPixels[i + 3];

                if (overA < 10)
                {
                    finalPixels[i] = baseB;
                    finalPixels[i + 1] = baseG;
                    finalPixels[i + 2] = baseR;
                    finalPixels[i + 3] = 255;
                }
                else if (overA > 245)
                {
                    finalPixels[i] = overB;
                    finalPixels[i + 1] = overG;
                    finalPixels[i + 2] = overR;
                    finalPixels[i + 3] = 255;
                }
                else
                {
                    double alpha = overA / 255.0;
                    finalPixels[i] = (byte)(overB * alpha + baseB * (1 - alpha));
                    finalPixels[i + 1] = (byte)(overG * alpha + baseG * (1 - alpha));
                    finalPixels[i + 2] = (byte)(overR * alpha + baseR * (1 - alpha));
                    finalPixels[i + 3] = 255;
                }
            }

            var mergedBitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, finalPixels, stride);
            mergedBitmap.Freeze();
            return mergedBitmap;
        }

        private static BitmapSource ForceSolidBackground(BitmapSource overlay)
        {
            // 如果没有原版底图，强制铺一层灰色底
            var baseBmp = CreateSolidSkin(overlay.PixelWidth, overlay.PixelHeight, Colors.Gray);
            return MergeSkin(baseBmp, overlay);
        }

        public static BitmapSource CreateSolidSkin(int width, int height, Color color)
        {
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = 255;
            }
            var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }

        public static async Task<ImageSource> LoadNetworkOrDefaultAsync(string username)
        {
            try
            {
                Directory.CreateDirectory(PathManager.SkinsCacheDir);
                string originalFile = Path.Combine(PathManager.SkinsCacheDir, $"{username}_original.png");
                string scaledFile = Path.Combine(PathManager.SkinsCacheDir, $"{username}_scaled.png");

                if (File.Exists(scaledFile)) return Load(scaledFile);

                if (!File.Exists(originalFile))
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
                    {
                        var data = await http.GetByteArrayAsync($"https://minotar.net/skin/{username}").ConfigureAwait(false);
                        await Task.Run(() => File.WriteAllBytes(originalFile, data)).ConfigureAwait(false);
                    }
                }

                BitmapSource originalBmp = Load(originalFile);
                BitmapSource scaledBmp = ScaleBitmapNearestNeighbor(originalBmp, 20);
                SaveBitmapToFile(scaledBmp, scaledFile);
                return scaledBmp;
            }
            catch
            {
                return await LoadDefaultAsync().ConfigureAwait(false);
            }
        }

        public static async Task<ImageSource> LoadDefaultAsync()
        {
            try
            {
                Directory.CreateDirectory(PathManager.SkinsCacheDir);
                string originalFile = Path.Combine(PathManager.SkinsCacheDir, "steve_original.png");
                string scaledFile = Path.Combine(PathManager.SkinsCacheDir, "steve_scaled.png");

                if (!File.Exists(scaledFile))
                {
                    if (!File.Exists(originalFile))
                    {
                        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "steve.png");
                        if (File.Exists(localPath)) File.Copy(localPath, originalFile, true);
                        else
                        {
                            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
                            {
                                var data = await http.GetByteArrayAsync("https://minotar.net/skin/Steve").ConfigureAwait(false);
                                await Task.Run(() => File.WriteAllBytes(originalFile, data)).ConfigureAwait(false);
                            }
                        }
                    }
                    BitmapSource originalBmp = Load(originalFile);
                    BitmapSource scaledBmp = ScaleBitmapNearestNeighbor(originalBmp, 20);
                    SaveBitmapToFile(scaledBmp, scaledFile);
                }
                return Load(scaledFile);
            }
            catch
            {
                return null;
            }
        }

        public static BitmapSource ScaleBitmapNearestNeighbor(BitmapSource source, int scale)
        {
            int width = source.PixelWidth;
            int height = source.PixelHeight;
            BitmapSource formattedSource = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = width * 4;
            byte[] sourcePixels = new byte[height * stride];
            formattedSource.CopyPixels(sourcePixels, stride, 0);

            int newWidth = width * scale;
            int newHeight = height * scale;
            int newStride = newWidth * 4;
            byte[] destPixels = new byte[newHeight * newStride];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIndex = y * stride + x * 4;
                    byte b = sourcePixels[srcIndex];
                    byte g = sourcePixels[srcIndex + 1];
                    byte r = sourcePixels[srcIndex + 2];
                    byte a = sourcePixels[srcIndex + 3];

                    for (int dy = 0; dy < scale; dy++)
                    {
                        for (int dx = 0; dx < scale; dx++)
                        {
                            int destX = x * scale + dx;
                            int destY = y * scale + dy;
                            int destIndex = destY * newStride + destX * 4;
                            destPixels[destIndex] = b;
                            destPixels[destIndex + 1] = g;
                            destPixels[destIndex + 2] = r;
                            destPixels[destIndex + 3] = a;
                        }
                    }
                }
            }

            var destBitmap = BitmapSource.Create(newWidth, newHeight, 96, 96, PixelFormats.Bgra32, null, destPixels, newStride);
            destBitmap.Freeze();
            return destBitmap;
        }

        private static void SaveBitmapToFile(BitmapSource bmp, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var stream = new FileStream(path, FileMode.Create))
            {
                encoder.Save(stream);
            }
        }

        private static BitmapSource Load(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return frame;
            }
        }
    }
}