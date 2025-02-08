using System;
using System.Text;
using Kcp.Interface;

namespace Kcp.Encrypt
{
    public sealed class BitEncryptor : IKcpEncryptor
    {
        private readonly byte[] keyChars = Encoding.UTF8.GetBytes("TheLight233");
        
        public Memory<byte> Encrypt(ReadOnlyMemory<byte> data)
        {
            // 创建一个与输入数据相同大小的内存块
            Memory<byte> encryptedData = new byte[data.Length];

            // 遍历数据并进行 XOR 加密
            for (int i = 0; i < data.Length; i++)
            {
                byte dataByte = data.Span[i];
                byte keyByte = keyChars[i % keyChars.Length]; // 循环使用密钥
                byte newByte = (byte)(dataByte ^ keyByte); // XOR 操作
                encryptedData.Span[i] = newByte;
            }

            return encryptedData;
        }

        public ReadOnlyMemory<byte> Decrypt(ReadOnlyMemory<byte> data)
        {
            // XOR 加密和解密是相同的操作，因此可以直接调用 Encrypt 方法
            return Encrypt(data);
        }
    }
}