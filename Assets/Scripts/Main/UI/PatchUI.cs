using System;
using UnityEngine;
using UnityEngine.UI;

namespace Main.UI
{
    /// <summary>
    /// 热更/下载流程 UI：步骤状态 + 进度条 + 重试按钮 + 版本信息。
    ///
    /// 通过 <see cref="UiBuilder"/> 由代码构建（Overlay Canvas + uGUI），不依赖场景与预制体；
    /// 由 GameBootstrap 创建为自己的子物体，随 Bootstrap 场景卸载自动销毁。
    /// 失败策略由 GameBootstrap 决定：可重试失败用 ShowRetry，强制更新用 ShowBlocking。
    /// </summary>
    public class PatchUI : MonoBehaviour
    {
        private static readonly Color PanelColor = new(0f, 0f, 0f, 0.88f);
        private static readonly Color BarBackColor = new(1f, 1f, 1f, 0.18f);
        private static readonly Color BarFillColor = new(0.30f, 0.78f, 0.42f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color SubTextColor = new(0.75f, 0.75f, 0.75f);
        private static readonly Color ButtonColor = new(0.20f, 0.48f, 0.90f);

        private Text _statusText;
        private Text _percentText;
        private Text _versionText;
        private Image _progressFill;
        private GameObject _retryButton;

        private Action _onRetry;
        private float _targetProgress;
        private bool _indeterminate;
        private float _time;

        /// <summary>在 parent 下创建 PatchUI 并构建全部 UI 层级。</summary>
        public static PatchUI Create(Transform parent)
        {
            var go = new GameObject("PatchUI");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            return go.AddComponent<PatchUI>();
        }

        private void Awake()
        {
            Build();
            SetProgress(0f);
            SetVersions(Application.version, null);
        }

        private void Update()
        {
            _time += Time.unscaledDeltaTime;

            if (_indeterminate)
            {
                // 不确定进度：让填充条来回呼吸
                var pulse = 0.5f + 0.5f * Mathf.Sin(_time * 3f);
                _progressFill.fillAmount = Mathf.Lerp(0.08f, 0.55f, pulse);
                _percentText.text = "请稍候…";
                return;
            }

            _progressFill.fillAmount = Mathf.MoveTowards(
                _progressFill.fillAmount, _targetProgress, Time.unscaledDeltaTime * 1.5f);
            _percentText.text = $"{Mathf.RoundToInt(_progressFill.fillAmount * 100f)}%";
        }

        // ── 对外接口（由 GameBootstrap 驱动）──────────────────────

        /// <summary>显示当前步骤文本。</summary>
        public void SetStep(string step)
        {
            _statusText.text = step;
        }

        /// <summary>显示确定进度（0~1）。</summary>
        public void SetProgress(float progress)
        {
            _indeterminate = false;
            _targetProgress = Mathf.Clamp01(progress);
        }

        /// <summary>进入不确定进度模式（该步骤本身没有进度数据时）。</summary>
        public void SetIndeterminate()
        {
            _indeterminate = true;
        }

        /// <summary>更新底部版本信息；热更版本为空时显示「内置」。</summary>
        public void SetVersions(string appVersion, string hotVersion)
        {
            _versionText.text = $"App {appVersion}  |  热更 {(string.IsNullOrEmpty(hotVersion) ? "内置" : hotVersion)}";
        }

        /// <summary>可重试失败：显示信息与重试按钮（message 为 null 时保留当前文本）。</summary>
        public void ShowRetry(string message, Action onRetry)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _statusText.text = message;
            }

            _onRetry = onRetry;
            _retryButton.SetActive(onRetry != null);
            gameObject.SetActive(true);
        }

        /// <summary>不可重试的阻断提示（如强制更新 App）。</summary>
        public void ShowBlocking(string message)
        {
            ShowRetry(message, null);
        }

        /// <summary>隐藏界面（显示大厅时折叠，流程成功结束时调用）。</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>重新显示（从大厅进入某游戏时）。</summary>
        public void Show()
        {
            gameObject.SetActive(true);
        }

        // ── UI 构建 ───────────────────────────────────────────────

        private void Build()
        {
            var canvas = UiBuilder.CreateOverlayCanvas("PatchCanvas", 1000);
            canvas.transform.SetParent(transform, false);

            var root = UiBuilder.CreateImage(canvas.transform, "Panel", PanelColor);
            UiBuilder.Stretch(root.rectTransform);

            var title = UiBuilder.CreateText(root.transform, "Title", 30, TextColor, TextAnchor.UpperCenter);
            title.text = Application.productName;
            UiBuilder.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(900f, 60f));

            _statusText = UiBuilder.CreateText(root.transform, "Status", 24, TextColor, TextAnchor.UpperCenter);
            UiBuilder.SetBox(_statusText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(900f, 130f));

            var barBack = UiBuilder.CreateImage(root.transform, "ProgressBack", BarBackColor);
            UiBuilder.SetBox(barBack.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 16f));

            _progressFill = UiBuilder.CreateImage(barBack.transform, "ProgressFill", BarFillColor);
            UiBuilder.Stretch(_progressFill.rectTransform);
            _progressFill.type = Image.Type.Filled;
            _progressFill.fillMethod = Image.FillMethod.Horizontal;
            _progressFill.fillAmount = 0f;

            _percentText = UiBuilder.CreateText(root.transform, "Percent", 18, SubTextColor, TextAnchor.MiddleCenter);
            UiBuilder.SetBox(_percentText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -42f), new Vector2(300f, 26f));

            var retry = UiBuilder.CreateButton(root.transform, "RetryButton", "重 试", ButtonColor, () => _onRetry?.Invoke());
            UiBuilder.SetBox(retry.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(220f, 52f));
            _retryButton = retry.gameObject;

            _versionText = UiBuilder.CreateText(root.transform, "Version", 15, SubTextColor, TextAnchor.MiddleCenter);
            UiBuilder.SetBox(_versionText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(900f, 24f));
        }
    }
}
