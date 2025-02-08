using System;
using System.Security.Cryptography;
using System.Text;
using Kcp.Interface;

namespace Kcp.Encrypt
{
    public sealed class AesKcpEncryptor : IKcpEncryptor
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;

        public AesKcpEncryptor(string key, string iv)
        {
            if (key.Length != 16 && key.Length != 24 && key.Length != 32)
                throw new ArgumentException("Key must be 16, 24, or 32 bytes long.");

            if (iv.Length != 16)
                throw new ArgumentException("IV must be 16 bytes long.");

            _key = Encoding.UTF8.GetBytes(key);
            _iv = Encoding.UTF8.GetBytes(iv);
        }

        public Memory<byte> Encrypt(ReadOnlyMemory<byte> data)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = _key;
                aes.IV = _iv;
                aes.Padding = PaddingMode.PKCS7;  // 使用 PKCS7 填充

                ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                byte[] encryptedData = encryptor.TransformFinalBlock(data.ToArray(), 0, data.Length);
                return new Memory<byte>(encryptedData);
            }
        }

        public ReadOnlyMemory<byte> Decrypt(ReadOnlyMemory<byte> data)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = _key;
                aes.IV = _iv;
                aes.Padding = PaddingMode.PKCS7;  // 使用 PKCS7 填充

                ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                byte[] decryptedData = decryptor.TransformFinalBlock(data.ToArray(), 0, data.Length);
                return new ReadOnlyMemory<byte>(decryptedData);
            }
        }
    }
}