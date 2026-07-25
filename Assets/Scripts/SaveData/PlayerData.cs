namespace KTC.SaveData
{
    /// <summary>
    /// プレイヤーのローカルセーブデータ。
    /// フィールドを追加したら PlayerDataSerializer の CurrentDataVersion を上げ、
    /// 旧バージョンからのマイグレーション処理を追加すること。
    /// </summary>
    public sealed class PlayerData
    {
        public string PlayerName = "";
        public long Chips;
        public int Level = 1;

        /// <summary>チュートリアル進行フラグ (ビットフィールド)。</summary>
        public uint TutorialFlags;

        /// <summary>利用規約に同意済みか (初回フロー判定)。</summary>
        public bool IsTermsAccepted;

        // ---- 設定 ----
        public float MasterVolume = 1f;
        public float BgmVolume = 1f;
        public float SeVolume = 1f;
        public bool EffectsEnabled = true;

        /// <summary>初期データ (新規プレイヤー / セーブ破損時のフォールバック)。</summary>
        public static PlayerData CreateDefault()
        {
            return new PlayerData
            {
                PlayerName = "",
                Chips = 1000, // サーバーDBの初期付与額と揃える (CardGame-Server/Docs/DB.md)
                Level = 1,
            };
        }

        public bool HasTutorialFlag(int bit) => (TutorialFlags & (1u << bit)) != 0;

        public void SetTutorialFlag(int bit, bool value)
        {
            if (value)
            {
                TutorialFlags |= 1u << bit;
            }
            else
            {
                TutorialFlags &= ~(1u << bit);
            }
        }
    }
}
