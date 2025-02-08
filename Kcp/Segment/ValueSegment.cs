using System;
using System.Runtime.InteropServices;

namespace Kcp
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ValueSegment
    {
        public uint ConversationId; // 会话编号
        public byte Command; // 命令类型 包括数据包、ACK确认包、窗口探测包
        public byte Fragment; // 分片编号
        public ushort WindowSize; // 窗口大小
        public uint SequenceNumber; // 序列号
        public uint Length; // 数据长度
        public Memory<byte> Data; // 数据缓冲区 (MTU)
    }
}

