using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityFramework.UI;

namespace KTC.UI
{
    /// <summary>
    /// 利用規約同意モーダル。「同意する」で閉じる。
    /// 同意した事実の保存は呼び出し側 (Title) が行う。
    /// </summary>
    public class TermsModal : ModalBase
    {
        [SerializeField, FormerlySerializedAs("agreeButton")] private Button _agreeButton = null;

        private void Awake()
        {
            _agreeButton.onClick.AddListener(OnAgreeClicked);
        }

        private void Start()
        {
            // ScrollRect のクランプで初期位置が下端に張り付くため、先頭へ戻す
            ScrollRect scroll = GetComponentInChildren<ScrollRect>();
            if (scroll != null)
            {
                scroll.verticalNormalizedPosition = 1f;
            }
        }

        private async void OnAgreeClicked()
        {
            _agreeButton.interactable = false; // 連打防止
            await CloseAsync();
        }
    }
}
