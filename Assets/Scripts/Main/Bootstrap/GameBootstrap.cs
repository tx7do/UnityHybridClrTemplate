using System.Collections;
using Common.Utilities;
using Main.Config;
using Main.Games;
using Main.HybridCLR;
using Main.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Main.Bootstrap
{
    /// <summary>
    /// 游戏容器入口：单游戏与多游戏矩阵共用的一条流程。
    ///
    /// 流程：
    /// 1. 初始化 HybridCLR（加载 AOT 补充元数据）
    /// 2. 获取注册表：远程 games.json → 失败回退 StreamingAssets 内置
    /// 3. 全局最低 App 版本门禁（registry.minAppVersion）
    /// 4. 初始化 Addressables
    /// 5. 分形态：
    ///    - games 只有 1 条 → 自动进入（等价于旧"单游戏整体热更"体验）
    ///    - games 有 N 条   → 显示游戏大厅，点击后按需下载并进入
    ///
    /// 进入单个游戏（EnterGameRoutine）：
    /// 按游戏对比版本 → 下载其 DLL（MD5 校验）→ Assembly.Load（跨游戏去重）
    /// → 执行该游戏入口 → 按其 label 预下载资源 → 加载其入口场景。
    ///
    /// 失败策略：
    /// - 注册表/某游戏的下载失败：显示重试，重试只重跑当前环节
    /// - App 版本低于门禁：阻断并提示更新 App（无重试）
    /// - 服务器完全不可达：回退内置清单与内置 DLL（离线可玩内置版本）
    ///
    /// 部署参数（服务器地址、超时）来自 Resources/HotUpdateConfig.asset；
    /// 游戏清单的"定义"在 Assets/HotUpdate/games.json，由打包工具发布。
    /// 将此组件挂载到 Bootstrap 场景（SampleScene）中的 GameObject 上。
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("代码热更新")]
        [Tooltip("是否联网检查更新。关闭 = 离线模式，直接使用 StreamingAssets 内置清单与 DLL。")]
        [SerializeField] private bool enableHotUpdate = true;

        private HotUpdateConfig _config;
        private PatchUI _ui;
        private GameHallUI _hall;
        private GameRegistry _builtinRegistry;
        private GameInfo _currentGame;
        private Coroutine _flowCoroutine;

        private static void Log(string msg)
        {
            Debug.Log($"[GameBootstrap] {msg}");
        }

        private void Start()
        {
            Application.targetFrameRate = 60;

            _config = HotUpdateConfig.Load();
            GameUpdateManager.Configure(_config);

            _ui = PatchUI.Create(transform);
            _ui.SetVersions(Application.version, null);

            Log($"启动容器流程（服务器: {_config.ServerBaseUrl}）");
            _flowCoroutine = StartCoroutine(RunFlow());
        }

        // ── 启动流程 ──────────────────────────────────────────────

        private IEnumerator RunFlow()
        {
            _ui.SetStep("正在初始化…");
            _ui.SetIndeterminate();

            // ① HybridCLR：加载 AOT 补充元数据
            Log("① 初始化 HybridCLR");
            HybridClrManager.Initialize();
            Log("HybridCLR 初始化完成");

            // ② 获取注册表（远程优先，内置兜底）
            _ui.SetStep("正在获取游戏清单…");
            Log("② 获取游戏注册表");
            _builtinRegistry = GameUpdateManager.GetBuiltinRegistry();

            GameRegistry registry = null;
            if (enableHotUpdate)
            {
                var fetchTask = GameUpdateManager.FetchRemoteRegistry();
                yield return WaitForTask(fetchTask);
                if (!fetchTask.IsFaulted)
                {
                    registry = fetchTask.Result;
                }
                else
                {
                    Log($"获取远程注册表异常: {fetchTask.Exception?.GetBaseException().Message}");
                }
            }

            registry ??= _builtinRegistry;
            if (registry?.games == null || registry.games.Count == 0)
            {
                _ui.ShowRetry(
                    "获取游戏清单失败（服务器不可达且无内置清单）。\n请确认已执行打包、games.json 已生成且本地服务器已启动。",
                    RetryFlow);
                yield break;
            }

            Log($"注册表就绪: {registry.games.Count} 个游戏 (version={registry.version ?? "内置"})");
            _ui.SetVersions(Application.version, registry.version);

            // ③ 全局最低 App 版本门禁
            if (VersionUtils.IsBelow(Application.version, registry.minAppVersion))
            {
                var message = $"当前 App 版本 {Application.version} 低于要求的最低版本 " +
                              $"{registry.minAppVersion}，请更新 App 后重试。";
                Log(message);
                _ui.SetStep("需要更新 App");
                _ui.ShowBlocking(message);
                yield break;
            }

            // ④ 初始化 Addressables（进入任何游戏前都需要）
            _ui.SetStep("正在初始化 Addressables…");
            Log("④ 初始化 Addressables");
            // autoReleaseHandle 必须为 false：若 catalog 已缓存，初始化会在协程恢复前完成，
            // 自动释放后的句柄再读 Status 会抛 invalid operation handle
            var initHandle = Addressables.InitializeAsync(autoReleaseHandle: false);
            yield return initHandle;

            if (initHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Log("Addressables 初始化失败，无法加载游戏场景");
                _ui.ShowRetry($"Addressables 初始化失败：{initHandle.OperationException?.Message}", RetryFlow);
                Addressables.Release(initHandle);
                yield break;
            }

            Log("Addressables 初始化成功");
            Addressables.Release(initHandle);

            // ⑤ 分形态
            if (registry.games.Count == 1)
            {
                Log("⑤ 单游戏形态，自动进入");
                yield return EnterGameRoutine(registry.games[0]);
            }
            else
            {
                Log($"⑤ 矩阵形态（{registry.games.Count} 个游戏），显示大厅");
                ShowHall(registry);
            }
        }

        /// <summary>重试：重跑当前环节（整个启动流程，或当前进入的游戏）。</summary>
        private void RetryFlow()
        {
            Log("用户触发重试");
            if (_flowCoroutine != null)
            {
                StopCoroutine(_flowCoroutine);
            }

            _flowCoroutine = _currentGame != null
                ? StartCoroutine(EnterGameRoutine(_currentGame))
                : StartCoroutine(RunFlow());
        }

        // ── 进入单个游戏 ──────────────────────────────────────────

        /// <summary>
        /// 按需更新并进入一个游戏。单游戏形态由启动流程调用；矩阵形态由大厅按钮触发。
        /// </summary>
        public void EnterGame(GameInfo game)
        {
            _currentGame = game;
            if (_flowCoroutine != null)
            {
                StopCoroutine(_flowCoroutine);
            }

            _flowCoroutine = StartCoroutine(EnterGameRoutine(game));
        }

        private IEnumerator EnterGameRoutine(GameInfo remoteGame)
        {
            _currentGame = remoteGame;
            var gameName = string.IsNullOrEmpty(remoteGame.name) ? remoteGame.id : remoteGame.name;
            _ui.Show(); // 从大厅进入时确保 PatchUI 可见
            _ui.SetStep($"进入 {gameName}…");
            _ui.SetIndeterminate();

            // 1) 按游戏检查并下载代码
            var local = GameUpdateManager.ResolveLocal(remoteGame, _builtinRegistry);
            var effective = local ?? remoteGame;
            if (enableHotUpdate && GameUpdateManager.HasUpdate(remoteGame, local))
            {
                _ui.SetStep($"下载 {gameName} v{remoteGame.version}…");
                Log($"[{remoteGame.id}] 发现新版本 {remoteGame.version}，开始下载");
                var downloadTask = GameUpdateManager.DownloadGame(remoteGame, OnDownloadProgress);
                yield return WaitForTask(downloadTask);

                if (downloadTask.IsFaulted)
                {
                    FailRetryable($"{gameName} 下载失败: {downloadTask.Exception?.GetBaseException().Message}");
                    yield break;
                }

                if (!downloadTask.Result)
                {
                    FailRetryable($"{gameName} 下载失败（校验或写入错误）");
                    yield break;
                }

                effective = remoteGame;
                Log($"[{remoteGame.id}] 下载成功");
            }

            _ui.SetVersions(Application.version, effective.version);

            // 2) 加载热更程序集并执行该游戏入口
            _ui.SetStep($"加载 {gameName} 热更代码…");
            _ui.SetIndeterminate();
            if (!HotUpdateLoader.EnsureLoaded(effective, Log) || !HotUpdateLoader.InvokeEntry(effective, Log))
            {
                // 入口失败意味着该游戏逻辑不可用，与整体降级不同，这里选择可重试阻断
                FailRetryable($"{gameName} 热更代码加载失败，详见日志");
                yield break;
            }

            // 3) 按该游戏的 label 预下载资源；失败不阻塞（场景加载会按需拉取）
            //    先解析 label 对应的全部资源位置再下载：label 常同时命中场景与预制体等不同类型，
            //    直接 DownloadDependenciesAsync(label) 会因类型歧义抛 InvalidKeyException
            _ui.SetStep($"下载 {gameName} 资源…");
            _ui.SetIndeterminate();
            Log($"[{remoteGame.id}] 预下载资源 (label={effective.label})");
            var locHandle = Addressables.LoadResourceLocationsAsync(effective.label, typeof(object));
            while (!locHandle.IsDone)
            {
                yield return null;
            }

            if (locHandle.Status == AsyncOperationStatus.Succeeded
                && locHandle.Result != null
                && locHandle.Result.Count > 0)
            {
                Log($"[{remoteGame.id}] label 命中 {locHandle.Result.Count} 个资源位置，开始下载");
                // autoReleaseHandle 必须 false：自动释放后读 Status 会抛 invalid operation handle
                var downloadHandle = Addressables.DownloadDependenciesAsync(locHandle.Result, false);
                while (!downloadHandle.IsDone)
                {
                    OnDownloadProgress(downloadHandle.PercentComplete);
                    yield return null;
                }

                if (downloadHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    Log($"[{remoteGame.id}] 资源就绪");
                }
                else
                {
                    Log($"[{remoteGame.id}] 资源预下载失败（加载时按需拉取）: {downloadHandle.OperationException?.Message}");
                }

                Addressables.Release(downloadHandle);
            }
            else
            {
                Log($"[{remoteGame.id}] label '{effective.label}' 未匹配到资源，跳过预下载");
            }

            Addressables.Release(locHandle);

            // 4) 加载该游戏的入口场景
            _ui.SetStep($"加载 {gameName} 场景…");
            var loadHandle = Addressables.LoadSceneAsync(effective.scene);
            while (!loadHandle.IsDone)
            {
                _ui.SetProgress(loadHandle.PercentComplete);
                OnDownloadProgress(loadHandle.PercentComplete);
                yield return null;
            }

            if (loadHandle.Status != AsyncOperationStatus.Succeeded)
            {
                FailRetryable(
                    $"{gameName} 场景加载失败：\n{loadHandle.OperationException?.Message}\n\n" +
                    $"请确认已执行 Addressables 打包（场景地址 '{effective.scene}'）且服务器已启动。");
                yield break;
            }

            _ui.Hide();
            Log($"[{remoteGame.id}] 进入完成: {effective.scene}");
            // Single 模式加载会卸载 Bootstrap 场景，本组件与大厅/补丁 UI 一并销毁；
            // 退出子游戏回大厅 = 重新加载 Bootstrap 场景（流程会再次走到大厅分支）
        }

        private void FailRetryable(string message)
        {
            Log(message);
            _ui.SetStep("出错了");
            _ui.ShowRetry(message, RetryFlow);
        }

        // ── 大厅 ──────────────────────────────────────────────────

        private void ShowHall(GameRegistry registry)
        {
            _ui.Hide();
            _hall = GameHallUI.Create(transform, registry, _builtinRegistry, game =>
            {
                _hall?.Hide(); // 收起大厅，避免补丁面板下方按钮仍可点击
                EnterGame(game);
            });
        }

        // ── 辅助 ──────────────────────────────────────────────────

        /// <summary>将 Task 转为 IEnumerator，在协程中逐帧等待完成。</summary>
        private static IEnumerator WaitForTask(System.Threading.Tasks.Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
        }

        private void OnDownloadProgress(float progress)
        {
            _ui.SetProgress(progress);
            var percent = Mathf.RoundToInt(progress * 100f);
            if (percent % 10 == 0)
            {
                Log($"下载进度: {percent}%");
            }
        }
    }
}
