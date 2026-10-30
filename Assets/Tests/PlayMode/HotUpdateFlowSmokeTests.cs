using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PlayModeTests
{
    /// <summary>
    /// 端到端冒烟测试：清空本地缓存后加载 Bootstrap 场景，
    /// 断言容器流程走完（单游戏形态自动进入）：games.json 获取 → 按游戏下载 →
    /// 热更入口执行 → 进入 games/demo/main 场景且 DemoCube（热更组件）实例化成功。
    ///
    /// 运行前必须先打包（代码 + Addressables）并启动 tools/serve_hotupdate.py。
    /// </summary>
    public class HotUpdateFlowSmokeTests
    {
        [UnityTest]
        public IEnumerator Bootstrap_AutoEntersSingleGame_AndInstantiatesDemoCube()
        {
            // 模拟首次安装：清掉已下载的游戏缓存，让流程走完整的按游戏下载路径
            var dataDir = Path.Combine(Application.persistentDataPath, "Data");
            if (Directory.Exists(dataDir))
            {
                Directory.Delete(dataDir, true);
            }
            Caching.ClearCache();

            SceneManager.LoadScene("Assets/AddressableAssets/Scenes/SampleScene.unity", LoadSceneMode.Single);
            yield return null;

            var deadline = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < deadline
                   && SceneManager.GetActiveScene().name != "MainMenu")
            {
                yield return null;
            }

            // 地址是 games/demo/main，但 Scene.name 取资产名 MainMenu
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name,
                "GameBootstrap 未在 90s 内进入 demo 游戏场景，流程卡住或失败，详见日志");

            // demo 游戏的本地状态应已落盘（按游戏下载路径真实执行过的证据）
            var statePath = Path.Combine(Application.persistentDataPath, "Data", "games", "demo", "state.json");
            Assert.True(File.Exists(statePath), $"游戏本地状态未保存: {statePath}");

            deadline = Time.realtimeSinceStartup + 15f;
            GameObject cube = null;
            while (Time.realtimeSinceStartup < deadline && cube == null)
            {
                // Addressables 实例化会追加 "(Clone)" 后缀
                cube = GameObject.Find("DemoCube") ?? GameObject.Find("DemoCube(Clone)");
                yield return null;
            }

            Assert.NotNull(cube, "DemoCube 未实例化");
            Assert.NotNull(cube.GetComponent("DemoCubeController"),
                "DemoCube 缺少热更程序集组件 DemoCubeController");
        }
    }
}
