using KTC.SaveData;
using NUnit.Framework;

namespace KTC.Poker.Tests
{
    public class PlayerNameValidatorTests
    {
        [TestCase("たむら", "たむら")]
        [TestCase("  たむら  ", "たむら", TestName = "前後空白は除去される")]
        [TestCase("Player01", "Player01", TestName = "8文字ちょうどはOK")]
        [TestCase("A", "A", TestName = "1文字はOK")]
        public void 有効な名前(string input, string expected)
        {
            Assert.That(PlayerNameValidator.Validate(input, out string normalized, out string error), Is.True, error);
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [TestCase("", TestName = "空文字")]
        [TestCase("   ", TestName = "空白のみ")]
        [TestCase(null, TestName = "null")]
        [TestCase("Player001", TestName = "9文字は長すぎ")]
        [TestCase("たむ\nら", TestName = "改行入り")]
        [TestCase("a\tb", TestName = "タブ入り")]
        public void 無効な名前(string input)
        {
            Assert.That(PlayerNameValidator.Validate(input, out _, out string error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty, "表示用エラーメッセージが入る");
        }
    }
}
