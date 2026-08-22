namespace KTC.SaveData
{
    /// <summary>
    /// プレイヤーのローカルセーブデータ。
    /// フィールドを追加したら PlayerDataSerializer の CURRENT_DATA_VERSION を上げ、
    /// 旧バージョンからのマイグレーション処理を追加すること。
    /// </summary>
    public sealed class PlayerData
    {
        public string PlayerName = "";
        public long Chips = 0;
        public int Level = 1;

        /// <summary>チュートリアル進行フラグ (ビットフィールド)。</summary>
        public uint TutorialFlags = 0;

        /// <summary>利用規約に同意済みか (初回フロー判定)。</summary>
        public bool IsTermsAccepted = false;

        // ---- 設定 ----
        public float MasterVolume = 1f;
        public float BgmVolume = 1f;
        public float SeVolume = 1f;
        public bool EffectsEnabled = true;

        // ---- 成長・戦績 (v2)。サーバー移行後は DB (CardGame-Server/Docs/DB.md) が正となる ----
        public long Xp = 0;
        public int MatchesPlayed = 0;
        public int HandsPlayed = 0;
        public int HandsWon = 0;

        /// <summary>累計XPからレベルを算出する (100XPごとに1レベル、Lv.1開始)。</summary>
        public static int LevelForXp(long xp) => 1 + (int)(xp / 100);

        /// <summary>XPを加算し、レベルを追従させる。レベルが上がったら true。</summary>
        public bool AddXp(long amount)
        {
            int before = Level;
            Xp += amount;
            Level = LevelForXp(Xp);
            return Level > before;
        }

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
