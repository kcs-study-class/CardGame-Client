using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace UnityFramework.SaveData
{
    /// <summary>
    /// セーブデータ暗号化に使う鍵ペア (AES用 / HMAC用)。
    ///
    /// 脅威モデルの注記: 鍵素材はアプリ内シークレット + 端末情報から導出されるため、
    /// 本気の解析者には突破される。目的は「バイナリエディタでの書き換え・
    /// 端末間のセーブコピーの抑止」であり、本当の資産保護はサーバー側で行うこと。
    /// </summary>
    public sealed class SaveKeys
    {
        public byte[] AesKey { get; }
        public byte[] HmacKey { get; }

        public SaveKeys(byte[] aesKey, byte[] hmacKey)
        {
            AesKey = aesKey ?? throw new ArgumentNullException(nameof(aesKey));
            HmacKey = hmacKey ?? throw new ArgumentNullException(nameof(hmacKey));
            if (aesKey.Length != 32 || hmacKey.Length != 32)
            {
                throw new ArgumentException("鍵は 32 byte (SHA-256 導出) を想定しています。");
            }
        }

        /// <summary>
        /// この端末用の鍵を導出する (端末間でセーブをコピーしても復号できない)。
        /// appSecret はアプリ固有のシークレット文字列。ローテーションすると既存セーブは全て読めなくなる。
        /// </summary>
        public static SaveKeys DeriveForDevice(string appSecret)
        {
            return Derive(appSecret, SystemInfo.deviceUniqueIdentifier);
        }

        /// <summary>シークレットとシード文字列から鍵ペアを導出する (テストでは任意のシードを注入)。</summary>
        public static SaveKeys Derive(string appSecret, string deviceSeed)
        {
            return new SaveKeys(
                Sha256($"aes|{appSecret}|{deviceSeed}"),
                Sha256($"mac|{appSecret}|{deviceSeed}"));
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
