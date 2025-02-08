using System;

namespace Kcp.Interface
{
    public interface IKcpEncryptor
    {
        Memory<byte> Encrypt(ReadOnlyMemory<byte> data);
        ReadOnlyMemory<byte> Decrypt(ReadOnlyMemory<byte> data);
    }
}