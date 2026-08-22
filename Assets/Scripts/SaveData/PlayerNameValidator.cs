namespace KTC.SaveData
{
    /// <summary>
    /// プレイヤー名のバリデーション (UI とテストの共通ソース)。
    /// ルール: 前後空白を除去した上で 1〜8 文字、改行・制御文字は禁止。
    /// 文字数は UTF-16 コード単位で数える (絵文字などサロゲートペアは2文字扱い)。
    /// </summary>
    public static class PlayerNameValidator
    {
        public const int MAX_LENGTH = 8;

        /// <summary>
        /// 検証して正規化名を返す。NG の場合は error に表示用メッセージが入る。
        /// </summary>
        public static bool Validate(string raw, out string normalized, out string error)
        {
            normalized = (raw != null ? raw : "").Trim();
            if (normalized.Length == 0)
            {
                error = "名前を入力してください";
                return false;
            }
            if (normalized.Length > MAX_LENGTH)
            {
                error = $"{MAX_LENGTH}文字以内で入力してください";
                return false;
            }
            foreach (char c in normalized)
            {
                if (char.IsControl(c))
                {
                    error = "使用できない文字が含まれています";
                    return false;
                }
            }
            error = null;
            return true;
        }
    }
}
