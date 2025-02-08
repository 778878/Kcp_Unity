using System;
using System.Net;

namespace Kcp.Event
{
    public sealed class ConnectionEventArgs : EventArgs
    {
        public IPEndPoint RemoteEndPoint { get; set; }
        public string Message { get; set; }

        public ConnectionEventArgs(IPEndPoint remoteEndPoint, string message)
        {
            RemoteEndPoint = remoteEndPoint;
            Message = message;
        }
        
        
    }

    public sealed class DataEventArgs : EventArgs
    {
        public ReadOnlyMemory<byte> Data { get; set; }

        public DataEventArgs(ReadOnlyMemory<byte> data)
        {
            Data = data;
        }
    }
    
    public delegate void ConnectionEventHandler(object sender, ConnectionEventArgs e);
    
    public delegate void DataEventHandler(object sender, DataEventArgs e);
}