using System;

namespace Kcp
{
    public sealed class SegmentAck : IDisposable
    {
        public uint ConversationId; // 会话编号
        public const byte Command = 82; // 命令类型 包括数据包、ACK确认包、窗口探测包
        public ushort WindowSize; // 窗口大小
        public uint SequenceNumber; // 序列号

        public void Dispose()
        {
            
        }
    }
    
    
}

