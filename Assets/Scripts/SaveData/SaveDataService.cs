using System;
using System.IO;
using UnityEngine;
using UnityFramework;

namespace KTC.SaveData
{
    /// <summary>
    /// ローカルセーブの読み書きサービス (暗号化バイナリ、アトミック書き込み)。
    ///
    /// - ゲームコードは PlayerPrefs やファイルを直接触らず、必ずこのサービスを経由する
    /// - 破損・改ざん・鍵不一致はすべて初期データへフォールバックし、
    ///   調査用に破損ファイルを ".corrupt" として退避する
    /// - 将来のサーバー同期はこのサービスの差し替えで行う
    /// </summary>
    public sealed class SaveDataService
    {
        public const string DefaultFileName = "save.bin";

        private readonly string _filePath;
        private readonly SaveKeys _keys;

        private string TempPath => _filePath + ".tmp";
        private string CorruptPath => _filePath + ".corrupt";

        public SaveDataService(string filePath, SaveKeys keys)
        {
            _filePath = !string.IsNullOrEmpty(filePath)
                ? filePath
                : throw new ArgumentException("filePath が空です。", nameof(filePath));
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        }

        /// <summary>既定の保存先 (persistentDataPath) と端末導出鍵でサービスを作る。</summary>
        public static SaveDataService CreateDefault()
        {
            return new SaveDataService(
                Path.Combine(Application.persistentDataPath, DefaultFileName),
                SaveKeys.DeriveForDevice());
        }

        public bool HasSave => File.Exists(_filePath);

        /// <summary>
        /// セーブデータをロードする。ファイルなし・破損・改ざんは初期データを返す (例外を投げない)。
        /// </summary>
        public PlayerData Load()
        {
            if (!File.Exists(_filePath))
            {
                return PlayerData.CreateDefault();
            }

            byte[] file;
            try
            {
                file = File.ReadAllBytes(_filePath);
            }
            catch (Exception e)
            {
                SafeLogger.LogWarning($"[SaveDataService] セーブ読み込みに失敗: {e.Message}。初期データで開始します。");
                return PlayerData.CreateDefault();
            }

            if (!SaveCrypto.TryDecrypt(file, _keys, out var payload, out var reason))
            {
                SafeLogger.LogWarning($"[SaveDataService] セーブが不正 ({reason})。破損ファイルを退避し初期データで開始します。");
                BackupCorruptFile();
                return PlayerData.CreateDefault();
            }

            try
            {
                return PlayerDataSerializer.Deserialize(payload);
            }
            catch (Exception e)
            {
                SafeLogger.LogWarning($"[SaveDataService] セーブ解析に失敗 ({e.Message})。破損ファイルを退避し初期データで開始します。");
                BackupCorruptFile();
                return PlayerData.CreateDefault();
            }
        }

        /// <summary>
        /// セーブする。一時ファイルへ書き切ってから置換するため、
        /// 書き込み途中のクラッシュで既存セーブが壊れることはない。
        /// </summary>
        public void Save(PlayerData data)
        {
            byte[] file = SaveCrypto.Encrypt(PlayerDataSerializer.Serialize(data), _keys);

            string directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(TempPath, file);
            if (File.Exists(_filePath))
            {
                File.Replace(TempPath, _filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(TempPath, _filePath);
            }
        }

        /// <summary>セーブデータを完全に削除する (デバッグメニュー用)。</summary>
        public void Delete()
        {
            DeleteIfExists(_filePath);
            DeleteIfExists(TempPath);
            DeleteIfExists(CorruptPath);
        }

        private void BackupCorruptFile()
        {
            try
            {
                File.Copy(_filePath, CorruptPath, overwrite: true);
            }
            catch (Exception)
            {
                // 退避は best-effort (本処理のフォールバックを妨げない)
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
