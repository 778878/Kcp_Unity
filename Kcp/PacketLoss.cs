using System;

namespace Kcp
{
    #nullable enable

    public enum LossType
    {
        快速包,
        普通包
    }
    public sealed class PacketLoss : IDisposable
    {
        public Segment? Segment { get; set; }
        
        public LossType LossType { get; set; }
    
        public uint RetransmissionCount { get; set; }

        public ulong LastRetransmissionTime { get; set; }
    
        private bool _disposed;
        public PacketLoss(Segment segment, ulong lastRetransmissionTime, LossType lossType, uint retransmissionCount)
        {
            Segment = segment;
            LossType = lossType;
            LastRetransmissionTime = lastRetransmissionTime;
            RetransmissionCount = retransmissionCount;
        }

        public void Dispose()
        {
            if (_disposed) return; // 如果已经释放，直接返回

            // 清理资源
            Segment = null; // 清理对 Segment 的引用
            LastRetransmissionTime = 0;
            RetransmissionCount = 0;

            // 标记对象为已释放
            _disposed = true;
        
        }
    }
}

