using Main.Games;
using NUnit.Framework;
using UnityEngine;

namespace EditorTests
{
    /// <summary>
    /// games.json 序列化契约测试：保证运行时反序列化与编辑器打包生成始终兼容，
    /// 以及旧格式 JSON（缺字段）的向后兼容。
    /// </summary>
    public class GameRegistryTests
    {
        [Test]
        public void ToJson_FromJson_RoundTripsAllFields()
        {
            var registry = new GameRegistry
            {
                version = "20261030120000",
                minAppVersion = "1.2.0",
                games =
                {
                    new GameInfo
                    {
                        id = "demo",
                        name = "Demo 小游戏",
                        version = "20261030120000",
                        dlls = { new DllEntry { name = "HotUpdate.dll", md5 = "abc123", size = 45678 } },
                        entryAssembly = "HotUpdate",
                        entryClass = "HotUpdate.Hello",
                        entryMethod = "Run",
                        scene = "games/demo/main",
                        label = "games/demo"
                    },
                    new GameInfo
                    {
                        id = "ddz",
                        name = "欢乐斗地主",
                        version = "20261030100000",
                        dlls =
                        {
                            new DllEntry { name = "HotUpdate.Common.dll", md5 = "bbb", size = 1 },
                            new DllEntry { name = "Hot.Game.Ddz.dll", md5 = "ccc", size = 2 }
                        },
                        entryAssembly = "Hot.Game.Ddz",
                        entryClass = "Ddz.DdzGame",
                        entryMethod = "Enter",
                        scene = "games/ddz/main",
                        label = "games/ddz"
                    }
                }
            };

            var restored = JsonUtility.FromJson<GameRegistry>(JsonUtility.ToJson(registry));

            Assert.AreEqual("20261030120000", restored.version);
            Assert.AreEqual("1.2.0", restored.minAppVersion);
            Assert.AreEqual(2, restored.games.Count);

            var demo = restored.Find("demo");
            Assert.NotNull(demo, "Find 应能按 id 定位游戏");
            Assert.AreEqual("Demo 小游戏", demo.name);
            Assert.AreEqual(1, demo.dlls.Count);
            Assert.AreEqual("HotUpdate.dll", demo.dlls[0].name);
            Assert.AreEqual("abc123", demo.dlls[0].md5);
            Assert.AreEqual(45678, demo.dlls[0].size);
            Assert.AreEqual("games/demo/main", demo.scene);
            Assert.AreEqual("games/demo", demo.label);

            var ddz = restored.Find("ddz");
            Assert.AreEqual(2, ddz.dlls.Count, "多 DLL（含公共程序集）应完整保留");
            Assert.AreEqual("Ddz.DdzGame", ddz.entryClass);
            Assert.IsNull(restored.Find("not_exists"));
        }

        [Test]
        public void FromJson_MissingOptionalFields_IsNullNotThrow()
        {
            // 旧版 games.json 没有 minAppVersion；游戏条目缺 version —— 都必须反序列化成功
            const string legacyJson =
                "{\"games\":[{\"id\":\"demo\",\"name\":\"Demo\",\"dlls\":[]}]}";

            var restored = JsonUtility.FromJson<GameRegistry>(legacyJson);

            Assert.IsNull(restored.minAppVersion, "缺 minAppVersion 应视为不启用门禁");
            Assert.AreEqual(1, restored.games.Count);
            Assert.IsNull(restored.games[0].version);
            Assert.IsEmpty(restored.games[0].dlls);
        }
    }
}
