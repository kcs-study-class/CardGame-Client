using KTC.SaveData;
using UnityFramework.Audio;

namespace KTC.Scene
{
    /// <summary>
    /// BGM 切り替えとセーブ済み音量の適用をまとめるヘルパー。
    /// 各シーンは PrepareAsync で PlayBgm を呼ぶだけでよい (同じ曲なら何もしない)。
    /// </summary>
    public static class GameAudio
    {
        public const string MENU_BGM = "BGM/Menu";
        public const string TABLE_BGM = "BGM/Table";

        private static string CurrentBgm;
        private static bool VolumesApplied;

        /// <summary>
        /// セーブ済み音量を SoundController に反映する (起動後、最初の再生前に1回)。
        /// 設定モーダルでの変更は即時反映されるため、ここは起動時の初期適用のみ。
        /// </summary>
        public static void ApplySavedVolumes()
        {
            if (VolumesApplied)
            {
                return;
            }
            VolumesApplied = true;
            var data = SaveDataService.CreateDefault().Load();
            var sound = SoundController.Instance;
            sound.MasterVolume = data.MasterVolume;
            sound.BGMVolume = data.BgmVolume;
            sound.SEVolume = data.SeVolume;
        }

        /// <summary>指定BGMへクロスフェードで切り替える。再生中の曲と同じなら何もしない。</summary>
        public static void PlayBgm(string id, float fadeTime = 0.8f)
        {
            ApplySavedVolumes();
            if (CurrentBgm == id)
            {
                return;
            }
            CurrentBgm = id;
            _ = SoundController.Instance.PlayBGMAsync(id, fadeTime);
        }
    }
}
