using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LocalServiceStreaming.Models
{
    public static class AesEncryption
    {
        private static readonly string HexKey = "5518c8f250d4cbb939aa431f525beb3d4e850a22890811e9b62a95ff81108c3c"; // 64 hex chars = 32 bytes
        private static readonly string HexIV = "74654107fa179dded32ff50e443e9844";              // 32 hex chars = 16 bytes

        private static readonly byte[] Key = ConvertHexStringToByteArray(HexKey);
        private static readonly byte[] IV = ConvertHexStringToByteArray(HexIV);

        public static string Encrypt(string plainText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Key;
                aes.IV = IV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream();
                using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
                using (var sw = new StreamWriter(cs))
                {
                    sw.Write(plainText);
                }
                return BitConverter.ToString(ms.ToArray()).Replace("-", "").ToLower();
            }
        }

        public static string Decrypt(string cipherHex)
        {
            try
            {
                byte[] cipherBytes = ConvertHexStringToByteArray(cipherHex);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = Key;
                    aes.IV = IV;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                    using var ms = new MemoryStream(cipherBytes);
                    using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                    using var sr = new StreamReader(cs);
                    return sr.ReadToEnd();
                }
            }
            catch
            {
                return "";
            }
        }

        private static byte[] ConvertHexStringToByteArray(string hex)
        {
            int length = hex.Length;
            byte[] buffer = new byte[length / 2];
            for (int i = 0; i < length; i += 2)
                buffer[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return buffer;
        }
    }
}
