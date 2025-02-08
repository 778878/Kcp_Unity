using Kcp.Interface;

namespace Kcp.Setting
{
    public class KcpAutoRetransmitSetting : IKcpSetting
    {
        public uint RetransmitInterval { get; set; }
        
        public uint RttSamplingInterval { get; set; }

        public KcpAutoRetransmitSetting(uint retransmitInterval)
        {
            RetransmitInterval = retransmitInterval;
        }

        public KcpAutoRetransmitSetting(uint retransmitInterval, uint rttSamplingInterval)
        {
            RttSamplingInterval = rttSamplingInterval;
            RetransmitInterval = retransmitInterval;
        }
    }
}