using Kcp.Interface;

namespace Kcp.Setting
{
    public class KcpRetransmissionThresholdSetting : IKcpSetting
    {
        public uint RetransmissionThreshold { get; set; }

        public KcpRetransmissionThresholdSetting(uint retransmissionThreshold)
        {
            RetransmissionThreshold = retransmissionThreshold;
        }
    }
}