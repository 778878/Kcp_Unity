using System;
using System.Net;
using System.Threading.Tasks;
using Kcp.Attribute;
using Kcp.Setting;

namespace Kcp
{
    public sealed class KcpClient : IDisposable
    {
        private uint _conversationId;
        
        [KcpMode(isClient: true)]
        private Kcp Kcp { get; set; }
        
        private KcpBehaviourSetting _kcpBehaviourSetting;
        
        private IPEndPoint LocalEndPoint { get; set; }
        
        private IPEndPoint RemoteEndPoint { get; set; }
        
        public uint ConversationId => _conversationId;

        public KcpClient(uint conversationId)
        {
            this._conversationId = conversationId;
            LocalEndPoint = new IPEndPoint(IPAddress.Any, 0);
            
            SetKcpClientBehaviour();
        }

        public KcpClient(uint conversationId, IPEndPoint localEndPoint)
        {
            this._conversationId = conversationId;
            this.LocalEndPoint = localEndPoint;
            
            SetKcpClientBehaviour();
        }

        public KcpClient(uint conversationId, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
        {
            this._conversationId = conversationId;
            this.LocalEndPoint = localEndPoint;
            this.RemoteEndPoint = remoteEndPoint;
            
            Kcp = new Kcp(_conversationId, RemoteEndPoint);

            SetKcpClientBehaviour();
            
            Kcp.SetKcpBehaviour(_kcpBehaviourSetting);
        }

        #region 外部调用

        public void SetKcpClientBehaviour()
        {
            this._kcpBehaviourSetting = new KcpBehaviourSetting(false, true);
        }
        public void Connect(IPEndPoint remoteEndPoint)
        {
            if(Kcp is not null) throw new InvalidOperationException("Already connected");
            
            Kcp = new Kcp(_conversationId, LocalEndPoint, remoteEndPoint);
            
            Kcp.SetKcpBehaviour(_kcpBehaviourSetting);
        }

        public void Disconnect()
        {
            Kcp.Dispose();
            
            Kcp = null;
        }

        public async Task ConnectAsync(IPEndPoint remoteEndPoint) => await Task.Run(() => Connect(remoteEndPoint));
        
        public void Send(ReadOnlyMemory<byte> data) => Kcp.Send(data.Span);
        
        public async Task SendAsync(ReadOnlyMemory<byte> data) => await Kcp.SendAsync(data);
        
        public void SendFast(ReadOnlyMemory<byte> data) => Kcp.SendFast(data.Span);
        
        public async Task SendFastAsync(ReadOnlyMemory<byte> data) => await Kcp.SendFastAsync(data);
        
        public ReadOnlyMemory<byte> GetData() => Kcp.GetData();
        
        public async Task<ReadOnlyMemory<byte>> GetDataAsync() => await Kcp.GetDataAsync();

        public Kcp GetClient() => Kcp;
        
        public void Test() => Kcp.Test();
        
        #endregion
        
        
        public void Dispose()
        {
            Kcp?.Dispose();
        }
    }
}

