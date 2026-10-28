using TableGameRoom.Mobile.Net;

namespace TableGameRoom.Mobile.Core
{
    /// <summary>
    /// 网络消息的编解码 —— 与 Windows 端**线格式完全一致**。
    ///
    /// 历史教训：
    ///   早期安卓端因为 Unity 没有 System.Text.Json，改用了自定义行式文本格式
    ///   （`Type=hello\nPlayerName=qy130`）。安卓内部自洽，但与 Windows 端的
    ///   标准 JSON 完全不兼容，双端联机无法互通。
    ///
    ///   现在 <see cref="Json"/> 已重写为完整的标准 JSON 序列化器，
    ///   所以这里直接输出 JSON，两端可以互相解析。
    ///
    /// 格式示例：
    ///   {"Type":"hello","PlayerName":"qy130"}
    ///   {"Type":"session","Session":{"SessionName":"...","Port":47800,"Players":[...]}}
    /// </summary>
    public static class WireFormat
    {
        /// <summary>把消息序列化成 JSON 一行（不含换行）。</summary>
        public static string Encode(NetMessage msg)
        {
            if (msg == null) return "{}";
            return Json.Serialize(msg);
        }

        /// <summary>从 JSON 一行解析出消息。</summary>
        public static NetMessage Decode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new NetMessage();

            var msg = Json.Deserialize<NetMessage>(text);
            return msg ?? new NetMessage();
        }
    }
}
