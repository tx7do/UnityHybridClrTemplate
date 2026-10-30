using UnityEngine;

namespace Main.Config
{
    /// <summary>
    /// 热更部署配置（唯一来源）。
    ///
    /// 资产位于 Assets/Resources/HotUpdateConfig.asset，运行时与编辑器打包工具共同读取，
    /// 切换 CDN / 调整版本策略只改这一处，无需改代码重新编译主包：
    ///
    /// - 运行时：GameBootstrap 启动时 Load，下发给 GameUpdateManager
    /// - 编辑器：HotUpdateBuildEditor 用 minAppVersion 生成 games.json；
    ///           AddressablesSetupEditor 用 serverBaseUrl 生成 Remote.LoadPath
    ///
    /// 每个游戏的入口/依赖/场景不在这里——它们在 Assets/HotUpdate/games.json（容器模型）。
    /// </summary>
    public class HotUpdateConfig : ScriptableObject
    {
        /// <summary>资产在 Resources 下的路径（不含扩展名）。</summary>
        public const string ResourcePath = "HotUpdateConfig";

        /// <summary>默认服务器地址（资产缺失时的兜底，与 tools/serve_hotupdate.py 配套）。</summary>
        public const string DefaultServerBaseUrl = "http://127.0.0.1:8080";

        [Header("服务器")]
        [Tooltip("热更服务器根地址（代码热更与 Addressables 资源共用）。本地测试 http://127.0.0.1:8080，上线改为正式 CDN。")]
        [SerializeField] private string serverBaseUrl = DefaultServerBaseUrl;

        [Tooltip("HTTP 请求超时（秒）。")]
        [SerializeField] private int requestTimeoutSeconds = 30;

        [Header("版本门禁")]
        [Tooltip("本主包要求的最低 App 版本（如 1.2.0）。打包时写入 games.json 的 minAppVersion；低于它的旧主包会收到强制更新提示。留空表示不启用。")]
        [SerializeField] private string minAppVersion = "";

        /// <summary>服务器根地址（去除尾部 /，并兜底默认值）。</summary>
        public string ServerBaseUrl =>
            string.IsNullOrWhiteSpace(serverBaseUrl) ? DefaultServerBaseUrl : serverBaseUrl.TrimEnd('/');

        /// <summary>HTTP 超时（秒），限制在 1~300。</summary>
        public int RequestTimeoutSeconds => Mathf.Clamp(requestTimeoutSeconds, 1, 300);

        /// <summary>最低 App 版本要求（空表示不启用门禁）。</summary>
        public string MinAppVersion => minAppVersion ?? string.Empty;

        /// <summary>
        /// 读取配置资产；缺失时回退到代码内默认值，保证模板零配置可跑通。
        /// </summary>
        public static HotUpdateConfig Load()
        {
            var config = Resources.Load<HotUpdateConfig>(ResourcePath);
            if (config != null)
            {
                return config;
            }

            Debug.LogWarning($"[HotUpdateConfig] 未找到 Resources/{ResourcePath}.asset，使用默认配置（服务器 {DefaultServerBaseUrl}）");
            return CreateInstance<HotUpdateConfig>();
        }
    }
}
