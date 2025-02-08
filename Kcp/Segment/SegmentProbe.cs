namespace Kcp
{
    public class SegmentProbe
    {
        public uint ConversationId; // 会话编号
        public byte Command; // 命令类型 包括数据包、ACK确认包、窗口探测包
    }
}

