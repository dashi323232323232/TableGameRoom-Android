using UnityEngine;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 座位自适应布局 —— 手机屏幕窄，7 人桌必须缩小每个座位，否则放不下。
    ///
    /// 布局（竖排，信息层级清晰）：
    ///   ┌──────────┐
    ///   │   ◯ 头像  │   头像居中，一眼能认出是谁
    ///   │   名字    │   名字在头像下方，粗体
    ///   │  状态/注  │   状态在最下
    ///   └──────────┘
    ///
    /// 规则（按人数 n）：
    ///   n ≤ 3  → 单行，大尺寸
    ///   n = 4  → 单行，中尺寸
    ///   n = 5  → 单行，小尺寸
    ///   n ≥ 6  → 两行，最小尺寸
    ///
    /// 所有尺寸都按 host 实际宽度再夹一次，保证绝不溢出。
    /// </summary>
    public sealed class SeatGeometry
    {
        public float AvatarSize;
        public float CellWidth;
        public float CellHeight;
        public float AvatarOffsetY;
        public float ChipScale;
        public int NameFont;
        public int StatusFont;

        private int _count;
        private int _columns;
        private int _rows;
        private float _rowGap;
        private float _colGap;

        public static SeatGeometry For(int count, float hostWidth)
        {
            var g = new SeatGeometry { _count = Mathf.Max(1, count) };

            if (hostWidth <= 1f) hostWidth = 980f;

            var avail = hostWidth - 20f;

            // 先按人数选基准尺寸
            if (g._count <= 3)
            {
                g.AvatarSize = 66f; g.CellWidth = 200f; g.CellHeight = 132f;
                g.ChipScale = 0.60f; g.NameFont = Theme.FontSmall - 2; g.StatusFont = Theme.FontSmall - 8;
            }
            else if (g._count == 4)
            {
                g.AvatarSize = 56f; g.CellWidth = 170f; g.CellHeight = 120f;
                g.ChipScale = 0.52f; g.NameFont = Theme.FontSmall - 4; g.StatusFont = Theme.FontSmall - 10;
            }
            else if (g._count == 5)
            {
                g.AvatarSize = 50f; g.CellWidth = 146f; g.CellHeight = 112f;
                g.ChipScale = 0.46f; g.NameFont = Theme.FontSmall - 6; g.StatusFont = Theme.FontSmall - 12;
            }
            else
            {
                g.AvatarSize = 44f; g.CellWidth = 128f; g.CellHeight = 104f;
                g.ChipScale = 0.42f; g.NameFont = Theme.FontSmall - 8; g.StatusFont = Theme.FontSmall - 12;
            }

            g._colGap = g._count <= 3 ? 10f : 5f;

            // 单行是否溢出
            var singleRowWidth = g._count * g.CellWidth + (g._count - 1) * g._colGap;
            if (singleRowWidth <= avail)
            {
                g._columns = g._count;
                g._rows = 1;
                g._rowGap = 0f;
            }
            else
            {
                g._columns = Mathf.CeilToInt(g._count / 2f);
                g._rows = 2;
                g._rowGap = 6f;
            }

            // 夹一次：单元宽度不能超过可用宽度 / 列数
            var maxCell = (avail - (g._columns - 1) * g._colGap) / g._columns;
            if (g.CellWidth > maxCell)
            {
                var k = maxCell / g.CellWidth;
                g.CellWidth = maxCell;
                g.AvatarSize = Mathf.Clamp(g.AvatarSize * Mathf.Clamp(k, 0.6f, 1f), 34f, 80f);
                g.ChipScale *= Mathf.Clamp(k, 0.6f, 1f);
            }

            g.AvatarOffsetY = 0f;
            return g;
        }

        /// <summary>第 i 个座位相对 host 中心的位置。</summary>
        public Vector2 Position(int index)
        {
            var row = index / _columns;
            var col = index % _columns;

            var inRow = Mathf.Min(_columns, _count - row * _columns);
            var rowWidth = inRow * CellWidth + (inRow - 1) * _colGap;
            var startX = -rowWidth / 2f;

            var x = startX + col * (CellWidth + _colGap);

            float y;
            if (_rows == 1)
            {
                y = 0f;
            }
            else
            {
                var totalH = 2 * CellHeight + _rowGap;
                y = totalH / 2f - CellHeight / 2f - row * (CellHeight + _rowGap);
            }

            return new Vector2(x, y);
        }

        public float TotalHeight =>
            _rows == 1 ? CellHeight : 2 * CellHeight + _rowGap;

        /// <summary>人数多时名字太长会撑破，截断一下。</summary>
        public string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var max = _count <= 3 ? 6 : _count <= 5 ? 5 : 4;
            return name.Length <= max ? name : name.Substring(0, max);
        }
    }
}
