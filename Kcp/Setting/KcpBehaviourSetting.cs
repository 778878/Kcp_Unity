using Kcp.Interface;

namespace Kcp.Setting
{
    public record KcpBehaviourSetting : IKcpSetting
    {
        
        public bool IsServer { get; private set; }
        
        public bool IsClient { get; private set; }
        public KcpBehaviourSetting(bool IsServer, bool IsClient)
        {
            this.IsServer = IsServer;
            this.IsClient = IsClient;
        }
        
        
    }
}