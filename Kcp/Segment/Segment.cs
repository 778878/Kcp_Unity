using System;

namespace Kcp
{
    public sealed class Segment : IDisposable
    {
        public uint ConversationId; // 会话编号
        public byte Command; // 命令类型 包括数据包、ACK确认包、窗口探测包
        public byte Fragment; // 分片编号
        public ushort WindowSize; // 窗口大小
        public uint SequenceNumber; // 序列号
        public ushort Length; // 数据长度
        public Memory<byte> Data; // 数据缓冲区 (MTU)

        public void InjectData(uint conversationId, byte command, byte fragment, ushort windowSize, 
            uint SequenceNumber, ushort length, Memory<byte> data)
        {
            this.ConversationId = conversationId;
            this.Command = command;
            this.Fragment = fragment;
            this.WindowSize = windowSize;
            this.SequenceNumber = SequenceNumber;
            this.Length = length;
            this.Data = data;
        }

        public void Dispose()
        {
            // 释放托管资源
            Data = Memory<byte>.Empty; // 清空引用
        }
    }
}

