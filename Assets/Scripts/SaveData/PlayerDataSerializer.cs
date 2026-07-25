using System;
using System.IO;
using System.Text;

namespace KTC.SaveData
{
    /// <summary>セーブデータのバイナリシリアライズ (暗号化前の平文ペイロード)。</summary>
    public static class PlayerDataSerializer
    {
        /// <summary>
        /// ペイロードのデータバージョン。フィールドを追加・変更したら +1 し、
        /// Deserialize の switch に旧バージョンの読み取り (マイグレーション) を追加する。
        /// </summary>
        public const int CurrentDataVersion = 1;

        public static byte[] Serialize(PlayerData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(CurrentDataVersion);
                writer.Write(data.PlayerName ?? "");
                writer.Write(data.Chips);
                writer.Write(data.Level);
                writer.Write(data.TutorialFlags);
                writer.Write(data.IsTermsAccepted);
                writer.Write(data.MasterVolume);
                writer.Write(data.BgmVolume);
                writer.Write(data.SeVolume);
                writer.Write(data.EffectsEnabled);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>
        /// ペイロードを復元する。未対応バージョンや読み取り失敗は例外
        /// (呼び出し側 = SaveDataService がフォールバックを担当)。
        /// </summary>
        public static PlayerData Deserialize(byte[] payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            using (var stream = new MemoryStream(payload))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            {
                int version = reader.ReadInt32();
                switch (version)
                {
                    case 1:
                        return ReadVersion1(reader);
                    // 例: case 2 を追加したら、version1 は読み取り後に新フィールドへ
                    //     デフォルト値を補完する形でマイグレーションする
                    default:
                        throw new InvalidDataException($"未対応のセーブデータバージョンです: {version}");
                }
            }
        }

        private static PlayerData ReadVersion1(BinaryReader reader)
        {
            return new PlayerData
            {
                PlayerName = reader.ReadString(),
                Chips = reader.ReadInt64(),
                Level = reader.ReadInt32(),
                TutorialFlags = reader.ReadUInt32(),
                IsTermsAccepted = reader.ReadBoolean(),
                MasterVolume = reader.ReadSingle(),
                BgmVolume = reader.ReadSingle(),
                SeVolume = reader.ReadSingle(),
                EffectsEnabled = reader.ReadBoolean(),
            };
        }
    }
}
