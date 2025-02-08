using Kcp.Setting;

namespace Kcp.Interface
{
    public interface IExternalSetting
    {
        void SetAckDelay(bool isDelay, int interval);
    
        void SetUpdateFrequency(double interval);
        
        void SetKcpBehaviour(KcpBehaviourSetting kcpBehaviourSetting);
        
        void SetKcpDeadLink(KcpDeadLinkSetting kcpDeadLinkSetting);
        
        void SetKcpTrafficMonitor(KcpTrafficMonitorSetting kcpTrafficMonitorSetting);
        
        void SetKcpEncrypt(IKcpEncryptor kcpKcpEncryptor);
        
        void SetKcpReTransmitThreshold(KcpRetransmissionThresholdSetting kcpRetransmissionThresholdSetting);
        
        void SetKcpAutoRetransmit(KcpAutoRetransmitSetting kcpAutoRetransmitSetting);
        
        void SetKcpLogger(IKcpLogger kcpLogger);
    }
}

