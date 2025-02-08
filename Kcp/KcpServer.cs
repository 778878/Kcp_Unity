using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;
using Kcp.Attribute;
using Kcp.Setting;

namespace Kcp
{
    public sealed class KcpServer<T> : IDisposable where T : IComparable<uint>
    {
        [KcpMode(isServer: true)] 
        private ConcurrentDictionary<T, Kcp> clients = new ConcurrentDictionary<T, Kcp>();

        private IPEndPoint LocalEndPoint { get; set; }

        // 用于存储属性信息的字段
        private KcpBehaviourSetting _kcpBehaviourSetting;

        public KcpServer()
        {
            _kcpBehaviourSetting = new KcpBehaviourSetting(true, false);
        }

        public KcpServer(IPEndPoint localEndPoint)
        {
            LocalEndPoint = localEndPoint;
            _kcpBehaviourSetting = new KcpBehaviourSetting(true, false);
        }

        public KcpServer(IPEndPoint localEndPoint, KcpBehaviourSetting kcpBehaviourSetting)
        {
            _kcpBehaviourSetting = kcpBehaviourSetting;
            LocalEndPoint = localEndPoint;
        }

        /// <summary>
        /// 创建Kcp客户端
        /// </summary>
        /// <param name="conversationId"></param>
        public void CreateClient(T conversationId)
        {
            Kcp kcp = new Kcp(Convert.ToUInt32(conversationId), LocalEndPoint);
            
            kcp.SetKcpBehaviour(_kcpBehaviourSetting);

            clients.TryAdd(conversationId, kcp);
        }
        
        public void CreateClient(T conversationId, IPEndPoint remoteEndPoint)
        {
            Kcp kcp = new Kcp(Convert.ToUInt32(conversationId), LocalEndPoint, remoteEndPoint
                , _kcpBehaviourSetting);

            clients.TryAdd(conversationId, kcp);
        }

        public void CreateClient(T conversationId, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
        {
            Kcp kcp = new Kcp(Convert.ToUInt32(conversationId), localEndPoint, remoteEndPoint
                , _kcpBehaviourSetting);

            clients.TryAdd(conversationId, kcp);
        }

        public void RemoveClient(T conversationId)
        {
            if (clients.TryRemove(conversationId, out var kcp))
            {
                kcp?.Dispose();
            }
            
        }

        public void Send(T conversationId, ReadOnlyMemory<byte> data)
        {
            clients.TryGetValue(conversationId, out var kcp);

            kcp!.Send(data.Span);
        }

        public async Task SendAsync(T conversationId, ReadOnlyMemory<byte> data)
        {
            clients.TryGetValue(conversationId, out var kcp);
            
            await kcp!.SendAsync(data);
        }

        public void SendFast(T conversationId, ReadOnlyMemory<byte> data)
        {
            clients.TryGetValue(conversationId, out var kcp);
            
            kcp!.SendFast(data.Span);
        }

        public async Task SendFastAsync(T conversationId, ReadOnlyMemory<byte> data)
        {
            clients.TryGetValue(conversationId, out var kcp);
            
            await kcp!.SendFastAsync(data);
        } 
        
        public ReadOnlyMemory<byte> GetData(T conversationId)
        { 
            clients.TryGetValue(conversationId, out var kcp);
            
            return kcp!.GetData();
        }
        
        public async Task<ReadOnlyMemory<byte>> GetDataAsync(T conversationId)
        { 
            clients.TryGetValue(conversationId, out var kcp);

            return await kcp!.GetDataAsync();
        }

        public Kcp GetClient(T conversationId) => clients[conversationId];

        public int GetDataCount(T conversationId) => clients[conversationId].GetDataCount();
        
        
        public void Dispose()
        {
            foreach (var client in clients)
            {
                client.Value?.Dispose();
            }
            
            clients.Clear();
        }
    }
}