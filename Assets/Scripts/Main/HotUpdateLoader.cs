using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Main.Games;
using UnityEngine;
using UnityEngine.Networking;

namespace Main
{
    /// <summary>
    /// 热更程序集加载与入口调用（容器模型，按游戏按需加载）。
    ///
    /// 加载规则：
    /// - 每个游戏的 dlls 列表声明它依赖的全部程序集（含公共程序集，按依赖序）
    /// - 跨游戏去重：同名程序集只 Assembly.Load 一次（先进入的游戏先加载；
    ///   共享程序集的更新在本次会话内不生效，下次启动生效）
    /// - 来源优先级：已下载（Data/games/{id}/dlls/）→ StreamingAssets 内置 → 失败
    /// - Editor 下热更程序集由 Unity 自动加载，EnsureLoaded 直接通过
    ///
    /// 入口调用按 GameInfo 的 entryAssembly/entryClass/entryMethod 反射执行。
    /// </summary>
    public static class HotUpdateLoader
    {
        /// <summary>已加载的热更程序集（按加载顺序，供入口查找）。</summary>
        private static readonly List<Assembly> LoadedOrder = new();

        /// <summary>已加载的程序集名集合（跨游戏去重）。</summary>
        private static readonly HashSet<string> LoadedNames = new(StringComparer.Ordinal);

        /// <summary>
        /// 确保某游戏依赖的全部热更 DLL 已加载。
        /// </summary>
        /// <returns>true 表示列表内 DLL 全部就绪</returns>
        public static bool EnsureLoaded(GameInfo game, Action<string> onLog = null)
        {
            var log = onLog ?? Debug.Log;

#if UNITY_EDITOR
            log($"[HotUpdateLoader] Editor 环境，热更程序集已由 Unity 加载（{game.id}）");
            return true;
#else
            if (game?.dlls == null || game.dlls.Count == 0)
            {
                log($"[HotUpdateLoader] 游戏 {game?.id} 没有声明任何 DLL");
                return false;
            }

            foreach (var entry in game.dlls)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(entry.name);
                if (LoadedNames.Contains(assemblyName))
                {
                    continue;
                }

                var bytes = TryReadGameDll(game, entry.name, log);
                if (bytes == null)
                {
                    log($"[HotUpdateLoader] 热更 DLL 不可读: {entry.name}（{game.id}）");
                    return false;
                }

                try
                {
                    var assembly = Assembly.Load(bytes);
                    LoadedOrder.Add(assembly);
                    LoadedNames.Add(assemblyName);
                    log($"[HotUpdateLoader] 已加载程序集: {assemblyName}");
                }
                catch (Exception e)
                {
                    log($"[HotUpdateLoader] 加载 {entry.name} 失败: {e.Message}");
                    return false;
                }
            }

            return true;
#endif
        }

        /// <summary>
        /// 调用游戏的入口静态方法。
        /// </summary>
        public static bool InvokeEntry(GameInfo game, Action<string> onLog = null)
        {
            var log = onLog ?? Debug.Log;
            if (game == null || string.IsNullOrEmpty(game.entryClass))
            {
                log("[HotUpdateLoader] 游戏未配置入口（entryClass 为空）");
                return false;
            }

            var methodName = string.IsNullOrEmpty(game.entryMethod) ? "Run" : game.entryMethod;

#if UNITY_EDITOR
            // Editor 下在已加载程序集中查找；入口声明的程序集优先
            var all = AppDomain.CurrentDomain.GetAssemblies();
            var candidates = all.Where(a => a.GetName().Name == game.entryAssembly)
                .Concat(all.Where(a => a.GetName().Name != game.entryAssembly));
            foreach (var assembly in candidates)
            {
                var type = assembly.GetType(game.entryClass);
                if (type == null)
                {
                    continue;
                }

                log($"[HotUpdateLoader] Editor 环境，入口位于已加载程序集: {assembly.GetName().Name}");
                return InvokeOnType(type, methodName, game, log);
            }

            log($"[HotUpdateLoader] Editor 环境下未找到入口类: {game.entryClass}，请确认热更代码编译无误");
            return false;
#else
            foreach (var assembly in LoadedOrder)
            {
                var type = assembly.GetType(game.entryClass);
                if (type == null)
                {
                    continue;
                }

                return InvokeOnType(type, methodName, game, log);
            }

            log($"[HotUpdateLoader] 已加载的程序集中未找到入口类: {game.entryClass}");
            return false;
#endif
        }

        private static bool InvokeOnType(Type entryType, string methodName, GameInfo game, Action<string> log)
        {
            var method = entryType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                log($"[HotUpdateLoader] 入口类 {entryType.FullName} 中未找到静态方法: {methodName}（{game.id}）");
                return false;
            }

            try
            {
                method.Invoke(null, null);
                return true;
            }
            catch (Exception e)
            {
                log($"[HotUpdateLoader] 入口方法执行异常: {e}");
                return false;
            }
        }

        /// <summary>
        /// 调用游戏的退出静态方法（默认方法名 "Exit"；不声明则静默跳过）。
        /// 返回大厅前调用，用于让游戏自己释放运行期资源。
        /// </summary>
        public static void InvokeExit(GameInfo game, Action<string> log = null)
        {
            var l = log ?? Debug.Log;
            if (game == null || string.IsNullOrEmpty(game.entryClass))
            {
                return;
            }

            var methodName = string.IsNullOrEmpty(game.exitMethod) ? "Exit" : game.exitMethod;

            var all = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in all)
            {
                var type = assembly.GetType(game.entryClass);
                if (type == null)
                {
                    continue;
                }

                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    l($"[HotUpdateLoader] {game.entryClass} 未声明 {methodName}，跳过退出处理");
                    return;
                }

                try
                {
                    method.Invoke(null, null);
                    l($"[HotUpdateLoader] 已执行 {game.entryClass}.{methodName}");
                }
                catch (Exception e)
                {
                    l($"[HotUpdateLoader] 退出方法执行异常: {e}");
                }

                return;
            }

            l($"[HotUpdateLoader] 未找到入口类 {game.entryClass}，跳过退出处理");
        }

        // ── 文件读取 ──────────────────────────────────────────────

        /// <summary>
        /// 读取某游戏的一个热更 DLL：优先已下载目录，其次 StreamingAssets 内置。
        /// </summary>
        private static byte[] TryReadGameDll(GameInfo game, string dllName, Action<string> log)
        {
            var localPath = GameUpdateManager.LocalGameDllPath(game.id, dllName);
            if (File.Exists(localPath))
            {
                log($"[HotUpdateLoader] 读取已下载的 DLL: {localPath}");
                return File.ReadAllBytes(localPath);
            }

            var bytes = ReadStreamingBytes(GetStreamingDllPath(dllName), log);
            if (bytes != null)
            {
                log($"[HotUpdateLoader] 从 StreamingAssets 内置读取: {dllName}");
            }

            return bytes;
        }

        /// <summary>
        /// 读取 StreamingAssets 下的文件（也可用于读内置 games.json）。
        /// Android 平台 StreamingAssets 在 apk 内，必须经 UnityWebRequest 读取；
        /// 调用方保证主线程，这里同步等待完成。
        /// </summary>
        /// <param name="log">日志回调，传 null 则静默</param>
        public static byte[] ReadStreamingBytes(string path, Action<string> log)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var request = UnityWebRequest.Get(path);
            var op = request.SendWebRequest();
            while (!op.isDone)
            {
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                log?.Invoke($"[HotUpdateLoader] 读取 StreamingAssets 失败: {path} ({request.error})");
                return null;
            }

            return request.downloadHandler.data;
#else
            if (!File.Exists(path))
            {
                return null;
            }

            return File.ReadAllBytes(path);
#endif
        }

        // ── 路径约定 ──────────────────────────────────────────────

        /// <summary>StreamingAssets 下热更 DLL 路径（全部平铺在根目录）</summary>
        public static string GetStreamingDllPath(string dllName)
        {
            return Path.Combine(Application.streamingAssetsPath, Path.GetFileName(dllName));
        }

        /// <summary>StreamingAssets 下内置注册表（打包时生成的 games.json 兜底）</summary>
        public static string GetStreamingRegistryPath()
        {
            return Path.Combine(Application.streamingAssetsPath, "games.json");
        }
    }
}
