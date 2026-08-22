using System;
using System.IO;
using UnityEngine;
using UnityFramework;
using UnityFramework.SaveData;

namespace KTC.SaveData
{
    /// <summary>
    /// ローカルセーブの読み書きサービス (暗号化バイナリ、アトミック書き込み)。
    /// 暗号化・アトミック書き込み・破損退避の実体は UnityFramework.SaveData.EncryptedFileStore。
    /// このクラスは PlayerData のシリアライズと初期データフォールバックを担当する。
    ///
    /// - ゲームコードは PlayerPrefs やファイルを直接触らず、必ずこのサービスを経由する
    /// - 破損・改ざん・鍵不一致はすべて初期データへフォールバックする
    /// - 将来のサーバー同期はこのサービスの差し替えで行う
    /// </summary>
    public sealed class SaveDataService
    {
        public const string DEFAULT_FILE_NAME = "save.bin";

        // アプリ埋め込みシークレット。ローテーションすると既存セーブは全て読めなくなる点に注意
        private const string APP_SECRET = "KTC-CardGame-2026-7f3a9c1e";

        private readonly EncryptedFileStore _store;

        public SaveDataService(string filePath, SaveKeys keys)
        {
            _store = new EncryptedFileStore(filePath, keys);
        }

        /// <summary>既定の保存先 (persistentDataPath) と端末導出鍵でサービスを作る。</summary>
        public static SaveDataService CreateDefault()
        {
            return new SaveDataService(
                Path.Combine(Application.persistentDataPath, DEFAULT_FILE_NAME),
                SaveKeys.DeriveForDevice(APP_SECRET));
        }

        public bool HasSave => _store.HasFile;

        /// <summary>
        /// セーブデータをロードする。ファイルなし・破損・改ざんは初期データを返す (例外を投げない)。
        /// </summary>
        public PlayerData Load()
        {
            if (!_store.TryLoad(out var payload, out var reason))
            {
                if (reason != null)
                {
                    SafeLogger.LogWarning($"[SaveDataService] セーブが読めません ({reason})。初期データで開始します。");
                }
                return PlayerData.CreateDefault();
            }

            try
            {
                return PlayerDataSerializer.Deserialize(payload);
            }
            catch (Exception e)
            {
                SafeLogger.LogWarning($"[SaveDataService] セーブ解析に失敗 ({e.Message})。破損ファイルを退避し初期データで開始します。");
                _store.BackupCorruptFile();
                return PlayerData.CreateDefault();
            }
        }

        /// <summary>セーブする (暗号化 + アトミック書き込み)。</summary>
        public void Save(PlayerData data)
        {
            _store.Save(PlayerDataSerializer.Serialize(data));
        }

        /// <summary>セーブデータを完全に削除する (デバッグメニュー用)。</summary>
        public void Delete()
        {
            _store.Delete();
        }
    }
}
