using System.IO;
using System.Text;
using KTC.SaveData;
using NUnit.Framework;

namespace KTC.Poker.Tests
{
    /// <summary>セーブデータ v2 (成長・戦績) のマイグレーションとレベル計算。</summary>
    public class PlayerDataV2Tests
    {
        /// <summary>v1 レイアウトのペイロードを手組みする (旧バージョンのセーブを再現)。</summary>
        private static byte[] BuildVersion1Payload()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1);            // dataVersion
                writer.Write("旧プレイヤー");
                writer.Write(5000L);        // Chips
                writer.Write(3);            // Level
                writer.Write(0b101u);       // TutorialFlags
                writer.Write(true);         // IsTermsAccepted
                writer.Write(0.5f);         // MasterVolume
                writer.Write(0.6f);         // BgmVolume
                writer.Write(0.7f);         // SeVolume
                writer.Write(true);         // EffectsEnabled
                writer.Flush();
                return stream.ToArray();
            }
        }

        [Test]
        public void v1セーブはv2フィールドがデフォルト0で読める()
        {
            PlayerData data = PlayerDataSerializer.Deserialize(BuildVersion1Payload());

            Assert.AreEqual("旧プレイヤー", data.PlayerName);
            Assert.AreEqual(5000L, data.Chips);
            Assert.AreEqual(3, data.Level);
            Assert.AreEqual(0L, data.Xp);
            Assert.AreEqual(0, data.MatchesPlayed);
            Assert.AreEqual(0, data.HandsPlayed);
            Assert.AreEqual(0, data.HandsWon);
        }

        [Test]
        public void v2ラウンドトリップで成長と戦績が保持される()
        {
            PlayerData data = PlayerData.CreateDefault();
            data.Xp = 12345;
            data.Level = PlayerData.LevelForXp(data.Xp);
            data.MatchesPlayed = 7;
            data.HandsPlayed = 210;
            data.HandsWon = 80;

            PlayerData restored = PlayerDataSerializer.Deserialize(PlayerDataSerializer.Serialize(data));

            Assert.AreEqual(12345L, restored.Xp);
            Assert.AreEqual(PlayerData.LevelForXp(12345), restored.Level);
            Assert.AreEqual(7, restored.MatchesPlayed);
            Assert.AreEqual(210, restored.HandsPlayed);
            Assert.AreEqual(80, restored.HandsWon);
        }

        [Test]
        public void レベルは100XPごとに1上がる()
        {
            Assert.AreEqual(1, PlayerData.LevelForXp(0));
            Assert.AreEqual(1, PlayerData.LevelForXp(99));
            Assert.AreEqual(2, PlayerData.LevelForXp(100));
            Assert.AreEqual(11, PlayerData.LevelForXp(1000));
        }

        [Test]
        public void AddXpはレベルアップ時のみtrueを返す()
        {
            PlayerData data = PlayerData.CreateDefault(); // Xp=0, Level=1
            Assert.IsFalse(data.AddXp(50));  // 50 → Lv.1のまま
            Assert.IsTrue(data.AddXp(60));   // 110 → Lv.2
            Assert.AreEqual(2, data.Level);
            Assert.IsFalse(data.AddXp(10));  // 120 → Lv.2のまま
        }
    }
}
