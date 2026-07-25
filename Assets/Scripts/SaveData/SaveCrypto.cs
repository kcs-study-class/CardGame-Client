using System;
using System.IO;
using System.Security.Cryptography;

namespace KTC.SaveData
{
    /// <summary>
    /// セーブファイルの暗号化・復号 (encrypt-then-MAC)。
    ///
    /// ファイルフォーマット:
    /// <code>
    /// [magic "KTCS" 4B][フォーマットver 1B][IV 16B][AES-256-CBC 暗号文][HMAC-SHA256 32B]
    /// </code>
    /// HMAC は末尾32Bを除く全体 (magic/ver/IV/暗号文) に対して計算する。
    /// ヘッダの書き換えも改ざんとして検知される。
    /// </summary>
    public static class SaveCrypto
    {
        private static readonly byte[] Magic = { 0x4B, 0x54, 0x43, 0x53 }; // "KTCS"
        public const byte FormatVersion = 1;

        private const int IvSize = 16;
        private const int HmacSize = 32;
        private const int MinFileSize = 4 + 1 + IvSize + 16 + HmacSize; // 暗号文は最低1ブロック

        public static byte[] Encrypt(byte[] payload, SaveKeys keys)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (keys == null) throw new ArgumentNullException(nameof(keys));

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Key = keys.AesKey;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                byte[] cipher;
                using (var encryptor = aes.CreateEncryptor())
                {
                    cipher = encryptor.TransformFinalBlock(payload, 0, payload.Length);
                }

                using (var stream = new MemoryStream())
                {
                    stream.Write(Magic, 0, Magic.Length);
                    stream.WriteByte(FormatVersion);
                    stream.Write(aes.IV, 0, IvSize);
                    stream.Write(cipher, 0, cipher.Length);

                    byte[] mac = ComputeMac(keys, stream.ToArray());
                    stream.Write(mac, 0, mac.Length);
                    return stream.ToArray();
                }
            }
        }

        /// <summary>
        /// 復号を試みる。フォーマット不正・改ざん・鍵不一致はすべて false
        /// (詳細理由は reason に格納。ログ用)。
        /// </summary>
        public static bool TryDecrypt(byte[] file, SaveKeys keys, out byte[] payload, out string reason)
        {
            payload = null;
            reason = null;

            if (file == null || file.Length < MinFileSize)
            {
                reason = "ファイルサイズが不正";
                return false;
            }
            for (int i = 0; i < Magic.Length; i++)
            {
                if (file[i] != Magic[i])
                {
                    reason = "マジックナンバー不一致";
                    return false;
                }
            }
            if (file[4] != FormatVersion)
            {
                reason = $"未対応のフォーマットバージョン: {file[4]}";
                return false;
            }

            // HMAC 検証 (改ざん検知)。比較はタイミング攻撃対策で定数時間
            int macOffset = file.Length - HmacSize;
            byte[] expectedMac = ComputeMac(keys, file, macOffset);
            if (!FixedTimeEquals(file, macOffset, expectedMac))
            {
                reason = "HMAC 検証失敗 (改ざんまたは鍵不一致)";
                return false;
            }

            try
            {
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Key = keys.AesKey;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    var iv = new byte[IvSize];
                    Buffer.BlockCopy(file, 5, iv, 0, IvSize);
                    aes.IV = iv;

                    int cipherOffset = 5 + IvSize;
                    using (var decryptor = aes.CreateDecryptor())
                    {
                        payload = decryptor.TransformFinalBlock(file, cipherOffset, macOffset - cipherOffset);
                    }
                    return true;
                }
            }
            catch (CryptographicException e)
            {
                reason = $"復号失敗: {e.Message}";
                payload = null;
                return false;
            }
        }

        private static byte[] ComputeMac(SaveKeys keys, byte[] data, int count = -1)
        {
            using (var hmac = new HMACSHA256(keys.HmacKey))
            {
                return hmac.ComputeHash(data, 0, count < 0 ? data.Length : count);
            }
        }

        private static bool FixedTimeEquals(byte[] file, int macOffset, byte[] expected)
        {
            int diff = 0;
            for (int i = 0; i < HmacSize; i++)
            {
                diff |= file[macOffset + i] ^ expected[i];
            }
            return diff == 0;
        }
    }
}
