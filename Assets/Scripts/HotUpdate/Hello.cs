using UnityEngine;

namespace HotUpdate
{
    public class Hello
    {
        public static void Run()
        {
            Debug.Log("Hello, World");
        }

        /// <summary>
        /// 退出方法（games.json 的 exitMethod，缺省按 "Exit" 约定查找）。
        /// 返回大厅时由容器调用，用于释放游戏自己的运行期资源。
        /// </summary>
        public static void Exit()
        {
            Debug.Log("[Hello] 游戏退出，释放资源（示例）");
        }
    }
}
