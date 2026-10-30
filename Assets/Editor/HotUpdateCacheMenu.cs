using System.IO;
using UnityEditor;
using UnityEngine;

namespace EditorTools
{
    /// <summary>
    /// 本地热更缓存清理工具：删除已下载的热更 DLL/manifest 并清空 AssetBundle 缓存，
    /// 用于模拟首次安装、验证完整更新流程。
    /// </summary>
    public static class HotUpdateCacheMenu
    {
        [MenuItem("HotUpdate/8. Clear Local HotUpdate Cache (清本地缓存)")]
        public static void ClearLocalHotUpdateCache()
        {
            var dataDir = Path.Combine(Application.persistentDataPath, "Data");

            if (!EditorUtility.DisplayDialog("清除本地热更缓存",
                    "将执行以下操作（用于模拟首次安装）：\n\n" +
                    $"1. 删除已下载的热更目录：\n{dataDir}\n\n" +
                    "2. 清空 AssetBundle 缓存（Caching.ClearCache）\n\n" +
                    "下次运行将重新下载全部热更内容。是否继续？",
                    "清除", "取消"))
            {
                return;
            }

            if (Directory.Exists(dataDir))
            {
                Directory.Delete(dataDir, true);
                Debug.Log($"[HotUpdateCache] 已删除 {dataDir}");
            }
            else
            {
                Debug.Log($"[HotUpdateCache] 目录不存在，跳过: {dataDir}");
            }

            var cacheCleared = Caching.ClearCache();
            Debug.Log($"[HotUpdateCache] Caching.ClearCache: {cacheCleared}");

            EditorUtility.DisplayDialog("清除完成",
                $"热更缓存已清除：\n{dataDir}\n\nAssetBundle 缓存清空: {cacheCleared}", "OK");
        }
    }
}
