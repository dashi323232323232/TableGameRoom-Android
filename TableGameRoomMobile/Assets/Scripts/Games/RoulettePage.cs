using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile.Games
{
    /// <summary>
    /// 欧洲轮盘 —— 对应桌面版 Roulette.cs。
    ///
    /// 实现要点：
    /// - 37 个扇区烘焙成一张 1024×1024 贴图（比用 Mesh 拼扇区更稳）
    /// - 倒三角指针固定在顶部，不随轮盘转
    /// - 小球用协程做 4.2 秒螺旋落球（半径由外向内收，角度按圈数递增）
    /// </summary>
    public sealed class RoulettePage : MonoBehaviour
    {
        // 欧洲单零轮盘标准号码顺序
        private static readonly int[] WheelOrder =
        {
            0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5,
            24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26
        };

        private static readonly HashSet<int> RedNumbers = new HashSet<int>
        {
            1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36
        };

        // 贴图几何
        private const int TexSize = 1024;
        private const float TexCenter = TexSize / 2f;
        private const float R_Track = 500f;
        private const float R_NumOuter = 452f;
        private const float R_NumInner = 348f;
        private const float R_Hub = 150f;

        private App _app;
        private RectTransform _wheelHost;
        private RectTransform _ball;
        private Text _resultText;
        private Text _betText;
        private Text _logText;
        private Text _stakeLabel;

        private readonly List<(string Key, string Label, int Amount)> _bets =
            new List<(string, string, int)>();

        private int _stake = 100;
        private bool _spinning;

        // ==================== 入口 ====================

        public static void Build(RectTransform content)
        {
            var go = new GameObject("RoulettePage", typeof(RectTransform));
            go.transform.SetParent(content, false);
            UiFactory.Fill((RectTransform)go.transform);

            go.AddComponent<RoulettePage>().Init(content);
        }

        private void Init(RectTransform content)
        {
            _app = App.Instance;
            _stake = Mathf.Max(_app.TierMin, 100);

            var stack = new VStack(content);

            stack.Button("← 返回大厅", () => _app.ShowLobby(), 88f);

            // ---------- 轮盘 ----------
            stack.FixedCard(inner =>
            {
                // 轮盘本体（居中）
                var wheelGo = new GameObject("Wheel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                wheelGo.transform.SetParent(inner, false);
                _wheelHost = (RectTransform)wheelGo.transform;
                _wheelHost.anchorMin = _wheelHost.anchorMax = new Vector2(0.5f, 0.5f);
                _wheelHost.pivot = new Vector2(0.5f, 0.5f);
                _wheelHost.sizeDelta = new Vector2(680f, 680f);
                _wheelHost.anchoredPosition = Vector2.zero;

                var wheelImg = wheelGo.GetComponent<Image>();
                wheelImg.sprite = BakeWheel();
                wheelImg.color = Color.white;
                wheelImg.raycastTarget = false;
                wheelImg.preserveAspect = true;

                // 倒三角指针
                var pointerGo = new GameObject("Pointer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                pointerGo.transform.SetParent(_wheelHost, false);
                var pImg = pointerGo.GetComponent<Image>();
                pImg.sprite = MakeTriangle();
                pImg.color = Theme.Danger;
                pImg.raycastTarget = false;

                var pr = (RectTransform)pointerGo.transform;
                pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 1f);
                pr.pivot = new Vector2(0.5f, 1f);
                pr.sizeDelta = new Vector2(64f, 82f);
                pr.anchoredPosition = new Vector2(0f, -2f);

                // 小球
                var ballGo = new GameObject("Ball", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                ballGo.transform.SetParent(_wheelHost, false);
                var bImg = ballGo.GetComponent<Image>();
                bImg.sprite = UiFactory.CircleSprite;
                bImg.color = Color.white;
                bImg.raycastTarget = false;

                _ball = (RectTransform)ballGo.transform;
                _ball.anchorMin = _ball.anchorMax = new Vector2(0.5f, 0.5f);
                _ball.pivot = new Vector2(0.5f, 0.5f);
                _ball.sizeDelta = new Vector2(36f, 36f);
                _ball.anchoredPosition = Vector2.zero;
                ballGo.SetActive(false);
            }, 700f, Theme.Table, Theme.TableBorder, 10f);

            // ---------- 结果 ----------
            _resultText = stack.CenterText("选择一个投注项开始", Theme.FontHead, Theme.Ink,
                bold: true, height: 64f);

            // ---------- 投注金额 ----------
            _stakeLabel = stack.Text($"投注（单注 ${_stake:N0}）", Theme.FontHead, Theme.Ink, bold: true,
                minHeight: 52f);

            var stakeButtons = new List<Button>();
            foreach (var amt in new[] { 10, 20, 50, 100, 500, 1000 })
            {
                var v = amt;
                if (v > _app.TierMax) continue;

                var picked = _stake == v;
                var btn = UiFactory.Button(content, $"${v:N0}", () =>
                {
                    _stake = v;
                    _app.ShowRoulette();
                }, picked ? Theme.AccentSoft : Theme.Panel);

                stakeButtons.Add(btn);
            }
            stack.Grid(stakeButtons, 4, 84f);

            // ---------- 投注类型 ----------
            var betButtons = new List<Button>();
            foreach (var item in new (string Key, string Label)[]
            {
                ("红", "红"), ("黑", "黑"), ("单", "单"), ("双", "双"),
                ("大", "大 19-36"), ("小", "小 1-18"),
                ("打1", "1-12"), ("打2", "13-24"), ("打3", "25-36")
            })
            {
                var k = item.Key;
                var l = item.Label;
                var btn = UiFactory.Button(content, l, () => AddBet(k, l));
                betButtons.Add(btn);
            }
            stack.Grid(betButtons, 3, 88f);

            // ---------- 下注摘要 ----------
            _betText = stack.Text("尚未下注", Theme.FontBody, Theme.Muted);

            // ---------- 操作 ----------
            var actions = new List<Button>
            {
                UiFactory.Button(content, "清空退注", ClearBets),
                UiFactory.Primary(content, "开 盘", Spin)
            };
            stack.RowOf(actions, 104f);

            // ---------- 日志 ----------
            _logText = stack.Text("", Theme.FontSmall, Theme.Muted);

            stack.Finish();
        }

        // ==================== 轮盘贴图 ====================

        private Sprite _bakedWheel;

        private Sprite BakeWheel()
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

            // 外轨道
            FillRing(px, R_NumOuter, R_Track, Theme.TableBorder);
            // 号码环底
            FillRing(px, R_NumInner, R_NumOuter, Theme.Ink);

            // 37 个扇区
            for (var i = 0; i < WheelOrder.Length; i++)
            {
                var number = WheelOrder[i];
                var color = number == 0
                    ? Theme.Success
                    : RedNumbers.Contains(number) ? Theme.CardRed : Theme.Ink;

                FillSector(px, R_NumInner, R_NumOuter, i * step, step * 0.92f, color);
            }

            // 中心轮毂
            FillDisc(px, R_Hub, Color.white);
            FillRing(px, R_Hub - 5, R_Hub, Theme.AccentSoft);

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
                var dy = y - TexCenter;
                for (var x = 0; x < TexSize; x++)
                {
                    var dx = x - TexCenter;
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
                var dy = y - TexCenter;
                for (var x = 0; x < TexSize; x++)
                {
                    var dx = x - TexCenter;
                    var d2 = dx * dx + dy * dy;
                    if (d2 >= ri2 && d2 <= ro2) px[y * TexSize + x] = c;
                }
            }
        }

        private static void FillSector(Color32[] px, float inner, float outer,
            float centerAngle, float sweep, Color color)
        {
            var c = (Color32)color;
            var half = sweep / 2f;
            var a0 = centerAngle - half;
            var a1 = centerAngle + half;
            var ri2 = inner * inner;
            var ro2 = outer * outer;

            for (var y = 0; y < TexSize; y++)
            {
                var dy = y - TexCenter;
                for (var x = 0; x < TexSize; x++)
                {
                    var dx = x - TexCenter;
                    var d2 = dx * dx + dy * dy;
                    if (d2 < ri2 || d2 > ro2) continue;

                    // 0° = 正上方，顺时针
                    var angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                    if (angle < 0) angle += 360f;

                    if (InRange(angle, a0, a1)) px[y * TexSize + x] = c;
                }
            }
        }

        private static bool InRange(float angle, float start, float end)
        {
            angle = ((angle % 360f) + 360f) % 360f;
            start = ((start % 360f) + 360f) % 360f;
            end = ((end % 360f) + 360f) % 360f;

            return start <= end
                ? angle >= start && angle <= end
                : angle >= start || angle <= end;
        }

        private static Sprite _triangle;

        private static Sprite MakeTriangle()
        {
            if (_triangle != null) return _triangle;

            const int w = 64, h = 82;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    // 倒三角：顶部最宽，底部收成尖
                    var t = y / (float)(h - 1);           // 0=底 1=顶
                    var halfWidth = (1f - t) * (w / 2f);
                    var inside = Mathf.Abs(x - w / 2f) <= halfWidth;
                    px[y * w + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                }
            }

            tex.SetPixels32(px);
            tex.Apply();

            _triangle = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return _triangle;
        }

        // ==================== 投注 ====================

        private void AddBet(string key, string label)
        {
            if (_spinning) return;

            if (!_app.TryPay(_stake))
            {
                _app.Msg($"余额不足：当前 ${_app.Save.Balance:N0}", "筹码不足");
                return;
            }

            _bets.Add((key, label, _stake));
            RefreshBets();

            _resultText.text = $"已下注 {label}  ${_stake:N0}";
        }

        private void RefreshBets()
        {
            if (_bets.Count == 0)
            {
                _betText.text = "尚未下注";
                return;
            }

            var total = _bets.Sum(b => b.Amount);
            var parts = new List<string>();
            foreach (var b in _bets) parts.Add($"{b.Label} ${b.Amount:N0}");
            parts.Add($"合计 ${total:N0}");

            _betText.text = string.Join("   ", parts.ToArray());
        }

        private void ClearBets()
        {
            if (_spinning) return;

            var refund = _bets.Sum(b => b.Amount);
            if (refund > 0) _app.Win(refund);

            _bets.Clear();
            RefreshBets();

            _resultText.text = "已清空投注";
            _logText.text = "";
        }

        // ==================== 开盘 ====================

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

        private IEnumerator SpinRoutine()
        {
            _resultText.text = "开盘…";
            _logText.text = "";
            _ball.gameObject.SetActive(true);

            var target = UnityEngine.Random.Range(0, WheelOrder.Length);
            var turns = 4 + UnityEngine.Random.Range(0, 3);

            const float duration = 4.2f;
            const float startRadius = 300f;   // 452 * (680/1024)
            const float endRadius = 254f;     // 382 * (680/1024)
            var step = 360f / WheelOrder.Length;

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);   // CubicEaseOut

                var angle = eased * 360f * turns + target * step;
                var radius = Mathf.Lerp(startRadius, endRadius, eased);

                var rad = angle * Mathf.Deg2Rad;
                _ball.anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * radius;

                yield return null;
            }

            var finalRad = target * step * Mathf.Deg2Rad;
            _ball.anchoredPosition = new Vector2(Mathf.Sin(finalRad), Mathf.Cos(finalRad)) * endRadius;

            Settle(WheelOrder[target]);
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
            var staked = 0;
            var details = new List<string>();

            foreach (var bet in _bets)
            {
                staked += bet.Amount;

                var win = false;
                switch (bet.Key)
                {
                    case "红": win = isRed; break;
                    case "黑": win = isBlack; break;
                    case "单": win = isOdd; break;
                    case "双": win = isEven; break;
                    case "大": win = isBig; break;
                    case "小": win = isSmall; break;
                    case "打1": win = dozen == 1; break;
                    case "打2": win = dozen == 2; break;
                    case "打3": win = dozen == 3; break;
                }

                if (win)
                {
                    // 红黑单双大小 1:1（返还 2 倍含本金）；打 2:1（返还 3 倍）
                    var mult = bet.Key.StartsWith("打") ? 3 : 2;
                    var gained = bet.Amount * mult;
                    payout += gained;
                    details.Add($"{bet.Label} 中 +${gained:N0}");
                }
                else
                {
                    details.Add($"{bet.Label} 输 -${bet.Amount:N0}");
                }
            }

            if (payout > 0) _app.Win(payout);

            var net = payout - staked;
            var colorName = number == 0 ? "绿" : isRed ? "红" : "黑";

            _app.Settle("roulette", staked, payout, $"开出 {number}（{colorName}）");

            _resultText.text = net >= 0
                ? $"开出 {number}（{colorName}）   赢 ${net:N0}"
                : $"开出 {number}（{colorName}）   输 ${-net:N0}";

            _logText.text = string.Join("    ", details.ToArray());

            _bets.Clear();
            RefreshBets();
        }
    }
}
