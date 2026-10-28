using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 应用主控制器 —— 对应桌面版 MainWindow。
    ///
    /// 职责：持有存档状态、搭建 Canvas、切页、通用弹窗。
    /// 页面构建统一走 <see cref="ShowPage"/>，它会延迟两帧等 Canvas 布局完成。
    /// </summary>
    public sealed class App : MonoBehaviour
    {
        public static App Instance { get; private set; }

        // ==================== 状态 ====================

        public string Mode { get; private set; } = "存档";
        public Progress Save { get; private set; } = new Progress();
        public string CurrentPage { get; private set; } = "";

        public string Category { get; set; } = "全部游戏";
        public string People { get; set; } = "全部人数";

        public bool Unlimited => Mode == "无限";
        public int Balance => Unlimited ? int.MaxValue : Save.Balance;

        public int TierIndex => Save.TierIndex;
        public int Seats => Save.Seats;

        public (int Min, int? Max, string Name) Tier => BetTiers.Get(TierIndex);
        public int TierMin => Tier.Min;
        public int TierMax => Tier.Max ?? int.MaxValue;
        public string TierName => Tier.Name;

        /// <summary>牌局累计下注上限。</summary>
        public int RoundCap => Tier.Max is null
            ? int.MaxValue
            : (int)Math.Min(int.MaxValue, (long)Tier.Max.Value * 250);

        // ==================== UI ====================

        private Canvas _canvas;
        private RectTransform _content;
        private Text _modeText;
        private Text _balanceText;

        public Transform CanvasRoot => _canvas != null ? _canvas.transform : transform;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            BuildCanvas();
            LoadSave();
            LanGameRouter.Instance.Init();
            ShowModeSelect();
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 0;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Theme.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            var bg = UiFactory.Rect(canvasGo.transform, Color.white, "Background");
            UiFactory.Fill(bg.rectTransform);

            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }

            BuildTopBar();
        }

        private void BuildTopBar()
        {
            var bar = UiFactory.Rect(_canvas.transform, Theme.TopBar, "TopBar");
            UiFactory.TopStretch(bar.rectTransform, Theme.TopBarHeight);

            _modeText = UiFactory.Label(bar.transform, "", Theme.FontHead, Theme.OnDark,
                TextAnchor.MiddleLeft, bold: true);
            var mr = _modeText.rectTransform;
            mr.anchorMin = new Vector2(0f, 0f);
            mr.anchorMax = new Vector2(0.5f, 1f);
            mr.offsetMin = new Vector2(32f, 0f);
            mr.offsetMax = new Vector2(0f, 0f);

            _balanceText = UiFactory.Label(bar.transform, "", Theme.FontHead, Theme.AccentSoft,
                TextAnchor.MiddleRight, bold: true);
            var br = _balanceText.rectTransform;
            br.anchorMin = new Vector2(0.5f, 0f);
            br.anchorMax = new Vector2(1f, 1f);
            br.offsetMin = new Vector2(0f, 0f);
            br.offsetMax = new Vector2(-32f, 0f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(_canvas.transform, false);
            _content = (RectTransform)contentGo.transform;
            _content.anchorMin = Vector2.zero;
            _content.anchorMax = Vector2.one;
            _content.offsetMin = Vector2.zero;
            _content.offsetMax = new Vector2(0f, -Theme.TopBarHeight);

            RefreshTopBar();
        }

        public void RefreshTopBar()
        {
            if (_modeText != null) _modeText.text = $"模式：{Mode}";
            if (_balanceText != null)
                _balanceText.text = Unlimited ? "余额：∞" : $"余额：${Save.Balance:N0}";
        }

        // ==================== 存档 ====================

        private void LoadSave()
        {
            Save = SaveStore.Load(Mode);
            RefreshTopBar();
        }

        public void PersistSave()
        {
            SaveStore.Save(Mode, Save);
            RefreshTopBar();
        }

        public void SetMode(string mode)
        {
            if (Mode == mode) return;
            Mode = mode;
            LoadSave();
        }

        /// <summary>扣款，余额不足返回 false。</summary>
        public bool TryPay(int amount)
        {
            if (amount <= 0) return true;
            if (Unlimited) return true;
            if (Save.Balance < amount) return false;
            Save.Balance -= amount;
            PersistSave();
            return true;
        }

        /// <summary>入账。</summary>
        public void Win(int amount)
        {
            if (amount <= 0 || Unlimited) return;
            Save.Balance += amount;
            PersistSave();
        }

        /// <summary>一局结算：写历史 + 推进任务。</summary>
        public void Settle(string gameKey, int stake, int payout, string summary)
        {
            var net = payout - stake;
            Save.Rounds++;

            HistoryStore.Append(Mode, new HistoryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Time = DateTime.Now,
                GameKey = gameKey,
                GameName = ModeDisplay.GameName(gameKey),
                Stake = stake,
                Net = net,
                Summary = summary
            });

            var tasks = TaskSystem.Load(Mode);
            TaskSystem.OnRoundSettled(tasks, gameKey, net, Mode);
            TaskSystem.Save(Mode, tasks);

            PersistSave();
        }

        // ==================== 切页 ====================

        /// <summary>当前页面的滚动容器。</summary>
        public SimpleScroll Scroller { get; private set; }

        /// <summary>
        /// 切换页面。清空内容区，延迟两帧（等 Canvas 完成布局）再构建。
        /// 否则 VStack 拿到的宽度是 0，元素会挤在一起。
        /// </summary>
        public void ShowPage(string pageId, Action<RectTransform> build)
        {
            CurrentPage = pageId;
            UiFactory.Clear(_content);

            // 先建好滚动容器，页面构建时往里填
            var pageContent = SimpleScroll.Create(_content, out var scroller);
            Scroller = scroller;

            StartCoroutine(BuildNextFrame(pageContent, build));
        }

        private IEnumerator BuildNextFrame(RectTransform pageContent, Action<RectTransform> build)
        {
            yield return null;
            yield return null;

            Canvas.ForceUpdateCanvases();

            try
            {
                build?.Invoke(pageContent);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[App] 构建页面 {CurrentPage} 失败: {ex}");
            }

            Canvas.ForceUpdateCanvases();

            // 构建完成后重算滚动范围并归位顶部
            if (Scroller != null) Scroller.Refresh(resetToTop: true);
        }

        /// <summary>重建当前页面（状态刷新用）。</summary>
        public void Reload()
        {
            switch (CurrentPage)
            {
                case "lobby": ShowLobby(); break;
                case "roulette": ShowRoulette(); break;
                case "blackjack": ShowBlackjack(); break;
                case "poker": ShowPoker(); break;
                case "three": ShowThree(); break;
                case "liar": ShowLiar(); break;
                case "tasks": ShowTasks(); break;
                case "history": ShowHistory(); break;
                default: ShowModeSelect(); break;
            }
        }

        public void ShowModeSelect() => ShowPage("mode", ModeSelectPage.Build);
        public void ShowLobby() => ShowPage("lobby", LobbyPage.Build);
        public void ShowRoulette() => ShowPage("roulette", Games.RoulettePage.Build);
        public void ShowBlackjack() => ShowPage("blackjack", Games.BlackjackPage.Build);
        public void ShowPoker() => ShowPage("poker", Games.PokerPage.Build);
        public void ShowThree() => ShowPage("three", Games.ThreePage.Build);
        public void ShowLiar() => ShowPage("liar", Games.LiarPage.Build);
        public void ShowTasks() => ShowPage("tasks", TasksPage.Build);
        public void ShowHistory() => ShowPage("history", HistoryPage.Build);
        public void ShowLan() => ShowPage("lan", LanPage.Build);

        // ==================== 弹窗 ====================

        public void Msg(string text, string title = "提示", Action onOk = null)
        {
            Dialog.Show(CanvasRoot, title, text, "确定", null, onOk, null);
        }

        public void Confirm(string title, string text, Action onOk, Action onCancel = null)
        {
            Dialog.Show(CanvasRoot, title, text, "确定", "取消", onOk, onCancel);
        }
    }
}
