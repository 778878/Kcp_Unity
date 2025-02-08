using Kcp.Interface;

namespace Kcp.Setting
{
    public record KcpDeadLinkSetting : IKcpSetting
    {
        public uint DeadLinkCount { get; set; } //最大Dead重传次数
        
        public uint DeadProbeCount { get; set; } //最大Dead探测次数

        public KcpDeadLinkSetting()
        {
            DeadProbeCount = 5;
            DeadProbeCount = 5;
        }
        
        public KcpDeadLinkSetting(uint deadLinkCount, uint deadProbeCount)
        {
            DeadLinkCount = deadLinkCount;
            DeadProbeCount = deadProbeCount;
        }
    }
}