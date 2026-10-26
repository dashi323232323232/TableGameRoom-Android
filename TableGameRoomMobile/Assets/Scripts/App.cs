using System;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 应用主控制器 —— 对应桌面版的 MainWindow。
    ///
    /// 桌面版用 MainWindow 的 partial class + 一堆 ShowXxx() 方法切页；
    /// 这里用同一个思路：一个 App 单例持有状态，切页时清空内容容器重新构建。
    /// </summary>
    public sealed class App : MonoBehaviour
    {
        public static App Instance { get; private set; }

        // ---------- 状态（对应桌面版 MainWindow 字段）----------
        public string Mode { get; private set; } = "存档";
        public Progress Save { get; private set; } = new();
        public string CurrentPage { get; private set; } = "";
        public string Category { get; set; } = "全部游戏";
        public string People { get; set; } = "全部人数";

        public bool Unlimited => Mode == "无限";
        public int Balance => Unlimited ? int.MaxValue : Save.Balance;
        public int TierIndex => Save.TierIndex;
        public int Seats => Save.Seats;

        /// <summary>当前档位的单次下注上限。</summary>
        public int TierMax => BetTiers.Get(TierIndex).Max ?? int.MaxValue;
        public int TierMin => BetTiers.Get(TierIndex).Min;
        public string TierName => BetTiers.NameOf(TierIndex);

        /// <summary>牌局累计下注上限 = 单次上限 × 250。</summary>
        public int RoundCap => BetTiers.Get(TierIndex).Max is null
            ? int.MaxValue
            : (int)Math.Min(int.MaxValue, (long)BetTiers.Get(TierIndex).Max.Value * 250);

        // ---------- UI 根节点 ----------
        private Canvas _canvas;
        private RectTransform _safeArea;
        private RectTransform _content;
        private Text _headerMode;
        private Text _headerBalance;

        public RectTransform Content => _content;

        /// <summary>Canvas 根节点（弹窗挂到这里）。</summary>
        public Transform CanvasTransform => _canvas != null ? _canvas.transform : transform;

        // ---------- 生命周期 ----------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            BuildCanvas();
            LoadSave();
            ShowModeSelect();
        }

        private void BuildCanvas()
        {
            // Canvas
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Theme.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f; // 以宽度为准，竖屏更稳

            // 背景
            var bg = UiFactory.Background(canvasGo.transform, Theme.Background, "Background");
            UiFactory.Stretch(bg.rectTransform);

            // EventSystem
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }

            BuildChrome();
        }

        /// <summary>顶部状态条（模式 / 余额），所有页面共用。</summary>
        private void BuildChrome()
        {
            var bar = UiFactory.Background(_canvas.transform, Theme.TitleBar, "TopBar");
            UiFactory.TopBar(bar.rectTransform, 132);

            var row = UiFactory.HorizontalLayout(bar.transform, 20,
                new RectOffset(32, 32, 0, 0), TextAnchor.MiddleLeft);
            UiFactory.Stretch((RectTransform)row.transform);

            _headerMode = UiFactory.Label(row.transform, "", 34, Color.white);
            _headerMode.horizontalOverflow = HorizontalWrapMode.Overflow;
            _headerMode.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.Size(_headerMode.gameObject, 480, 64);
            UiFactory.Flex(_headerMode.gameObject, 1);

            _headerBalance = UiFactory.Label(row.transform, "", 34, Theme.AccentSoft);
            _headerBalance.horizontalOverflow = HorizontalWrapMode.Overflow;
            _headerBalance.verticalOverflow = VerticalWrapMode.Overflow;
            _headerBalance.alignment = TextAnchor.MiddleRight;
            UiFactory.Size(_headerBalance.gameObject, 480, 64);
            UiFactory.Flex(_headerBalance.gameObject, 1);

            // 内容区（状态条下方）
            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(_canvas.transform, false);
            _content = (RectTransform)contentGo.transform;
            _content.anchorMin = Vector2.zero;
            _content.anchorMax = Vector2.one;
            _content.offsetMin = new Vector2(0, 0);
            _content.offsetMax = new Vector2(0, -132);

            RefreshChrome();
        }

        public void RefreshChrome()
        {
            if (_headerMode == null) return;
            _headerMode.text = $"模式：{Mode}";
            _headerBalance.text = Unlimited ? "余额：∞" : $"余额：${Save.Balance:N0}";
        }

        // ---------- 存档 ----------

        private void LoadSave()
        {
            Save = SaveStore.Load(Mode);
            RefreshChrome();
        }

        public void PersistSave()
        {
            SaveStore.Save(Mode, Save);
            RefreshChrome();
        }

        /// <summary>扣款。返回 false 表示余额不足。</summary>
        public bool TryPay(int amount)
        {
            if (amount <= 0) return true;
            if (Unlimited) return true;
            if (Save.Balance < amount) return false;
            Save.Balance -= amount;
            PersistSave();
            return true;
        }

        /// <summary>赢钱入账。</summary>
        public void Win(int amount)
        {
            if (amount <= 0) return;
            if (Unlimited) return;
            Save.Balance += amount;
            PersistSave();
        }

        /// <summary>
        /// 一局结算：记录历史、推进任务进度。
        /// 对应桌面版的 Settle() 里那部分逻辑。
        /// </summary>
        /// <param name="gameKey">游戏标识（roulette/blackjack/poker/three/liar）</param>
        /// <param name="stake">本局投入</param>
        /// <param name="payout">本局返还（含本金）</param>
        /// <param name="summary">结算说明</param>
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

            // 推进任务进度
            var tasks = TaskSystem.Load(Mode);
            TaskSystem.OnRoundSettled(tasks, gameKey, net, Mode);
            TaskSystem.Save(Mode, tasks);

            PersistSave();
        }

        // ---------- 页面切换 ----------

        /// <summary>清空内容区并开始新页面。</summary>
        public RectTransform BeginPage(string pageId)
        {
            CurrentPage = pageId;
            UiFactory.Clear(_content);
            return _content;
        }

        public void ShowModeSelect()
        {
            var root = BeginPage("mode-select");
            UI.PageBuilder.Run(root, ModeSelectPage.Build);
        }

        public void ShowLobby()
        {
            var root = BeginPage("lobby");
            UI.PageBuilder.Run(root, LobbyPage.Build);
        }

        public void SetMode(string mode)
        {
            if (Mode == mode) return;
            Mode = mode;
            LoadSave();
        }

        // ---------- 通用对话框 ----------

        /// <summary>简单提示框（对应桌面版 Msg()）。</summary>
        public void Msg(string text, string title = "提示", Action onOk = null)
        {
            ModalDialog.Show(_canvas.transform, title, text, "确定", null, onOk);
        }

        /// <summary>确认框。</summary>
        public void Confirm(string title, string text, Action onOk, Action onCancel = null)
        {
            ModalDialog.Show(_canvas.transform, title, text, "确定", "取消", onOk, onCancel);
        }
    }
}
