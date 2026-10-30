using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Common.Utilities;
using Main.Config;
using UnityEngine;

namespace Main.Games
{
    /// <summary>
    /// 游戏更新管理器：获取注册表 → 按游戏对比版本 → 按游戏下载（MD5 校验）→ 保存本地状态。
    ///
    /// 版本粒度是"每个子游戏"：改了斗地主的代码只重下斗地主的 DLL，
    /// 其它游戏（以及公共程序集未变时）不受影响——这是矩阵形态与
    /// 旧"整体单版本"模型的本质区别。
    ///
    /// 注意：实际的 Assembly.Load 与入口调用由 <see cref="Main.HotUpdateLoader"/> 负责。
    /// </summary>
    public static class GameUpdateManager
    {
        private static string _serverBaseUrl = HotUpdateConfig.DefaultServerBaseUrl;
        private static HttpClient _httpClient = CreateHttpClient(30);

        /// <summary>远程注册表 URL</summary>
        private static string RegistryUrl => $"{_serverBaseUrl}/games.json";

        /// <summary>
        /// 注入部署配置（服务器地址 + 超时）。必须在第一次网络请求前调用。
        /// </summary>
        public static void Configure(HotUpdateConfig config)
        {
            if (config == null)
            {
                return;
            }

            _serverBaseUrl = config.ServerBaseUrl;
            _httpClient = CreateHttpClient(config.RequestTimeoutSeconds);
            Debug.Log($"[GameUpdateManager] 服务器: {_serverBaseUrl}, 超时: {config.RequestTimeoutSeconds}s");
        }

        private static HttpClient CreateHttpClient(int timeoutSeconds)
        {
            // UseProxy=false：热更下载不走系统代理，避免本地测试时被 Clash 等代理劫持 localhost
            return new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
        }

        // ── 注册表 ────────────────────────────────────────────────

        /// <summary>
        /// 获取远程注册表（服务器根目录 games.json）。
        /// 失败返回 null，由调用方回退到 StreamingAssets 内置清单。
        /// </summary>
        public static async Task<GameRegistry> FetchRemoteRegistry()
        {
            try
            {
                var json = await _httpClient.GetStringAsync(RegistryUrl);
                var registry = JsonUtility.FromJson<GameRegistry>(json);
                if (registry?.games == null || registry.games.Count == 0)
                {
                    Debug.LogError("[GameUpdateManager] 远程 games.json 格式无效（无 games 条目）");
                    return null;
                }

                Debug.Log($"[GameUpdateManager] 远程注册表: {registry.games.Count} 个游戏 (version={registry.version})");
                return registry;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameUpdateManager] 获取远程注册表失败: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 读取 StreamingAssets 内置注册表（打包时生成的兜底清单）。
        /// 缺失或解析失败返回 null。
        /// </summary>
        public static GameRegistry GetBuiltinRegistry()
        {
            var bytes = HotUpdateLoader.ReadStreamingBytes(HotUpdateLoader.GetStreamingRegistryPath(), null);
            if (bytes == null)
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<GameRegistry>(System.Text.Encoding.UTF8.GetString(bytes));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameUpdateManager] 解析内置 games.json 失败: {e.Message}");
                return null;
            }
        }

        // ── 按游戏的版本与下载 ────────────────────────────────────

        /// <summary>某游戏已保存的本地状态；无则 null。</summary>
        public static GameInfo GetLocalState(string gameId)
        {
            var path = LocalStatePath(gameId);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<GameInfo>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameUpdateManager] 读取本地状态失败: {gameId} ({e.Message})");
                return null;
            }
        }

        /// <summary>
        /// 解析某游戏的"本地有效版本"：优先已下载状态，其次 StreamingAssets 内置版本；都没有则 null。
        /// </summary>
        public static GameInfo ResolveLocal(GameInfo remote, GameRegistry builtinRegistry)
        {
            var local = GetLocalState(remote.id);
            if (local != null)
            {
                return local;
            }

            return builtinRegistry?.Find(remote.id);
        }

        /// <summary>远程游戏与本地有效版本不一致即认为有更新；本地无任何版本视为需下载。</summary>
        public static bool HasUpdate(GameInfo remote, GameInfo local)
        {
            if (remote == null || string.IsNullOrEmpty(remote.version))
            {
                return false;
            }

            if (local == null || string.IsNullOrEmpty(local.version))
            {
                return true;
            }

            var hasUpdate = !string.Equals(local.version, remote.version, StringComparison.Ordinal);
            Debug.Log($"[GameUpdateManager] {remote.id} 版本对比: 本地={local.version}, 远程={remote.version}, 有更新={hasUpdate}");
            return hasUpdate;
        }

        /// <summary>大厅按钮用的状态文案。</summary>
        public static string StateLabel(GameInfo remote, GameRegistry builtinRegistry)
        {
            var local = ResolveLocal(remote, builtinRegistry);
            if (local == null)
            {
                return "待下载";
            }

            return HasUpdate(remote, local) ? "可更新" : "已就绪";
        }

        /// <summary>
        /// 下载某游戏的全部 DLL（逐一 MD5 校验），全部成功后保存本地状态。
        /// </summary>
        public static async Task<bool> DownloadGame(GameInfo game, Action<float> onProgress = null)
        {
            if (game?.dlls == null || game.dlls.Count == 0)
            {
                Debug.LogError($"[GameUpdateManager] 游戏 {game?.id} 没有声明任何 DLL");
                return false;
            }

            try
            {
                var count = game.dlls.Count;
                for (var i = 0; i < count; i++)
                {
                    var entry = game.dlls[i];
                    // 统一平铺在服务器根目录与本地游戏目录下
                    var fileName = Path.GetFileName(entry.name);
                    var url = $"{_serverBaseUrl}/{fileName}";
                    Debug.Log($"[GameUpdateManager] ({i + 1}/{count}) 下载 {url}");
                    var bytes = await DownloadFile(url, p => onProgress?.Invoke((i + p) / count));

                    if (!string.IsNullOrEmpty(entry.md5))
                    {
                        var md5 = CryptoUtils.MD5Bytes(bytes);
                        if (!string.Equals(md5, entry.md5, StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.LogError($"[GameUpdateManager] MD5 校验失败: {fileName}, 计算={md5}, 预期={entry.md5}");
                            return false;
                        }

                        Debug.Log($"[GameUpdateManager] MD5 校验通过: {fileName}");
                    }

                    var localPath = LocalGameDllPath(game.id, fileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                    await File.WriteAllBytesAsync(localPath, bytes);
                }

                // 全部 DLL 就位后才写状态，保证"状态存在 = 完整可加载"
                var statePath = LocalStatePath(game.id);
                Directory.CreateDirectory(Path.GetDirectoryName(statePath));
                await File.WriteAllTextAsync(statePath, JsonUtility.ToJson(game, true));

                Debug.Log($"[GameUpdateManager] {game.id} 下载完成，版本 {game.version}（{count} 个 DLL）");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameUpdateManager] {game.id} 下载失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 下载文件，返回完整字节数组。
        /// 使用 MemoryStream 以支持 ContentLength 未知的情况。
        /// </summary>
        private static async Task<byte[]> DownloadFile(string url, Action<float> onProgress)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var memory = new MemoryStream();

            var buffer = new byte[81920];
            int read;
            long downloaded = 0;

            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                memory.Write(buffer, 0, read);
                downloaded += read;
                if (total > 0)
                {
                    onProgress?.Invoke((float)downloaded / total);
                }
            }

            onProgress?.Invoke(1f);
            return memory.ToArray();
        }

        // ── 路径约定 ──────────────────────────────────────────────

        /// <summary>游戏本地目录（persistentDataPath/Data/games/{id}/）</summary>
        public static string LocalGameDir(string gameId)
        {
            return Path.Combine(Application.persistentDataPath, "Data", "games", gameId);
        }

        /// <summary>游戏 DLL 本地路径（Data/games/{id}/dlls/&lt;fileName&gt;）</summary>
        public static string LocalGameDllPath(string gameId, string dllName)
        {
            return Path.Combine(LocalGameDir(gameId), "dlls", Path.GetFileName(dllName));
        }

        /// <summary>游戏本地状态文件（Data/games/{id}/state.json）</summary>
        public static string LocalStatePath(string gameId)
        {
            return Path.Combine(LocalGameDir(gameId), "state.json");
        }
    }
}
