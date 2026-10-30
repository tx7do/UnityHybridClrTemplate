using System;
using System.Collections.Generic;

namespace Main.Games
{
    /// <summary>
    /// 游戏注册表（games.json）：容器模型的核心数据结构。
    ///
    /// 一份清单同时服务两种项目形态，切换形态只改清单内容、不改代码：
    /// - 单游戏：games 只有 1 条 → GameBootstrap 自动进入该游戏（玩家无感知大厅）
    /// - 多游戏矩阵：games 有 N 条 → 显示游戏大厅，点击后按需下载/进入
    ///
    /// 三个版本的角色：
    /// - 项目内资产 Assets/HotUpdate/games.json —— 开发者维护的"定义"（不含版本/MD5）
    /// - 服务器根目录 games.json —— 打包工具生成的"发布"（含版本/MD5/大小）
    /// - StreamingAssets/games.json —— 打包时内置的兜底"发布"（无网络/首次安装用）
    /// </summary>
    [Serializable]
    public class GameRegistry
    {
        /// <summary>清单整体版本（时间戳，每次打包重新生成）。</summary>
        public string version;

        /// <summary>全局最低 App 版本门禁（取自 HotUpdateConfig.minAppVersion）。空表示不启用。</summary>
        public string minAppVersion;

        /// <summary>全部子游戏。1 条 = 单游戏形态，N 条 = 矩阵形态。</summary>
        public List<GameInfo> games = new();

        /// <summary>按 id 查找子游戏。</summary>
        public GameInfo Find(string id)
        {
            return games.Find(g => g.id == id);
        }
    }

    /// <summary>
    /// 单个子游戏的元信息。
    /// </summary>
    [Serializable]
    public class GameInfo
    {
        /// <summary>游戏唯一 id（用于本地存储目录 Data/games/{id}/）。</summary>
        public string id;

        /// <summary>显示名（大厅列表用）。</summary>
        public string name;

        /// <summary>游戏代码包版本（时间戳，该游戏的 DLL 有变动即变化）。</summary>
        public string version;

        /// <summary>
        /// 该游戏依赖的全部热更 DLL（含公共程序集，按依赖序排列）。
        /// 加载时会跨游戏去重：已加载的程序集不会重复 Assembly.Load。
        /// </summary>
        public List<DllEntry> dlls = new();

        /// <summary>入口所在程序集名（Editor 下查找入口时优先匹配）。</summary>
        public string entryAssembly;

        /// <summary>入口类全名。</summary>
        public string entryClass;

        /// <summary>入口静态方法名。</summary>
        public string entryMethod;

        /// <summary>入口场景的 Addressable 地址。</summary>
        public string scene;

        /// <summary>该游戏资源的 Addressables label（进入游戏前按 label 预下载）。</summary>
        public string label;
    }

    /// <summary>
    /// 单个热更 DLL 的元信息。
    /// </summary>
    [Serializable]
    public class DllEntry
    {
        /// <summary>文件名（含扩展名），如 "HotUpdate.dll"（服务器根目录与本地目录均平铺）</summary>
        public string name;

        /// <summary>文件 MD5，用于校验下载完整性</summary>
        public string md5;

        /// <summary>文件字节数</summary>
        public long size;
    }
}
