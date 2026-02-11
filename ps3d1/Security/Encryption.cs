using System;
using System.Collections.Generic;
using System.Text;

namespace ps3d1.Security
{
    /// <summary>
    /// Encryption utilities - C# port of Loader_V1 encryption
    /// </summary>
    public static class Encryption
    {
        private const ulong FNV_PRIME = 0x00000100000001B3;
        private const ulong FNV_OFFSET = 0xcbf29ce484222325;

        public static byte[] GenerateKeySchedule(string key, int length)
        {
            byte[] schedule = new byte[length];
            int keyLen = key.Length;
            if (keyLen == 0) return schedule;

            for (int i = 0; i < length; i++)
            {
                schedule[i] = (byte)((key[i % keyLen] ^ (i & 0xFF)) + ((i >> 8) & 0xFF));
            }
            return schedule;
        }

        public static byte[] EncryptData(byte[] data, string key)
        {
            if (data == null || data.Length == 0 || string.IsNullOrEmpty(key))
                return data;

            byte[] keySchedule = GenerateKeySchedule(key, data.Length);
            byte[] encrypted = new byte[data.Length];

            for (int i = 0; i < data.Length; i++)
            {
                encrypted[i] = (byte)(data[i] ^ keySchedule[i]);
            }
            return encrypted;
        }

        public static byte[] DecryptData(byte[] data, string key)
        {
            // XOR encryption is symmetric
            return EncryptData(data, key);
        }

        public static string EncryptString(string plaintext, string key)
        {
            byte[] data = Encoding.UTF8.GetBytes(plaintext);
            byte[] encrypted = EncryptData(data, key);

            StringBuilder sb = new StringBuilder();
            foreach (byte b in encrypted)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        public static string DecryptString(string ciphertext, string key)
        {
            if (ciphertext.Length % 2 != 0)
                return "";

            List<byte> data = new List<byte>();
            for (int i = 0; i < ciphertext.Length; i += 2)
            {
                string byteString = ciphertext.Substring(i, 2);
                data.Add(Convert.ToByte(byteString, 16));
            }

            byte[] decrypted = DecryptData(data.ToArray(), key);
            return Encoding.UTF8.GetString(decrypted);
        }

        public static ulong GenerateNumericHash(string input)
        {
            ulong hash = FNV_OFFSET;
            foreach (char c in input)
            {
                hash ^= (ulong)c;
                hash *= FNV_PRIME;
            }
            return hash;
        }

        public static string GenerateHash(string input)
        {
            ulong hash = GenerateNumericHash(input);
            return hash.ToString("x16");
        }

        public static string HashPassword(string password, string salt)
        {
            string combined = salt + password + salt;
            ulong hash1 = GenerateNumericHash(combined);

            string intermediate = hash1.ToString() + combined;
            ulong hash2 = GenerateNumericHash(intermediate);

            return hash2.ToString("x16");
        }

        public static bool VerifyPassword(string password, string salt, string hash)
        {
            string computed = HashPassword(password, salt);
            return computed == hash;
        }
    }
}
