using System;
using System.IO;
using KTC.SaveData;
using NUnit.Framework;
using UnityFramework.SaveData;

namespace KTC.Poker.Tests
{
    public class SaveDataTests
    {
        private string _directory = null;
        private string _filePath = null;
        private SaveKeys _keys = null;
        private SaveDataService _service = null;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "ktc-save-tests-" + Guid.NewGuid().ToString("N"));
            _filePath = Path.Combine(_directory, "save.bin");
            _keys = SaveKeys.Derive("test-secret", "test-device-A");
            _service = new SaveDataService(_filePath, _keys);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private static PlayerData MakeSample()
        {
            PlayerData data = new PlayerData {
                PlayerName = "たむら🦊",
                Chips = 123456789012345,
                Level = 42,
                IsTermsAccepted = true,
                MasterVolume = 0.5f,
                BgmVolume = 0.25f,
                SeVolume = 0.75f,
                EffectsEnabled = false,
            };
            data.SetTutorialFlag(0, true);
            data.SetTutorialFlag(5, true);
            return data;
        }

        [Test]
        public void ファイルが無ければ初期データ()
        {
            Assert.That(_service.HasSave, Is.False);
            PlayerData data = _service.Load();
            Assert.That(data.Chips, Is.EqualTo(1000), "初期チップはDB初期付与額と同じ1000");
            Assert.That(data.IsTermsAccepted, Is.False);
        }

        [Test]
        public void 保存とロードで全フィールドが往復する()
        {
            _service.Save(MakeSample());
            Assert.That(_service.HasSave, Is.True);

            PlayerData restored = _service.Load();
            Assert.That(restored.PlayerName, Is.EqualTo("たむら🦊"), "日本語・絵文字も往復する");
            Assert.That(restored.Chips, Is.EqualTo(123456789012345));
            Assert.That(restored.Level, Is.EqualTo(42));
            Assert.That(restored.HasTutorialFlag(0), Is.True);
            Assert.That(restored.HasTutorialFlag(5), Is.True);
            Assert.That(restored.HasTutorialFlag(1), Is.False);
            Assert.That(restored.IsTermsAccepted, Is.True);
            Assert.That(restored.MasterVolume, Is.EqualTo(0.5f));
            Assert.That(restored.BgmVolume, Is.EqualTo(0.25f));
            Assert.That(restored.SeVolume, Is.EqualTo(0.75f));
            Assert.That(restored.EffectsEnabled, Is.False);
        }

        [Test]
        public void ファイルは平文を含まない()
        {
            _service.Save(MakeSample());
            byte[] file = File.ReadAllBytes(_filePath);
            string ascii = System.Text.Encoding.ASCII.GetString(file);
            Assert.That(ascii, Does.Not.Contain("123456789"), "チップ額が平文で見えない");
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes("たむら");
            Assert.That(IndexOf(file, nameBytes), Is.EqualTo(-1), "名前が平文で見えない");
        }

        [Test]
        public void 暗号文の改ざんは検知され初期データへフォールバック()
        {
            _service.Save(MakeSample());
            TamperByteAt(file => file.Length / 2); // 暗号文の中央を書き換え

            PlayerData data = _service.Load();
            Assert.That(data.Chips, Is.EqualTo(1000), "改ざんセーブは信用しない");
            Assert.That(File.Exists(_filePath + ".corrupt"), Is.True, "調査用に退避される");
        }

        [Test]
        public void ヘッダの改ざんも検知される()
        {
            _service.Save(MakeSample());
            TamperByteAt(_ => 4); // フォーマットバージョンのbyte

            Assert.That(_service.Load().Chips, Is.EqualTo(1000));
        }

        [Test]
        public void 切り詰められたファイルはフォールバック()
        {
            _service.Save(MakeSample());
            byte[] file = File.ReadAllBytes(_filePath);
            File.WriteAllBytes(_filePath, new ArraySegment<byte>(file, 0, 20).ToArray());

            Assert.That(_service.Load().Chips, Is.EqualTo(1000));
        }

        [Test]
        public void 別端末の鍵では復号できない()
        {
            _service.Save(MakeSample());
            SaveDataService otherDevice = new SaveDataService(_filePath, SaveKeys.Derive("test-secret", "test-device-B"));

            Assert.That(otherDevice.Load().Chips, Is.EqualTo(1000), "端末間コピーは無効");
        }

        [Test]
        public void 保存後に一時ファイルは残らない()
        {
            _service.Save(MakeSample());
            _service.Save(MakeSample()); // 既存ファイルがある状態の上書き (File.Replace 経路)
            Assert.That(File.Exists(_filePath + ".tmp"), Is.False);
            Assert.That(_service.Load().Level, Is.EqualTo(42));
        }

        [Test]
        public void 未来のデータバージョンはフォールバック()
        {
            // 正しい鍵で「バージョン999」のペイロードを作って書き込む
            byte[] payload = PlayerDataSerializer.Serialize(MakeSample());
            BitConverter.GetBytes(999).CopyTo(payload, 0); // 先頭int = dataVersion
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(_filePath, SaveCrypto.Encrypt(payload, _keys));

            Assert.That(_service.Load().Chips, Is.EqualTo(1000));
            Assert.That(File.Exists(_filePath + ".corrupt"), Is.True);
        }

        [Test]
        public void Deleteで関連ファイルがすべて消える()
        {
            _service.Save(MakeSample());
            TamperByteAt(file => file.Length / 2);
            _service.Load(); // .corrupt を生成させる

            _service.Delete();
            Assert.That(File.Exists(_filePath), Is.False);
            Assert.That(File.Exists(_filePath + ".corrupt"), Is.False);
            Assert.That(_service.HasSave, Is.False);
        }

        // ---- helpers ----

        private void TamperByteAt(Func<byte[], int> offsetSelector)
        {
            byte[] file = File.ReadAllBytes(_filePath);
            int offset = offsetSelector(file);
            file[offset] ^= 0xFF;
            File.WriteAllBytes(_filePath, file);
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
