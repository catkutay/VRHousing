
using System;
using System.IO;
using System.Security.Cryptography;

namespace Arkio
{
    public static class CryptographyUtils
    {

        public static byte[] PerformCryptography(byte[] data, ICryptoTransform cryptoTransform)
        {
            using (var ms = new MemoryStream())
            using (var cryptoStream = new CryptoStream(ms, cryptoTransform, CryptoStreamMode.Write))
            {
                cryptoStream.Write(data, 0, data.Length);
                cryptoStream.FlushFinalBlock();

                return ms.ToArray();
            }
        }

        static RNGCryptoServiceProvider provider = new RNGCryptoServiceProvider();

        public static ulong GenerateRandomUInt64()
        {
            var randomValue = new byte[8];
            provider.GetBytes(randomValue);
            return BitConverter.ToUInt64(randomValue, 0);
        }
    }
}