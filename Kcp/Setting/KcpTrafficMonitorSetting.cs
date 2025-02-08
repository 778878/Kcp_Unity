using Kcp.Interface;

namespace Kcp.Setting
{
    public sealed class KcpTrafficMonitorSetting : IKcpSetting
    {
        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }

        public void AddSent(long bytesSent)
        {
            BytesSent += bytesSent;
        }

        public void AddReceived(long bytesReceived)
        {
            BytesReceived += bytesReceived;
        }

        public void ResetStatistics()
        {
            BytesSent = 0;
            BytesReceived = 0;
        }
    }
}