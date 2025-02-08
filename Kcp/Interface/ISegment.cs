using System;

namespace Kcp.Interface
{
    public interface ISegment
    {
        uint ConversationId { get; set; } // 会话编号
        byte Command { get; set; } // 命令类型 包括数据包、ACK确认包、窗口探测包
        byte Fragment { get; set; } // 分片编号
        ushort WindowSize { get; set; } // 窗口大小
        uint SequenceNumber { get; set; } // 序列号
        uint Length { get; set; } // 数据长度
        Memory<byte> Data { get; set; } // 数据缓冲区 (MTU)
    }
}