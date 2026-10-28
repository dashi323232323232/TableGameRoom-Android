using UnityEngine;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 座位自适应布局 —— 手机屏幕窄，7 人桌必须缩小每个座位，否则放不下。
    ///
    /// 规则（按 AI 人数 n）：
    ///   n ≤ 3  → 单行，大尺寸（头像 72，单元宽 210）
    ///   n = 4  → 单行，中尺寸（头像 60，单元宽 176）
    ///   n = 5  → 单行，小尺寸（头像 52，单元宽 148）
    ///   n ≥ 6  → 两行（每行 ceil(n/2) 个），最小尺寸（头像 46，单元宽 132）
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
        private bool _centered;

        public static SeatGeometry For(int count, float hostWidth)
        {
            var g = new SeatGeometry { _count = Mathf.Max(1, count) };

            if (hostWidth <= 1f) hostWidth = 980f;

            // 单行能放下吗？留 24px 边距
            var avail = hostWidth - 24f;

            // 先按人数选基准尺寸
            if (g._count <= 3)
            {
                g.AvatarSize = 72f; g.CellWidth = 210f; g.CellHeight = 132f;
                g.ChipScale = 0.50f; g.NameFont = Theme.FontSmall - 4; g.StatusFont = Theme.FontSmall - 8;
            }
            else if (g._count == 4)
            {
                g.AvatarSize = 60f; g.CellWidth = 176f; g.CellHeight = 120f;
                g.ChipScale = 0.42f; g.NameFont = Theme.FontSmall - 6; g.StatusFont = Theme.FontSmall - 10;
            }
            else if (g._count == 5)
            {
                g.AvatarSize = 52f; g.CellWidth = 148f; g.CellHeight = 112f;
                g.ChipScale = 0.36f; g.NameFont = Theme.FontSmall - 8; g.StatusFont = Theme.FontSmall - 12;
            }
            else
            {
                g.AvatarSize = 46f; g.CellWidth = 132f; g.CellHeight = 106f;
                g.ChipScale = 0.30f; g.NameFont = Theme.FontSmall - 10; g.StatusFont = Theme.FontSmall - 12;
            }

            g._colGap = g._count <= 3 ? 12f : 6f;

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
                // 两行
                g._columns = Mathf.CeilToInt(g._count / 2f);
                g._rows = 2;
                g._rowGap = 8f;
            }

            // 夹一次：单元宽度不能超过可用宽度 / 列数
            var maxCell = (avail - (g._columns - 1) * g._colGap) / g._columns;
            if (g.CellWidth > maxCell)
            {
                var k = maxCell / g.CellWidth;
                g.CellWidth = maxCell;
                g.AvatarSize *= Mathf.Clamp(k, 0.5f, 1f);
                g.ChipScale *= Mathf.Clamp(k, 0.5f, 1f);
            }

            g.AvatarOffsetY = g._rows == 1 ? 6f : 2f;
            return g;
        }

        /// <summary>第 i 个座位相对 host 中心的位置。</summary>
        public Vector2 Position(int index)
        {
            var row = index / _columns;
            var col = index % _columns;

            // 该行实际有几个（最后一行可能不满）
            var inRow = Mathf.Min(_columns, _count - row * _columns);
            var rowWidth = inRow * CellWidth + (inRow - 1) * _colGap;
            var startX = -rowWidth / 2f;

            var x = startX + col * (CellWidth + _colGap);

            // 纵向：单行居中；两行时上排在下排之上
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

        /// <summary>两行布局时，整体占用高度（供宿主调整）。</summary>
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
