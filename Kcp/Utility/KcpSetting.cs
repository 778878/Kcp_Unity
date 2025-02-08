using System;
using Kcp.Interface;

namespace Kcp.Utility
{
    public sealed class KcpSetting : IDisposable, IKcpSetting
    {
        public bool IsDelayAck { get; set; }

        public KcpSetting(bool isDelayAck)
        {
            IsDelayAck = isDelayAck;
        }
    
        public void Dispose()
        {
        
        }
    }
}

