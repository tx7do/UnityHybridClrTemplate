using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HybridCLR.Commands;
using Main.Config;
using Main.Games;
using UnityEditor;
using UnityEngine;

namespace EditorTools
{
    /// <summary>
    /// 热更新一键打包工具（容器模型）。
    ///
    /// 游戏清单的"定义"由开发者维护在 Assets/HotUpdate/games.json（不含版本/MD5），
    /// 本工具负责校验定义并发布：
    /// - HybridCLRData/ServerOutput/games.json      —— 服务器注册表（含每游戏版本/MD5/大小）
    /// - Assets/StreamingAssets/games.json          —— 内置兜底注册表（离线/首次安装用）
    /// - 全部被引用的热更 DLL 拷入上述两处根目录（平铺）
    ///
    /// 单游戏与多游戏矩阵是同一套流程：games.json 里 1 条 = 单游戏，N 条 = 矩阵。
    ///
    /// 其他约定：
    /// - AOT 元数据: HybridCLRData/AssembliesPostIl2CppStrip/[平台]/ → StreamingAssets/AOT/
    /// - games.json 的 minAppVersion 取自 HotUpdateConfig 资产
    /// - 只发布 games.json 中声明的 DLL：CompileDll 输出目录里还有主包等程序集，不能混入
    /// </summary>
    public static class HotUpdateBuildEditor
    {
        private const string MenuRoot = "HotUpdate/";

        // ── 路径常量 ──────────────────────────────────────────────

        /// <summary>游戏清单定义（开发者维护，随工程版本管理）</summary>
        private const string GamesDefinitionPath = "Assets/HotUpdate/games.json";

        /// <summary>HybridCLR 编译热更 DLL 的输出目录（含平台子目录）</summary>
        private static string HotUpdateDllSourceDir =>
            HybridCLR.SettingsUtil.GetHotUpdateDllsOutputDirByTarget(EditorUserBuildSettings.activeBuildTarget);

        /// <summary>StreamingAssets/AOT 目录（随主包打入）</summary>
        private static string StreamingAOTDir =>
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "AOT");

        /// <summary>StreamingAssets 根目录（热更 DLL 与内置注册表的所在）</summary>
        private static string StreamingDir =>
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets");

        /// <summary>服务器输出目录（Python serve 此目录）</summary>
        private static string ServerOutputDir =>
            Path.Combine(Directory.GetCurrentDirectory(), "HybridCLRData", "ServerOutput");

        /// <summary>注册表文件名（服务器根目录与 StreamingAssets 各一份）</summary>
        private const string RegistryFileName = "games.json";

        // ── 菜单项 ────────────────────────────────────────────────

        /// <summary>
        /// 打开（或首次创建）HotUpdateConfig 部署配置资产。
        /// </summary>
        [MenuItem(MenuRoot + "0. HotUpdate Config (部署配置)")]
        public static void SelectHotUpdateConfig()
        {
            var path = $"Assets/Resources/{HotUpdateConfig.ResourcePath}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<HotUpdateConfig>(path);
            if (asset == null)
            {
                if (!Directory.Exists("Assets/Resources"))
                {
                    Directory.CreateDirectory("Assets/Resources");
                }

                asset = ScriptableObject.CreateInstance<HotUpdateConfig>();
                AssetDatabase.CreateAsset(asset, path);
                Debug.Log($"[HotUpdateBuildEditor] 已创建配置资产: {path}");
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// 一键打包：编译 DLL → 生成 AOT → 拷贝 → 生成注册表。
        /// 这是日常使用的唯一入口。
        /// </summary>
        [MenuItem(MenuRoot + "1. Build HotUpdate Package (一键打包)")]
        public static void BuildHotUpdatePackage()
        {
            try
            {
                Debug.Log("===== 开始热更新打包 =====");

                // 1. 生成 HybridCLR 必要文件（link.xml、AOT 引用、方法桥接）
                Debug.Log("[1/5] 执行 HybridCLR Generate/All");
                GenerateHybridCLRAll();

                // 2. 编译热更 DLL
                Debug.Log("[2/5] 编译热更 DLL");
                CompileHotUpdateDll();

                // 3. 拷贝 AOT 元数据到 StreamingAssets
                Debug.Log("[3/5] 拷贝 AOT 元数据到 StreamingAssets");
                CopyAOTToStreamingAssets();

                // 4. 拷贝热更 DLL 到 StreamingAssets（内置兜底版本 + 内置注册表）
                Debug.Log("[4/5] 拷贝热更 DLL 到 StreamingAssets");
                CopyHotUpdateDllToStreamingAssets();

                // 5. 生成服务器输出（热更 DLL + games.json）
                Debug.Log("[5/5] 生成服务器输出目录");
                GenerateServerOutput();

                AssetDatabase.Refresh();

                Debug.Log("===== 热更新打包完成 =====");
                Debug.Log($"服务器输出目录: {ServerOutputDir}");
                Debug.Log("运行 `python3 tools/serve_hotupdate.py` 启动本地服务器");
                EditorUtility.DisplayDialog("打包完成",
                    "热更新打包完成！\n\n" +
                    "服务器输出目录:\n" + ServerOutputDir + "\n\n" +
                    "运行 python3 tools/serve_hotupdate.py 启动本地服务器", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"打包失败: {e}");
                EditorUtility.DisplayDialog("打包失败", e.Message, "OK");
            }
        }

        /// <summary>
        /// 执行 HybridCLR Generate/All（相当于菜单 HybridCLR/Generate/All）。
        /// </summary>
        [MenuItem(MenuRoot + "2. HybridCLR Generate/All")]
        public static void GenerateHybridCLRAll()
        {
            PrebuildCommand.GenerateAll();
            Debug.Log("HybridCLR Generate/All 完成");
        }

        /// <summary>
        /// 编译当前平台的热更 DLL。
        /// </summary>
        [MenuItem(MenuRoot + "3. Compile HotUpdate DLL")]
        public static void CompileHotUpdateDll()
        {
            CompileDllCommand.CompileDll(EditorUserBuildSettings.activeBuildTarget);
            Debug.Log($"热更 DLL 编译完成，输出目录: {HotUpdateDllSourceDir}");

            // 编译产物是否齐全在 GenerateServerOutput/拷贝时按 games.json 校验
            var definitions = LoadGameDefinitions();
            ValidateDefinitions(definitions);
        }

        /// <summary>
        /// 拷贝当前平台裁剪后的 AOT DLL 到 StreamingAssets/AOT/。
        /// </summary>
        [MenuItem(MenuRoot + "4. Copy AOT to StreamingAssets")]
        public static void CopyAOTToStreamingAssets()
        {
            // 裁剪后 AOT DLL 输出在按平台命名的子目录（由 HybridCLR 包维护）
            var aotSourceDir = HybridCLR.SettingsUtil.GetAssembliesPostIl2CppStripDir(
                EditorUserBuildSettings.activeBuildTarget);

            if (!Directory.Exists(aotSourceDir))
            {
                throw new DirectoryNotFoundException(
                    $"AOT DLL 源目录不存在: {aotSourceDir}\n" +
                    "请先执行 HybridCLR Generate/All。");
            }

            // 清空目标目录
            if (Directory.Exists(StreamingAOTDir))
            {
                Directory.Delete(StreamingAOTDir, true);
            }

            Directory.CreateDirectory(StreamingAOTDir);

            // 拷贝所有 .dll
            var dllFiles = Directory.GetFiles(aotSourceDir, "*.dll", SearchOption.TopDirectoryOnly);
            var aotFileNames = new List<string>();

            foreach (var dll in dllFiles)
            {
                var fileName = Path.GetFileName(dll);
                var destPath = Path.Combine(StreamingAOTDir, fileName);
                File.Copy(dll, destPath, true);
                aotFileNames.Add(fileName);
            }

            // 生成 Android 用的文件清单（Android 下 StreamingAssets 无法列举目录）
            var manifestPath = Path.Combine(StreamingAOTDir, "aot_files.txt");
            File.WriteAllLines(manifestPath, aotFileNames);

            Debug.Log($"AOT 元数据拷贝完成: {aotFileNames.Count} 个 DLL → {StreamingAOTDir}");
        }

        /// <summary>
        /// 拷贝全部被引用的热更 DLL 到 StreamingAssets（内置兜底版本），
        /// 并生成内置 games.json（离线/首次安装时客户端的兜底注册表）。
        /// </summary>
        [MenuItem(MenuRoot + "5. Copy HotUpdate DLL to StreamingAssets")]
        public static void CopyHotUpdateDllToStreamingAssets()
        {
            var definitions = LoadGameDefinitions();
            ValidateDefinitions(definitions);
            var registry = BuildRegistry(definitions);

            if (!Directory.Exists(StreamingDir))
            {
                Directory.CreateDirectory(StreamingDir);
            }

            // 清理上次构建可能残留在根目录的 DLL（不动 AOT/ 子目录）
            foreach (var stale in Directory.GetFiles(StreamingDir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                File.Delete(stale);
            }

            var names = new List<string>();
            foreach (var dllName in CollectRequiredDllNames(definitions))
            {
                var src = Path.Combine(HotUpdateDllSourceDir, dllName);
                File.Copy(src, Path.Combine(StreamingDir, dllName), true);
                names.Add(dllName);
            }

            var json = JsonUtility.ToJson(registry, true);
            File.WriteAllText(Path.Combine(StreamingDir, RegistryFileName), json);

            Debug.Log($"热更 DLL 已拷贝到 StreamingAssets: {names.Count} 个 + {RegistryFileName}（内置注册表）");
        }

        /// <summary>
        /// 生成服务器输出目录（全部被引用的热更 DLL + games.json 注册表）。
        /// </summary>
        [MenuItem(MenuRoot + "6. Generate Server Output")]
        public static void GenerateServerOutput()
        {
            var definitions = LoadGameDefinitions();
            ValidateDefinitions(definitions);
            var registry = BuildRegistry(definitions);

            // 清空服务器输出目录
            if (Directory.Exists(ServerOutputDir))
            {
                Directory.Delete(ServerOutputDir, true);
            }

            Directory.CreateDirectory(ServerOutputDir);

            // 拷贝全部被引用的热更 DLL
            foreach (var dllName in CollectRequiredDllNames(definitions))
            {
                File.Copy(Path.Combine(HotUpdateDllSourceDir, dllName),
                    Path.Combine(ServerOutputDir, dllName), true);
            }

            var json = JsonUtility.ToJson(registry, true);
            File.WriteAllText(Path.Combine(ServerOutputDir, RegistryFileName), json);

            Debug.Log($"games.json 已生成 (version={registry.version}, " +
                      $"games={registry.games.Count}, minAppVersion='{registry.minAppVersion}')");
        }

        /// <summary>
        /// 在 Finder/资源管理器中打开服务器输出目录。
        /// </summary>
        [MenuItem(MenuRoot + "7. Open Server Output Folder")]
        public static void OpenServerOutputFolder()
        {
            if (!Directory.Exists(ServerOutputDir))
            {
                EditorUtility.DisplayDialog("提示", "服务器输出目录不存在，请先执行打包。", "OK");
                return;
            }

            EditorUtility.RevealInFinder(ServerOutputDir);
        }

        // ── 游戏清单定义（Assets/HotUpdate/games.json）────────────

        /// <summary>项目内"定义"格式：dlls 是名字列表，版本/MD5 由打包时生成。</summary>
        [Serializable]
        private class GameDefinition
        {
            public string id;
            public string name;
            public List<string> dlls = new();
            public string entryAssembly;
            public string entryClass;
            public string entryMethod;
            public string exitMethod;
            public string scene;
            public string label;
        }

        [Serializable]
        private class GameDefinitionFile
        {
            public List<GameDefinition> games = new();
        }

        private static GameDefinitionFile LoadGameDefinitions()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), GamesDefinitionPath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"游戏清单定义不存在: {GamesDefinitionPath}\n" +
                    "请创建该文件并声明至少一个游戏（参考模板自带的 demo 条目）。");
            }

            var file = JsonUtility.FromJson<GameDefinitionFile>(File.ReadAllText(path));
            if (file?.games == null || file.games.Count == 0)
            {
                throw new InvalidDataException($"游戏清单定义无效（无 games 条目）: {GamesDefinitionPath}");
            }

            return file;
        }

        /// <summary>
        /// 校验游戏定义：id 唯一、DLL 必须是 HybridCLR Settings 声明的热更程序集、
        /// 入口/场景/label 字段齐全。问题直接抛异常阻止打包。
        /// </summary>
        private static void ValidateDefinitions(GameDefinitionFile file)
        {
            var hotUpdateNames = new HashSet<string>(
                HybridCLR.SettingsUtil.HotUpdateAssemblyNamesExcludePreserved, StringComparer.Ordinal);

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var game in file.games)
            {
                if (string.IsNullOrEmpty(game.id) || !seenIds.Add(game.id))
                {
                    throw new InvalidDataException($"游戏 id 缺失或重复: '{game.id}'");
                }

                if (game.dlls == null || game.dlls.Count == 0)
                {
                    throw new InvalidDataException($"游戏 {game.id} 未声明任何 dll");
                }

                foreach (var dll in game.dlls)
                {
                    var name = Path.GetFileNameWithoutExtension(dll);
                    if (!hotUpdateNames.Contains(name))
                    {
                        throw new InvalidDataException(
                            $"游戏 {game.id} 引用的 DLL '{dll}' 不在 HybridCLR Settings 的热更程序集中。\n" +
                            "请先在 HybridCLR/Settings 的 hotUpdateAssemblyDefinitions 里添加对应 asmdef。");
                    }

                    var path = Path.Combine(HotUpdateDllSourceDir, name + ".dll");
                    if (!File.Exists(path))
                    {
                        throw new FileNotFoundException(
                            $"热更 DLL 不存在: {path}\n请先执行编译（菜单 3）。", path);
                    }
                }

                if (string.IsNullOrEmpty(game.entryClass) || string.IsNullOrEmpty(game.scene) ||
                    string.IsNullOrEmpty(game.label))
                {
                    throw new InvalidDataException(
                        $"游戏 {game.id} 缺少 entryClass / scene / label 字段");
                }
            }
        }

        /// <summary>全部游戏引用的 DLL 名（去重、保持首次出现顺序，即近似依赖序）。</summary>
        private static List<string> CollectRequiredDllNames(GameDefinitionFile file)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var game in file.games)
            {
                foreach (var dll in game.dlls)
                {
                    var name = Path.GetFileName(dll);
                    if (seen.Add(name))
                    {
                        names.Add(name);
                    }
                }
            }

            return names;
        }

        /// <summary>
        /// 由定义构建发布用注册表：整体版本与每游戏版本 = 当前时间戳，
        /// minAppVersion 取自 HotUpdateConfig，DLL 条目补 MD5 与大小。
        /// </summary>
        private static GameRegistry BuildRegistry(GameDefinitionFile file)
        {
            var version = DateTime.Now.ToString("yyyyMMddHHmmss");
            var registry = new GameRegistry
            {
                version = version,
                minAppVersion = HotUpdateConfig.Load().MinAppVersion,
                games = new List<GameInfo>()
            };

            foreach (var def in file.games)
            {
                var game = new GameInfo
                {
                    id = def.id,
                    name = def.name,
                    version = version,
                    entryAssembly = def.entryAssembly,
                    entryClass = def.entryClass,
                    entryMethod = def.entryMethod,
                    exitMethod = def.exitMethod,
                    scene = def.scene,
                    label = def.label,
                    dlls = new List<DllEntry>()
                };

                foreach (var dll in def.dlls)
                {
                    var name = Path.GetFileName(dll);
                    var bytes = File.ReadAllBytes(Path.Combine(HotUpdateDllSourceDir, name));
                    game.dlls.Add(new DllEntry
                    {
                        name = name,
                        md5 = ComputeMD5(bytes),
                        size = bytes.Length
                    });
                }

                registry.games.Add(game);
            }

            return registry;
        }

        // ── 辅助方法 ──────────────────────────────────────────────

        /// <summary>
        /// 计算字节数组的 MD5（16 进制小写）。
        /// </summary>
        private static string ComputeMD5(byte[] data)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(data);
            var sb = new StringBuilder();
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
