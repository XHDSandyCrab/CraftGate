using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
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

        /// <summary>
        /// 把独立披风 PNG(64x32) 合并进皮肤 UV 图，输出 64x128 的扩展皮肤：
        /// 上半 y=0~63 放原皮肤，下半 y=64~95 放披风 UV，其余透明。
        /// 供低版本原版/CustomSkinLoader 把披风和角色放在同一张 UV 图里使用。
        /// 返回保存路径，失败返回 null。
        /// </summary>
        public static async Task<string> MergeCapeIntoSkinAsync(string skinPath, string capePath, string savePath)
        {
            try
            {
                if (!File.Exists(skinPath) || !File.Exists(capePath)) return null;
                var skin = Load(skinPath);
                var cape = Load(capePath);
                if (skin == null || cape == null) return null;

                const int W = 64, H = 128; // 扩展皮肤画布
                var pixels = new byte[W * H * 4]; // 默认全透明(0)

                // 贴皮肤：居中放上半（64x64 或 64x32 都从 y=0 开始）
                int skinW = Math.Min(W, skin.PixelWidth), skinH = Math.Min(64, skin.PixelHeight);
                int sStride = skin.PixelWidth * 4;
                var sPx = new byte[sStride * skinH];
                skin.CopyPixels(new Int32Rect(0, 0, skinW, skinH), sPx, sStride, 0);
                for (int y = 0; y < skinH; y++)
                    Buffer.BlockCopy(sPx, y * sStride, pixels, y * W * 4, skinW * 4);

                // 贴披风：放到 y=64 起始区域（64x32）
                int capeW = Math.Min(W, cape.PixelWidth), capeH = Math.Min(32, cape.PixelHeight);
                int cStride = cape.PixelWidth * 4;
                var cPx = new byte[cStride * capeH];
                cape.CopyPixels(new Int32Rect(0, 0, capeW, capeH), cPx, cStride, 0);
                for (int y = 0; y < capeH; y++)
                    Buffer.BlockCopy(cPx, y * cStride, pixels, (64 + y) * W * 4, capeW * 4);

                var merged = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4);
                merged.Freeze();

                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
                using (var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(merged));
                    enc.Save(fs);
                }
                return await Task.FromResult(savePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("披风UV合并失败: " + ex.Message);
                return null;
            }
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