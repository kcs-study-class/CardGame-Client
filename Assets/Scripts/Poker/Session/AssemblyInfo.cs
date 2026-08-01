using System.Runtime.CompilerServices;

// テストから内部フック (積み込みデッキ注入など) を使うため
[assembly: InternalsVisibleTo("KTC.Poker.Tests.EditMode")]
