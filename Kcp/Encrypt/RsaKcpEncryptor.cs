using System;
using System.Security.Cryptography;
using Kcp.Interface;

namespace Kcp.Encrypt
{
    public sealed class RsaKcpEncryptor : IKcpEncryptor
    {
        private readonly RSAParameters _publicKey;
        private readonly RSAParameters _privateKey;

        public RsaKcpEncryptor(RSAParameters publicKey, RSAParameters privateKey)
        {
            _publicKey = publicKey;
            _privateKey = privateKey;
        }

        public Memory<byte> Encrypt(ReadOnlyMemory<byte> data)
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(_publicKey);

                // 检查数据长度是否符合要求
                int maxLength = rsa.KeySize / 8 - 11; // RSA 最大加密数据长度（PKCS#1 v1.5）
                if (data.Length > maxLength)
                    throw new ArgumentException($"Data is too long. Maximum length for RSA encryption is {maxLength} bytes.");

                byte[] encryptedData = rsa.Encrypt(data.ToArray(), RSAEncryptionPadding.Pkcs1);
                return new Memory<byte>(encryptedData);
            }
        }

        public ReadOnlyMemory<byte> Decrypt(ReadOnlyMemory<byte> data)
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(_privateKey);

                // 检查数据长度是否符合要求
                int keySize = rsa.KeySize / 8;
                if (data.Length != keySize)
                    throw new ArgumentException($"Data length must be {keySize} bytes.");

                byte[] decryptedData = rsa.Decrypt(data.ToArray(), RSAEncryptionPadding.Pkcs1);
                return new ReadOnlyMemory<byte>(decryptedData);
            }
        }
    }
}