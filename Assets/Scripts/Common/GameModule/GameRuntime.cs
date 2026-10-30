using System;

namespace Common.GameModule
{
    /// <summary>
    /// 热更子游戏与容器之间的运行期契约（Common 被主包与热更程序集共同引用）。
    ///
    /// 热更代码想回到大厅时调用 <see cref="RequestReturnToHall"/>，
    /// 实际处理（执行该游戏退出方法、卸载场景、重启容器流程）由主包 GameBootstrap 完成。
    /// 这样热更侧不需要引用主包代码，容器实现也可自由演进。
    /// </summary>
    public static class GameRuntime
    {
        /// <summary>容器注册的"返回大厅"处理器。</summary>
        public static Action ReturnToHallHandler;

        /// <summary>
        /// 请求返回大厅。返回 false 表示当前没有容器接管（如未在容器流程中）。
        /// </summary>
        public static bool RequestReturnToHall()
        {
            if (ReturnToHallHandler == null)
            {
                return false;
            }

            ReturnToHallHandler.Invoke();
            return true;
        }
    }
}
