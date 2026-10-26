using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 欧洲轮盘 —— 对应桌面版 Roulette.cs。
    ///
    /// 绘制方式：桌面版用 WPF 的 PathGeometry 画 37 个扇区；
    /// 移动端改为**一次性烘焙成一张 1024×1024 贴图**再贴到 Image 上。
    /// 这样比用 Mesh 拼扇区更稳（不依赖 UI/Default 材质的顶点色支持），
    /// 而且静止状态下只有一个 DrawCall。
    /// </summary>
    public sealed class RoulettePage : MonoBehaviour
    {
        // 欧洲单零轮盘标准号码顺序（与桌面版一致）
        private static readonly int[] WheelOrder =
        {
            0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5,
            24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26
        };

        private static readonly HashSet<int> RedNumbers = new()
        {
            1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36
        };

        private App _app;
        private RectTransform _wheelHost;
        private Text _resultText;
        private Text _betText;
        private Text _logText;
        private RectTransform _ball;
        private Image _ballImage;

        private readonly List<(string Key, string Label, int Amount)> _bets = new();
        private int _stake = 100;
        private bool _spinning;
        private int _lastNumber = -1;
        private float _wheelRotation;

        // 轮盘几何（相对 1024 贴图）
        private const int TexSize = 1024;
        private const float Center = TexSize / 2f;
        private const float R_TrackOuter = 500f;
        private const float R_NumberOuter = 452f;
        private const float R_NumberInner = 348f;
        private const float R_Hub = 150f;

        public static void Build(RectTransform root)
        {
            var go = new GameObject("RoulettePage", typeof(RectTransform));
            go.transform.SetParent(root, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 0f);

            var page = go.AddComponent<RoulettePage>();
            try
            {
                page.Init(root);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Roulette] Init 失败: {ex}");
                throw;
            }
        }

        private FlowLayout _flow;
        private RectTransform _content;

        private void Init(RectTransform root)
        {
            _app = App.Instance;
            _stake = Math.Max(_app.TierMin, 100);

            _content = UiFactory.ScrollView(root, out _);
            _flow = new FlowLayout(_content, padding: 24, spacing: 16);

            // 返回
            var back = UiFactory.Button(_content, "← 返回大厅", () => _app.ShowLobby());
            _flow.AddButton(back, 88);

            // ---------- 轮盘 ----------
            var wheelCard = UiFactory.Card(_content, Theme.Table, Theme.TableBorder);
            var wheelCardRect = wheelCard.rectTransform;
            _flow.Add(wheelCardRect, 960);

            var wheelGo = new GameObject("Wheel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            wheelGo.transform.SetParent(wheelCardRect, false);
            _wheelHost = (RectTransform)wheelGo.transform;
            _wheelHost.anchorMin = _wheelHost.anchorMax = new Vector2(0.5f, 0.5f);
            _wheelHost.pivot = new Vector2(0.5f, 0.5f);
            _wheelHost.sizeDelta = new Vector2(900, 900);
            _wheelHost.anchoredPosition = Vector2.zero;

            var wheelImg = wheelGo.GetComponent<Image>();
            wheelImg.sprite = BakeWheelSprite();
            wheelImg.color = Color.white;
            wheelImg.raycastTarget = false;
            wheelImg.preserveAspect = true;

            // 倒三角指针
            var pointerGo = new GameObject("Pointer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            pointerGo.transform.SetParent(_wheelHost, false);
            var pImg = pointerGo.GetComponent<Image>();
            pImg.sprite = TriangleSprite();
            pImg.color = Theme.Danger;
            pImg.raycastTarget = false;
            var pr = (RectTransform)pointerGo.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 1f);
            pr.pivot = new Vector2(0.5f, 1f);
            pr.sizeDelta = new Vector2(60, 76);
            pr.anchoredPosition = new Vector2(0, -4);

            // 小球
            var ballGo = new GameObject("Ball", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ballGo.transform.SetParent(_wheelHost, false);
            _ballImage = ballGo.GetComponent<Image>();
            _ballImage.sprite = CircleSprite();
            _ballImage.color = Color.white;
            _ballImage.raycastTarget = false;
            _ball = (RectTransform)ballGo.transform;
            _ball.anchorMin = _ball.anchorMax = new Vector2(0.5f, 0.5f);
            _ball.pivot = new Vector2(0.5f, 0.5f);
            _ball.sizeDelta = new Vector2(36, 36);
            _ball.anchoredPosition = Vector2.zero;
            ballGo.SetActive(false);

            // ---------- 结果 ----------
            _resultText = UiFactory.Label(_content, "选择一个投注项开始", 36, Theme.Ink,
                TextAnchor.MiddleCenter, bold: true);
            _flow.AddText(_resultText, 60);

            // ---------- 投注金额 ----------
            var stakeTitle = UiFactory.Label(_content, $"投注（单注 ${_stake:N0}）", 32, Theme.Ink,
                TextAnchor.UpperLeft, bold: true);
            _flow.AddText(stakeTitle, 50);

            var stakeButtons = new List<Button>();
            var amounts = new[] { 10, 20, 50, 100, 500, 1000 };
            foreach (var amt in amounts)
            {
                var v = amt;
                if (v > _app.TierMax) continue;
                var picked = _stake == v;
                var btn = UiFactory.Button(_content, $"${v:N0}", () =>
                {
                    _stake = v;
                    RebuildPage();
                }, picked ? Theme.AccentSoft : Theme.Panel);
                stakeButtons.Add(btn);
            }
            _flow.AddGrid(stakeButtons, 4, 86);

            // ---------- 投注类型 ----------
            var kinds = new (string Key, string Label)[]
            {
                ("红", "红"), ("黑", "黑"), ("单", "单"), ("双", "双"),
                ("大", "大 19-36"), ("小", "小 1-18"),
                ("打1", "1-12"), ("打2", "13-24"), ("打3", "25-36")
            };

            var betButtons = new List<Button>();
            foreach (var item in kinds)
            {
                var k = item.Key;
                var l = item.Label;
                var btn = UiFactory.Button(_content, item.Label, () => AddBet(k, l));
                betButtons.Add(btn);
            }
            _flow.AddGrid(betButtons, 3, 86);

            // ---------- 下注摘要 ----------
            _betText = UiFactory.Label(_content, "尚未下注", 28, Theme.Muted);
            _flow.AddText(_betText);

            // ---------- 操作 ----------
            var actionRow = UiFactory.HorizontalLayout(_content, 12, null, TextAnchor.MiddleCenter);
            _flow.Add((RectTransform)actionRow.transform, 110);

            var clearBtn = UiFactory.Button(actionRow.transform, "清空退注", ClearBets);
            UiFactory.Size(clearBtn.gameObject, 280, 100);

            var spinBtn = UiFactory.PrimaryButton(actionRow.transform, "开 盘", Spin);
            UiFactory.Size(spinBtn.gameObject, 380, 100);

            // ---------- 日志 ----------
            _logText = UiFactory.Label(_content, "", 24, Theme.Muted);
            _flow.AddText(_logText);

            _flow.Finish();
        }

        private void RebuildPage()
        {
            var root = _app.BeginPage("roulette");
            PageBuilder.Run(root, Build);
        }

        // ---------- 贴图烘焙 ----------

        private Sprite _bakedWheel;

        private Sprite BakeWheelSprite()
        {
            if (_bakedWheel != null) return _bakedWheel;

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var px = new Color32[TexSize * TexSize];
            var clear = new Color32(0, 0, 0, 0);

            for (var i = 0; i < px.Length; i++) px[i] = clear;

            var step = 360f / WheelOrder.Length;

            // 1) 外轨道
            FillRing(px, 0, R_TrackOuter, Theme.TableBorder);
            // 2) 号码环底色
            FillRing(px, R_NumberInner, R_NumberOuter, Theme.Ink);

            // 3) 37 个扇区
            for (var i = 0; i < WheelOrder.Length; i++)
            {
                var number = WheelOrder[i];
                var color = number == 0 ? Theme.Success
                    : RedNumbers.Contains(number) ? Theme.CardRed
                    : Theme.Ink;

                // 扇区中线对准 i*step（0 在正上方）
                var centerAngle = i * step;
                FillSector(px, R_NumberInner, R_NumberOuter, centerAngle, step * 0.94f, color);
            }

            // 4) 中心轮毂
            FillDisc(px, R_Hub, Color.white);
            FillRing(px, R_Hub - 6, R_Hub, Theme.AccentSoft);

            tex.SetPixels32(px);
            tex.Apply();

            _bakedWheel = Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, 0.5f));
            return _bakedWheel;
        }

        private static void FillDisc(Color32[] px, float radius, Color color)
        {
            var c = (Color32)color;
            var r2 = radius * radius;
            for (var y = 0; y < TexSize; y++)
            {
                var dy = y - Center;
                for (var x = 0; x < TexSize; x++)
                {
                    var dx = x - Center;
                    if (dx * dx + dy * dy <= r2) px[y * TexSize + x] = c;
                }
            }
        }

        private static void FillRing(Color32[] px, float inner, float outer, Color color)
        {
            var c = (Color32)color;
            var ri2 = inner * inner;
            var ro2 = outer * outer;
            for (var y = 0; y < TexSize; y++)
            {
                var dy = y - Center;
                for (var x = 0; x < TexSize; x++)
                {
                    var dx = x - Center;
                    var d2 = dx * dx + dy * dy;
                    if (d2 >= ri2 && d2 <= ro2) px[y * TexSize + x] = c;
                }
            }
        }

        /// <summary>填充一个扇区（角度 0 在正上方，顺时针）。</summary>
        private static void FillSector(Color32[] px, float inner, float outer,
            float centerAngleDeg, float sweepDeg, Color color)
        {
            var c = (Color32)color;
            var half = sweepDeg / 2f;
            var startAngle = centerAngleDeg - half;
            var endAngle = centerAngleDeg + half;

            var ri2 = inner * inner;
            var ro2 = outer * outer;

            for (var y = 0; y < TexSize; y++)
            {
                var dy = y - Center;
                for (var x = 0; x < TexSize; x++)
                {
                    var dx = x - Center;
                    var d2 = dx * dx + dy * dy;
                    if (d2 < ri2 || d2 > ro2) continue;

                    // 屏幕坐标 y 向上，所以角度取负；0° = 正上方
                    var angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                    if (angle < 0) angle += 360f;

                    if (AngleInRange(angle, startAngle, endAngle))
                        px[y * TexSize + x] = c;
                }
            }
        }

        private static bool AngleInRange(float angle, float start, float end)
        {
            // 归一化到 0..360
            angle = ((angle % 360f) + 360f) % 360f;
            start = ((start % 360f) + 360f) % 360f;
            end = ((end % 360f) + 360f) % 360f;

            return start <= end
                ? angle >= start && angle <= end
                : angle >= start || angle <= end;
        }

        private static Sprite _circleSprite;
        private static Sprite CircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            var c = (size - 1) / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    var a = Mathf.Clamp01(c - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _circleSprite;
        }

        private static Sprite _triDown;
        private static Sprite TriangleSprite()
        {
            if (_triDown != null) return _triDown;

            const int w = 64, h = 80;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    // 倒三角：顶部最宽，底部收成尖
                    var t = y / (float)(h - 1);       // 0=底 1=顶
                    var halfWidth = (1f - t) * (w / 2f);
                    var inside = Mathf.Abs(x - w / 2f) <= halfWidth;
                    px[y * w + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();

            _triDown = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return _triDown;
        }

        // ---------- 投注 ----------

        private void BuildStakeRow(RectTransform parent)
        {
            var row = UiFactory.HorizontalLayout(parent, 10, null, TextAnchor.MiddleLeft);
            UiFactory.Size(row.gameObject, -1, 92);

            var amounts = new[] { 10, 20, 50, 100, 500, 1000 };
            foreach (var amt in amounts)
            {
                var v = amt;
                if (v > _app.TierMax) continue;

                var picked = _stake == v;
                var btn = UiFactory.Button(row.transform, $"${v:N0}",
                    () => { _stake = v; _app.ShowLobby(); RefreshStakeOnly(); },
                    picked ? Theme.AccentSoft : Theme.Panel);
                UiFactory.Size(btn.gameObject, 150, 84);
            }
        }

        private void RefreshStakeOnly()
        {
            // 简单重建当前页
            _app.ShowLobby();
            var root = _app.BeginPage("roulette");
            Build(root);
        }

        private void BuildBetButtons(RectTransform parent)
        {
            var kinds = new (string Key, string Label)[]
            {
                ("红", "红"), ("黑", "黑"), ("单", "单"), ("双", "双"),
                ("大", "大 19-36"), ("小", "小 1-18"),
                ("打1", "1-12"), ("打2", "13-24"), ("打3", "25-36")
            };

            var row = UiFactory.HorizontalLayout(parent, 8, null, TextAnchor.MiddleLeft);
            UiFactory.Size(row.gameObject, -1, 96);

            foreach (var (key, label) in kinds)
            {
                var k = key;
                var l = label;
                var btn = UiFactory.Button(row.transform, label, () => AddBet(k, l));
                UiFactory.Size(btn.gameObject, -1, 86);
            }
        }

        private void AddBet(string key, string label)
        {
            if (_spinning) return;

            if (!_app.TryPay(_stake))
            {
                _app.Msg($"余额不足：当前 ${_app.Save.Balance:N0}", "筹码不足");
                return;
            }

            _bets.Add((key, label, _stake));
            RefreshBetText();
            _resultText.text = $"已下注 {label}  ${_stake:N0}";
        }

        private void RefreshBetText()
        {
            if (_bets.Count == 0)
            {
                _betText.text = "尚未下注";
                return;
            }

            var total = _bets.Sum(b => b.Amount);
            var lines = _bets.Select(b => $"{b.Label} ${b.Amount:N0}").ToList();
            lines.Add($"合计 ${total:N0}");
            _betText.text = string.Join("  ·  ", lines);
        }

        private void ClearBets()
        {
            if (_spinning) return;

            var refund = _bets.Sum(b => b.Amount);
            if (refund > 0) _app.Win(refund);

            _bets.Clear();
            RefreshBetText();
            _resultText.text = "已清空投注";
            _logText.text = "";
        }

        // ---------- 开盘 ----------

        private void Spin()
        {
            if (_spinning) return;
            if (_bets.Count == 0)
            {
                _app.Msg("请先下注", "提示");
                return;
            }

            _spinning = true;
            StartCoroutine(SpinRoutine());
        }

        private System.Collections.IEnumerator SpinRoutine()
        {
            _resultText.text = "开盘…";
            _logText.text = "";
            if (_ball != null) _ball.gameObject.SetActive(true);

            var target = UnityEngine.Random.Range(0, WheelOrder.Length);
            var turns = 4 + UnityEngine.Random.Range(0, 3);
            const float duration = 4.2f;
            var elapsed = 0f;

            const float startRadius = 452f;
            const float endRadius = 380f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);   // CubicEaseOut

                // 小球绕圈：总角度 = 圈数 × 360 + 目标扇区角度
                var targetAngle = target * (360f / WheelOrder.Length);
                var angle = eased * 360f * turns + targetAngle;
                var radius = Mathf.Lerp(startRadius, endRadius, eased);

                // 0° = 正上方，顺时针
                var rad = angle * Mathf.Deg2Rad;
                _ball.anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * radius;

                yield return null;
            }

            var finalRad = (target * (360f / WheelOrder.Length)) * Mathf.Deg2Rad;
            _ball.anchoredPosition = new Vector2(Mathf.Sin(finalRad), Mathf.Cos(finalRad)) * endRadius;

            var number = WheelOrder[target];
            _lastNumber = number;
            Settle(number);
            _spinning = false;
        }

        private void Settle(int number)
        {
            var isRed = number != 0 && RedNumbers.Contains(number);
            var isBlack = number != 0 && !RedNumbers.Contains(number);
            var isOdd = number % 2 == 1;
            var isEven = number != 0 && number % 2 == 0;
            var isBig = number >= 19;
            var isSmall = number >= 1 && number <= 18;
            var dozen = number == 0 ? 0 : (number - 1) / 12 + 1;

            var payout = 0;
            var details = new List<string>();

            foreach (var (key, label, amount) in _bets)
            {
                var win = key switch
                {
                    "红" => isRed,
                    "黑" => isBlack,
                    "单" => isOdd,
                    "双" => isEven,
                    "大" => isBig,
                    "小" => isSmall,
                    "打1" => dozen == 1,
                    "打2" => dozen == 2,
                    "打3" => dozen == 3,
                    _ => false
                };

                if (win)
                {
                    // 红黑单双大小 1:1（含本金），打 2:1
                    var mult = key.StartsWith("打") ? 3 : 2;
                    var gained = amount * mult;
                    payout += gained;
                    details.Add($"{label} 中 +${gained:N0}");
                }
                else
                {
                    details.Add($"{label} 输 -${amount:N0}");
                }
            }

            if (payout > 0) _app.Win(payout);

            var staked = _bets.Sum(b => b.Amount);
            var net = payout - staked;
            var colorName = number == 0 ? "绿" : isRed ? "红" : "黑";

            _app.Settle("roulette", staked, payout, $"开出 {number}（{colorName}）");

            _resultText.text = net >= 0
                ? $"开出 {number}（{colorName}）  赢 ${net:N0}"
                : $"开出 {number}（{colorName}）  输 ${-net:N0}";

            _logText.text = string.Join("   ", details);

            _bets.Clear();
            RefreshBetText();
        }
    }
}
