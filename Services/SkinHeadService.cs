using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CraftGate.Services
{
    /// <summary>
    /// 从 Minecraft 皮肤 UV 纹理（64x32 / 64x64）中裁出头部，合成一张"大头"头像。
    /// 合成规则（经典 UV 布局）：
    ///   脸部底层：8,8 - 16,16 （8x8）
    ///   头部第二层（头发/帽子）：40,8 - 48,16 （8x8，叠在脸上，透明处露出底脸）
    ///   再把脸放大到 NxN 显示，像素风最近邻。
    /// </summary>
    public static class SkinHeadService
    {
        /// <summary>从皮肤文件生成头像（放大 scale 倍，默认 8 → 64x64）。失败返回 null。</summary>
        public static BitmapSource GetHead(string skinPath, int scale = 8)
        {
            if (string.IsNullOrEmpty(skinPath) || !File.Exists(skinPath)) return null;
            try
            {
                BitmapSource skin;
                using (var fs = new FileStream(skinPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    skin = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Bgra32, null, 0);
                    skin.Freeze();
                }
                return BuildHead(skin, scale);
            }
            catch { return null; }
        }

        /// <summary>从已加载的皮肤位图生成头像。</summary>
        public static BitmapSource BuildHead(BitmapSource skin, int scale = 8)
        {
            if (skin == null || skin.PixelWidth < 64) return null;
            int stride = skin.PixelWidth * 4;
            var px = new byte[skin.PixelHeight * stride];
            skin.CopyPixels(px, stride, 0);

            // 裁脸底层 (8,8)-(16,16) 与 第二层 (40,8)-(48,16)，合成 8x8
            var face = new byte[8 * 8 * 4];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int baseIdx = ((8 + y) * skin.PixelWidth + (8 + x)) * 4;
                    int layerIdx = ((8 + y) * skin.PixelWidth + (40 + x)) * 4;
                    int dst = (y * 8 + x) * 4;

                    byte br = px[baseIdx + 2], bg = px[baseIdx + 1], bb = px[baseIdx], ba = px[baseIdx + 3];
                    byte lr = px[layerIdx + 2], lg = px[layerIdx + 1], lb = px[layerIdx], la = px[layerIdx + 3];

                    if (la >= 10)
                    {
                        face[dst] = lb; face[dst + 1] = lg; face[dst + 2] = lr; face[dst + 3] = la;
                    }
                    else
                    {
                        face[dst] = bb; face[dst + 1] = bg; face[dst + 2] = br; face[dst + 3] = ba;
                    }
                }
            }

            // 放大到 NxN（最近邻）
            int outSize = 8 * scale;
            int outStride = outSize * 4;
            var outPx = new byte[outSize * outStride];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int src = (y * 8 + x) * 4;
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                        {
                            int d = ((y * scale + dy) * outSize + (x * scale + dx)) * 4;
                            outPx[d] = face[src]; outPx[d + 1] = face[src + 1];
                            outPx[d + 2] = face[src + 2]; outPx[d + 3] = face[src + 3];
                        }
                }

            var bmp = BitmapSource.Create(outSize, outSize, 96, 96, PixelFormats.Bgra32, null, outPx, outStride);
            bmp.Freeze();
            return bmp;
        }
    }
}
