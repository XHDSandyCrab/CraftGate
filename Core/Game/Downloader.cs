using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CraftGate.Models;
using CraftGate.Services;

namespace CraftGate.Core.Game
{
    public static class Downloader
    {
        // 👇 伪装成 Edge 浏览器，防止被 CDN 针对
        private static readonly HttpClient _client = new HttpClient(new HttpClientHandler
        {
            MaxConnectionsPerServer = 16
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static Downloader()
        {
            _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0");
            _client.DefaultRequestHeaders.Add("Accept", "*/*");
            _client.DefaultRequestHeaders.Add("Connection", "keep-alive");
        }

        public static async Task DownloadFileAsync(string url, string savePath, Action<long, long> progressCallback = null, FileDownloadTask task = null)
        {
            string finalUrl = HttpService.GetUrl(url);

            string dir = Path.GetDirectoryName(savePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // 👇 直接使用单线程流式下载（和浏览器完全一致）
            using (var response = await _client.GetAsync(finalUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                if (task != null && totalBytes > 0) task.FileSize = totalBytes;

                using (var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long bytesReadTotal = 0;
                    int bytesRead;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            bytesReadTotal += bytesRead;

                            progressCallback?.Invoke(bytesReadTotal, totalBytes);
                            if (task != null)
                            {
                                if (totalBytes > 0)
                                    task.Progress = (double)bytesReadTotal / totalBytes * 100;
                                else
                                    task.Progress = 100; // 如果服务器不给文件大小，直接满进度
                            }
                        }
                    }
                }
            }
        }
    }
}