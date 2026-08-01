using System;
using System.IO;

namespace UnityFramework.SaveData
{
    /// <summary>
    /// 暗号化バイナリファイルの読み書きストア (バイト列 in/out)。
    ///
    /// - 書き込みは一時ファイルへ書き切ってから置換するアトミック方式
    ///   (書き込み途中のクラッシュで既存ファイルが壊れない)
    /// - 破損・改ざん・鍵不一致は false を返し、調査用に ".corrupt" として退避する
    /// - ペイロードのシリアライズ形式 (バージョニング等) は呼び出し側の責務
    /// </summary>
    public sealed class EncryptedFileStore
    {
        private readonly string _filePath;
        private readonly SaveKeys _keys;

        private string TempPath => _filePath + ".tmp";
        private string CorruptPath => _filePath + ".corrupt";

        public EncryptedFileStore(string filePath, SaveKeys keys)
        {
            _filePath = !string.IsNullOrEmpty(filePath)
                ? filePath
                : throw new ArgumentException("filePath が空です。", nameof(filePath));
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        }

        public bool HasFile => File.Exists(_filePath);

        /// <summary>
        /// 復号済みペイロードの読み込みを試みる。
        /// ファイルなしは false (reason=null)。破損・改ざんは false + 退避 + reason 格納。
        /// </summary>
        public bool TryLoad(out byte[] payload, out string reason)
        {
            payload = null;
            reason = null;

            if (!File.Exists(_filePath))
            {
                return false;
            }

            byte[] file;
            try
            {
                file = File.ReadAllBytes(_filePath);
            }
            catch (Exception e)
            {
                reason = $"読み込み失敗: {e.Message}";
                return false;
            }

            if (!SaveCrypto.TryDecrypt(file, _keys, out payload, out reason))
            {
                BackupCorruptFile();
                return false;
            }
            return true;
        }

        /// <summary>ペイロードを暗号化してアトミックに保存する。</summary>
        public void Save(byte[] payload)
        {
            byte[] file = SaveCrypto.Encrypt(payload, _keys);

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

        /// <summary>ファイルを完全に削除する (一時・退避ファイル含む)。</summary>
        public void Delete()
        {
            DeleteIfExists(_filePath);
            DeleteIfExists(TempPath);
            DeleteIfExists(CorruptPath);
        }

        /// <summary>破損ファイルを ".corrupt" として退避する (best-effort)。</summary>
        public void BackupCorruptFile()
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
