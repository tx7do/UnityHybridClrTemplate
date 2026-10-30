using System;
using System.Collections.Generic;
using Main.Games;
using UnityEngine;
using UnityEngine.UI;

namespace Main.UI
{
    /// <summary>
    /// 游戏大厅 UI：运行时代码构建的游戏列表（矩阵形态用）。
    ///
    /// games.json 只有 1 个游戏时 GameBootstrap 自动进入、不会创建本界面；
    /// N &gt; 1 时显示列表，点击按钮 → 回调 GameBootstrap 完成按需下载与进入。
    /// 生命周期：挂在 Bootstrap 场景下，进入子游戏（Single 模式加载场景）时随之销毁；
    /// 退出子游戏回到大厅 = 重新加载 Bootstrap 场景（会再次走到大厅分支）。
    /// </summary>
    public class GameHallUI : MonoBehaviour
    {
        private static readonly Color PanelColor = new(0f, 0f, 0f, 0.92f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color SubTextColor = new(0.75f, 0.75f, 0.75f);
        private static readonly Color ButtonColor = new(0.18f, 0.42f, 0.78f);

        private Text _registryVersionText;

        /// <summary>进入某游戏时收起大厅（PatchUI 会覆盖在最上层显示进度）。</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 创建大厅。每个游戏一行按钮：游戏名 + 当前状态（待下载/可更新/已就绪）。
        /// </summary>
        /// <param name="parent">挂在哪（传 GameBootstrap 的 transform，随场景销毁）</param>
        /// <param name="registry">服务端/内置注册表</param>
        /// <param name="builtinRegistry">内置注册表（用于计算状态文案，可为 null）</param>
        /// <param name="onEnter">点击某游戏时的回调</param>
        public static GameHallUI Create(Transform parent, GameRegistry registry,
            GameRegistry builtinRegistry, Action<GameInfo> onEnter)
        {
            var go = new GameObject("GameHallUI");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var hall = go.AddComponent<GameHallUI>();
            hall.Build(registry, builtinRegistry, onEnter);
            return hall;
        }

        private void Build(GameRegistry registry, GameRegistry builtinRegistry, Action<GameInfo> onEnter)
        {
            var canvas = UiBuilder.CreateOverlayCanvas("HallCanvas", 900);
            canvas.transform.SetParent(transform, false);

            var root = UiBuilder.CreateImage(canvas.transform, "Panel", PanelColor);
            UiBuilder.Stretch(root.rectTransform);

            var title = UiBuilder.CreateText(root.transform, "Title", 34, TextColor, TextAnchor.MiddleCenter);
            title.text = "游戏大厅";
            UiBuilder.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(900f, 70f));

            _registryVersionText = UiBuilder.CreateText(root.transform, "Version", 15, SubTextColor, TextAnchor.MiddleCenter);
            _registryVersionText.text = $"清单版本 {registry.version}  |  共 {registry.games.Count} 个游戏";
            UiBuilder.SetBox(_registryVersionText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(900f, 24f));

            // 按钮从上往下排；游戏数量极多时应换 ScrollView（模板保持最简实现）
            const float rowHeight = 64f;
            const float rowGap = 14f;
            var top = 240f;
            foreach (var game in registry.games)
            {
                var current = game;
                var state = GameUpdateManager.StateLabel(current, builtinRegistry);
                var button = UiBuilder.CreateButton(root.transform, $"Game_{current.id}",
                    $"{current.name}    {state}", ButtonColor, () => onEnter?.Invoke(current));
                UiBuilder.SetBox(button.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, top), new Vector2(680f, rowHeight));
                top -= rowHeight + rowGap;
            }
        }
    }
}
