using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace UnityFramework.Video
{
    /// <summary>.kcsv の暗号鍵セット。マスターシークレットから <see cref="KcsvFormat.DeriveKeys"/> で導出する。</summary>
    public readonly struct KcsvKeys
    {
        /// <summary>AES-256-CTR 用 32 byte (ネイティブ側へ渡す)。</summary>
        public readonly byte[] AesKey;

        /// <summary>HMAC-SHA256 用 32 byte (改ざん検知。C# 側でのみ使う)。</summary>
        public readonly byte[] MacKey;

        public KcsvKeys(byte[] aesKey, byte[] macKey)
        {
            AesKey = aesKey;
            MacKey = macKey;
        }
    }

    /// <summary>.kcsv の解析に失敗したとき (マジック不一致 / 改ざん / 長さ不正)。</summary>
    public sealed class KcsvFormatException : Exception
    {
        public KcsvFormatException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// .kcsv コンテナ (AES-256-CTR で暗号化した IVF/VP8) の読み書き。
    /// レイアウトはネイティブ側 (Native~/KcsVideo/src/kcsv.h) と同一:
    /// <code>
    /// 0  magic "KCSV" | 4 version u16 | 6 flags u16 | 8 nonce[16] | 24 hmac[32] | 56 payloadLength u64 | 64 暗号文
    /// </code>
    /// CTR のカウンタは nonce を 128bit ビッグエンディアン整数として 1 ブロック (16 byte) ごとに +1。
    /// HMAC はヘッダ先頭 24 byte + 暗号文に対して計算する。
    /// </summary>
    public static class KcsvFormat
    {
        public const int HEADER_SIZE = 64;
        public const int KEY_SIZE = 32;
        public const int NONCE_SIZE = 16;
        public const int HMAC_SIZE = 32;
        public const ushort VERSION = 1;
        public const ushort FLAG_HAS_HMAC = 0x0001;
        public const string FILE_EXTENSION = ".kcsv";

        private const int AES_BLOCK_SIZE = 16;
        private const int HMAC_OFFSET = 24;
        private const int PAYLOAD_LENGTH_OFFSET = 56;
        private const int HMAC_COVERED_HEADER_SIZE = 24;
        private const int STREAM_BUFFER_SIZE = 64 * 1024;

        private static readonly byte[] MAGIC = { (byte)'K', (byte)'C', (byte)'S', (byte)'V' };
        private static readonly byte[] AES_KEY_LABEL = Encoding.ASCII.GetBytes("kcsv-aes");
        private static readonly byte[] MAC_KEY_LABEL = Encoding.ASCII.GetBytes("kcsv-mac");

        /// <summary>ヘッダの平文部分。</summary>
        public readonly struct Header
        {
            public readonly ushort Version;
            public readonly ushort Flags;
            public readonly byte[] Nonce;
            public readonly byte[] Hmac;
            public readonly long PayloadLength;

            public bool HasHmac => (Flags & FLAG_HAS_HMAC) != 0;

            public Header(ushort version, ushort flags, byte[] nonce, byte[] hmac, long payloadLength)
            {
                Version = version;
                Flags = flags;
                Nonce = nonce;
                Hmac = hmac;
                PayloadLength = payloadLength;
            }
        }

        /// <summary>マスターシークレット (任意長) から AES 鍵と HMAC 鍵を導出する (HMAC-SHA256 ベースの KDF)。</summary>
        public static KcsvKeys DeriveKeys(byte[] masterSecret)
        {
            if (masterSecret == null || masterSecret.Length == 0)
            {
                throw new ArgumentException("masterSecret が空です", nameof(masterSecret));
            }
            using (HMACSHA256 hmac = new HMACSHA256(masterSecret))
            {
                byte[] aesKey = hmac.ComputeHash(AES_KEY_LABEL);
                byte[] macKey = hmac.ComputeHash(MAC_KEY_LABEL);
                return new KcsvKeys(aesKey, macKey);
            }
        }

        /// <summary>IVF バイト列を暗号化して .kcsv バイト列にする。nonce を省略するとランダム生成。</summary>
        public static byte[] Encrypt(byte[] ivf, KcsvKeys keys, byte[] nonce = null)
        {
            if (ivf == null)
            {
                throw new ArgumentNullException(nameof(ivf));
            }
            ValidateKeys(keys);
            if (nonce == null)
            {
                nonce = new byte[NONCE_SIZE];
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(nonce);
                }
            }
            if (nonce.Length != NONCE_SIZE)
            {
                throw new ArgumentException("nonce は 16 byte", nameof(nonce));
            }

            byte[] output = new byte[HEADER_SIZE + ivf.Length];
            Buffer.BlockCopy(MAGIC, 0, output, 0, MAGIC.Length);
            WriteUInt16(output, 4, VERSION);
            WriteUInt16(output, 6, FLAG_HAS_HMAC);
            Buffer.BlockCopy(nonce, 0, output, 8, NONCE_SIZE);
            WriteUInt64(output, PAYLOAD_LENGTH_OFFSET, (ulong)ivf.Length);

            Buffer.BlockCopy(ivf, 0, output, HEADER_SIZE, ivf.Length);
            CtrTransform(keys.AesKey, nonce, 0, output, HEADER_SIZE, ivf.Length);

            byte[] mac = ComputeHmac(keys.MacKey, output, HEADER_SIZE, ivf.Length);
            Buffer.BlockCopy(mac, 0, output, HMAC_OFFSET, HMAC_SIZE);
            return output;
        }

        /// <summary>.kcsv バイト列を復号して IVF バイト列を返す。HMAC 不一致は <see cref="KcsvFormatException"/>。</summary>
        public static byte[] Decrypt(byte[] kcsv, KcsvKeys keys, bool verifyHmac = true)
        {
            if (kcsv == null)
            {
                throw new ArgumentNullException(nameof(kcsv));
            }
            ValidateKeys(keys);
            Header header = ParseHeader(kcsv);
            if (kcsv.Length < HEADER_SIZE + header.PayloadLength)
            {
                throw new KcsvFormatException("ファイルが payloadLength より短い (破損)");
            }
            int payloadLength = (int)header.PayloadLength;
            if (verifyHmac && header.HasHmac)
            {
                byte[] expected = ComputeHmac(keys.MacKey, kcsv, HEADER_SIZE, payloadLength);
                if (!FixedTimeEquals(expected, header.Hmac))
                {
                    throw new KcsvFormatException("HMAC が一致しません (鍵違いか改ざん)");
                }
            }
            byte[] ivf = new byte[payloadLength];
            Buffer.BlockCopy(kcsv, HEADER_SIZE, ivf, 0, payloadLength);
            CtrTransform(keys.AesKey, header.Nonce, 0, ivf, 0, payloadLength);
            return ivf;
        }

        /// <summary>ヘッダを解析する (暗号文は読まない)。</summary>
        public static Header ParseHeader(byte[] data)
        {
            if (data == null || data.Length < HEADER_SIZE)
            {
                throw new KcsvFormatException("ヘッダが短い");
            }
            for (int i = 0; i < MAGIC.Length; i++)
            {
                if (data[i] != MAGIC[i])
                {
                    throw new KcsvFormatException("マジックが KCSV ではない");
                }
            }
            ushort version = ReadUInt16(data, 4);
            if (version != VERSION)
            {
                throw new KcsvFormatException($"未対応バージョン {version}");
            }
            ushort flags = ReadUInt16(data, 6);
            byte[] nonce = new byte[NONCE_SIZE];
            Buffer.BlockCopy(data, 8, nonce, 0, NONCE_SIZE);
            byte[] hmac = new byte[HMAC_SIZE];
            Buffer.BlockCopy(data, HMAC_OFFSET, hmac, 0, HMAC_SIZE);
            ulong payloadLength = ReadUInt64(data, PAYLOAD_LENGTH_OFFSET);
            if (payloadLength > int.MaxValue)
            {
                throw new KcsvFormatException("payloadLength が大きすぎる");
            }
            return new Header(version, flags, nonce, hmac, (long)payloadLength);
        }

        /// <summary>ファイルをストリーム読みして HMAC を検証する (全体をメモリに載せない)。HMAC 無しのファイルは true。</summary>
        public static bool VerifyFile(string path, KcsvKeys keys, out string error)
        {
            error = null;
            ValidateKeys(keys);
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, STREAM_BUFFER_SIZE))
                {
                    byte[] headerBytes = new byte[HEADER_SIZE];
                    if (ReadFully(stream, headerBytes, HEADER_SIZE) != HEADER_SIZE)
                    {
                        error = "ヘッダが短い";
                        return false;
                    }
                    Header header = ParseHeader(headerBytes);
                    if (stream.Length < HEADER_SIZE + header.PayloadLength)
                    {
                        error = "ファイルが payloadLength より短い";
                        return false;
                    }
                    if (!header.HasHmac)
                    {
                        return true;
                    }
                    using (HMACSHA256 hmac = new HMACSHA256(keys.MacKey))
                    {
                        hmac.TransformBlock(headerBytes, 0, HMAC_COVERED_HEADER_SIZE, null, 0);
                        byte[] buffer = new byte[STREAM_BUFFER_SIZE];
                        long remaining = header.PayloadLength;
                        while (remaining > 0)
                        {
                            int want = (int)Math.Min(remaining, buffer.Length);
                            int read = stream.Read(buffer, 0, want);
                            if (read <= 0)
                            {
                                error = "読み込みが途中で終了";
                                return false;
                            }
                            hmac.TransformBlock(buffer, 0, read, null, 0);
                            remaining -= read;
                        }
                        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        if (!FixedTimeEquals(hmac.Hash, header.Hmac))
                        {
                            error = "HMAC が一致しません (鍵違いか改ざん)";
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (KcsvFormatException e)
            {
                error = e.Message;
                return false;
            }
            catch (IOException e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>IVF ファイルを暗号化して書き出す (エディタツール用)。</summary>
        public static void EncryptFile(string ivfPath, string outputPath, KcsvKeys keys)
        {
            byte[] ivf = File.ReadAllBytes(ivfPath);
            byte[] kcsv = Encrypt(ivf, keys);
            File.WriteAllBytes(outputPath, kcsv);
        }

        /// <summary>16進文字列 (64 文字) → 32 byte 鍵。開発用の鍵指定に使う。</summary>
        public static byte[] ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
            {
                throw new ArgumentException("16進文字列の長さが不正", nameof(hex));
            }
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        // ---- AES-256-CTR ----

        /// <summary>
        /// data[offset..offset+count) を CTR で暗号化/復号する (同じ操作)。
        /// startBlock はペイロード先頭からのブロック番号 (途中から処理する場合)。
        /// </summary>
        public static void CtrTransform(byte[] key, byte[] nonce, long startBlock, byte[] data, int offset, int count)
        {
            byte[] counter = new byte[AES_BLOCK_SIZE];
            Buffer.BlockCopy(nonce, 0, counter, 0, AES_BLOCK_SIZE);
            AddToCounter(counter, (ulong)startBlock);

            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    byte[] keystream = new byte[AES_BLOCK_SIZE];
                    int position = 0;
                    while (position < count)
                    {
                        encryptor.TransformBlock(counter, 0, AES_BLOCK_SIZE, keystream, 0);
                        int chunk = Math.Min(AES_BLOCK_SIZE, count - position);
                        for (int i = 0; i < chunk; i++)
                        {
                            data[offset + position + i] ^= keystream[i];
                        }
                        position += chunk;
                        AddToCounter(counter, 1);
                    }
                }
            }
        }

        /// <summary>128bit ビッグエンディアン加算 (ネイティブ側 counterAdd と同じ)。</summary>
        private static void AddToCounter(byte[] counter, ulong blocks)
        {
            ulong carry = blocks;
            for (int i = AES_BLOCK_SIZE - 1; i >= 0 && carry != 0; i--)
            {
                ulong sum = counter[i] + (carry & 0xFF);
                counter[i] = (byte)(sum & 0xFF);
                carry = (carry >> 8) + (sum >> 8);
            }
        }

        private static byte[] ComputeHmac(byte[] macKey, byte[] kcsv, int payloadOffset, int payloadLength)
        {
            using (HMACSHA256 hmac = new HMACSHA256(macKey))
            {
                hmac.TransformBlock(kcsv, 0, HMAC_COVERED_HEADER_SIZE, null, 0);
                hmac.TransformBlock(kcsv, payloadOffset, payloadLength, null, 0);
                hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return hmac.Hash;
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }

        private static void ValidateKeys(KcsvKeys keys)
        {
            if (keys.AesKey == null || keys.AesKey.Length != KEY_SIZE)
            {
                throw new ArgumentException("AesKey は 32 byte");
            }
            if (keys.MacKey == null || keys.MacKey.Length != KEY_SIZE)
            {
                throw new ArgumentException("MacKey は 32 byte");
            }
        }

        private static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read <= 0)
                {
                    break;
                }
                total += read;
            }
            return total;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static ulong ReadUInt64(byte[] data, int offset)
        {
            ulong value = 0;
            for (int i = 7; i >= 0; i--)
            {
                value = (value << 8) | data[offset + i];
            }
            return value;
        }

        private static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteUInt64(byte[] data, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                data[offset + i] = (byte)((value >> (8 * i)) & 0xFF);
            }
        }
    }
}
