using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityFramework.UI;

namespace KTC.UI
{
    /// <summary>
    /// ブラインド (SB/BB) 選択モーダル。選択肢を押すと即クローズし、
    /// 呼び出し側は <see cref="SelectedSmallBlind"/> / <see cref="SelectedBigBlind"/> を読む。
    /// キャンセル (背景クリック/閉じる) 時は <see cref="SetCurrent"/> で渡した値のまま。
    /// </summary>
    public class BlindsModal : ModalBase
    {
        /// <summary>選択肢 (SB, BB)。プレハブの _optionButtons とこの順で対応させる。</summary>
        public static readonly (int Small, int Big)[] OPTIONS = { (1, 2), (2, 4), (5, 10), (10, 20) };

        [Header("選択肢 (OPTIONS と同順に割り当て)")]
        [SerializeField, FormerlySerializedAs("optionButtons")] private Button[] _optionButtons;
        [SerializeField, FormerlySerializedAs("closeButton")] private Button _closeButton;

        protected override bool CloseOnBackdropClick => true;

        public int SelectedSmallBlind { get; private set; }
        public int SelectedBigBlind { get; private set; }

        private void Awake()
        {
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                var (small, big) = OPTIONS[i];
                _optionButtons[i].onClick.AddListener(() => OnSelect(small, big));
            }
            _closeButton.onClick.AddListener(() => _ = CloseAsync());
        }

        /// <summary>現在の選択を反映してハイライトする (開いた直後に呼ぶ)。</summary>
        public void SetCurrent(int smallBlind, int bigBlind)
        {
            SelectedSmallBlind = smallBlind;
            SelectedBigBlind = bigBlind;
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                QuickUi.SetSelected(_optionButtons[i], OPTIONS[i].Small == smallBlind);
            }
        }

        private async void OnSelect(int small, int big)
        {
            SelectedSmallBlind = small;
            SelectedBigBlind = big;
            await CloseAsync();
        }
    }
}
