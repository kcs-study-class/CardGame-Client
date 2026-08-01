using UnityEngine;
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
        /// <summary>選択肢 (SB, BB)。プレハブの optionButtons とこの順で対応させる。</summary>
        public static readonly (int Small, int Big)[] Options = { (1, 2), (2, 4), (5, 10), (10, 20) };

        [Header("選択肢 (Options と同順に割り当て)")]
        [SerializeField] private Button[] optionButtons;
        [SerializeField] private Button closeButton;

        protected override bool CloseOnBackdropClick => true;

        public int SelectedSmallBlind { get; private set; }
        public int SelectedBigBlind { get; private set; }

        private void Awake()
        {
            for (int i = 0; i < optionButtons.Length; i++)
            {
                var (small, big) = Options[i];
                optionButtons[i].onClick.AddListener(() => OnSelect(small, big));
            }
            closeButton.onClick.AddListener(() => _ = CloseAsync());
        }

        /// <summary>現在の選択を反映してハイライトする (開いた直後に呼ぶ)。</summary>
        public void SetCurrent(int smallBlind, int bigBlind)
        {
            SelectedSmallBlind = smallBlind;
            SelectedBigBlind = bigBlind;
            for (int i = 0; i < optionButtons.Length; i++)
            {
                QuickUi.SetSelected(optionButtons[i], Options[i].Small == smallBlind);
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
