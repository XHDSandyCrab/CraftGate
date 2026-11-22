using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CraftGate.Core.Game
{
    public class LocalSkinServer
    {
        private HttpListener _listener;
        private readonly int _port = 25565;
        private readonly string _uuid;
        private readonly string _username;
        private readonly string _skinPath;
        private bool _isRunning;

        public LocalSkinServer(string uuid, string username, string skinPath)
        {
            _uuid = uuid.Replace("-", ""); // Yggdrasil 要求无横杠的 UUID
            _username = username;
            _skinPath = skinPath;
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
                _isRunning = true;

                _ = Task.Run(ListenLoopAsync);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("本地皮肤服务端口被占用，换肤可能失败！\n错误信息：" + ex.Message, "端口冲突");
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            try { _listener.Stop(); _listener.Close(); } catch { }
        }

        private async Task ListenLoopAsync()
        {
            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch
                {
                    break;
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;

            try
            {
                if (req.Url == null) return;
                string path = req.Url.AbsolutePath.ToLower();

                // 1. 根节点 API 元数据
                if (path == "/" || path == "/api/yggdrasil" || path == "/api/yggdrasil/")
                {
                    string json = "{\"meta\":{\"serverName\":\"CraftGate Local\",\"implementationName\":\"CraftGate\",\"implementationVersion\":\"1.0\"},\"skinDomains\":[\"127.0.0.1\"]}";
                    SendJson(res, json);
                }
                // 2. 获取玩家档案（核心修复：加上了 /api/yggdrasil 前缀）
                else if (path.EndsWith($"/sessionserver/session/minecraft/profile/{_uuid}"))
                {
                    string skinUrl = $"http://127.0.0.1:{_port}/skins/{_username}.png";

                    // 组装 Minecraft 的 Base64 纹理数据
                    string texturesJson = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"profileId\":\"{_uuid}\",\"profileName\":\"{_username}\",\"textures\":{{\"SKIN\":{{\"url\":\"{skinUrl}\"}}}}}}";
                    string base64Textures = Convert.ToBase64String(Encoding.UTF8.GetBytes(texturesJson));

                    string profileJson = $"{{\"id\":\"{_uuid}\",\"name\":\"{_username}\",\"properties\":[{{\"name\":\"textures\",\"value\":\"{base64Textures}\"}}]}}";
                    SendJson(res, profileJson);
                }
                // 3. 提供真实的图片文件
                else if (path.StartsWith("/skins/") && path.EndsWith(".png"))
                {
                    if (File.Exists(_skinPath))
                    {
                        byte[] buffer = File.ReadAllBytes(_skinPath);
                        res.StatusCode = 200;
                        res.ContentType = "image/png";
                        res.ContentLength64 = buffer.Length;
                        res.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        res.StatusCode = 404;
                    }
                }
                else
                {
                    res.StatusCode = 404;
                }
            }
            catch { }
            finally
            {
                try { res.Close(); } catch { }
            }
        }

        private void SendJson(HttpListenerResponse res, string json)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            res.StatusCode = 200;
            res.ContentType = "application/json";
            res.ContentLength64 = buffer.Length;
            res.OutputStream.Write(buffer, 0, buffer.Length);
        }
    }
}