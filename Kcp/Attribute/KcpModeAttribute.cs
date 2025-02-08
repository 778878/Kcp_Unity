namespace Kcp.Attribute
{
    using System;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class KcpModeAttribute : Attribute
    {
        public bool IsServer { get; }
        public bool IsClient { get; }

        public KcpModeAttribute(bool isServer = false, bool isClient = false)
        {
            if (isServer && isClient)
                throw new ArgumentException("Kcp cannot be both server and client at the same time.");

            if (!isServer && !isClient)
                throw new ArgumentException("Kcp must be either server or client.");

            IsServer = isServer;
            IsClient = isClient;
        }
    }
}