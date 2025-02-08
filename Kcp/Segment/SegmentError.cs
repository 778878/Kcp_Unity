
namespace Kcp
{
    public sealed class SegmentError
    {
        public uint ConversationId; // 会话编号
        public byte Command; // 命令类型 包括数据包、ACK确认包、窗口探测包
        public uint SequenceNumber; // 序列号
    }
}

