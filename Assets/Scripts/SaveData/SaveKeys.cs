using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace KTC.SaveData
{
    /// <summary>
    /// セーブデータ暗号化に使う鍵ペア (AES用 / HMAC用)。
    ///
    /// 脅威モデルの注記: 鍵素材はアプリ内 + 端末情報から導出されるため、
    /// 本気の解析者には突破される。目的は「バイナリエディタでの書き換え・
    /// 端末間のセーブコピーの抑止」であり、本当の資産保護はサーバー残高で行う。
    /// </summary>
    public sealed class SaveKeys
    {
        public byte[] AesKey { get; }
        public byte[] HmacKey { get; }

        // アプリ埋め込みシークレット。ローテーションすると既存セーブは全て読めなくなる点に注意
        private const string AppSecret = "KTC-CardGame-2026-7f3a9c1e";

        public SaveKeys(byte[] aesKey, byte[] hmacKey)
        {
            AesKey = aesKey ?? throw new ArgumentNullException(nameof(aesKey));
            HmacKey = hmacKey ?? throw new ArgumentNullException(nameof(hmacKey));
            if (aesKey.Length != 32 || hmacKey.Length != 32)
            {
                throw new ArgumentException("鍵は 32 byte (SHA-256 導出) を想定しています。");
            }
        }

        /// <summary>この端末用の鍵を導出する (端末間でセーブをコピーしても復号できない)。</summary>
        public static SaveKeys DeriveForDevice()
        {
            return Derive(SystemInfo.deviceUniqueIdentifier);
        }

        /// <summary>シード文字列から鍵ペアを導出する (テストでは任意のシードを注入)。</summary>
        public static SaveKeys Derive(string deviceSeed)
        {
            return new SaveKeys(
                Sha256($"aes|{AppSecret}|{deviceSeed}"),
                Sha256($"mac|{AppSecret}|{deviceSeed}"));
        }

        private static byte[] Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            }
        }
    }
}
