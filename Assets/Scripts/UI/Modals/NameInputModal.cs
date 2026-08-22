using KTC.SaveData;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityFramework.UI;

namespace KTC.UI
{
    /// <summary>
    /// プレイヤー名入力モーダル。検証を通った名前で「決定」すると閉じる。
    /// 閉じた後に <see cref="ResultName"/> から結果を取得する。
    /// </summary>
    public class NameInputModal : ModalBase
    {
        [SerializeField, FormerlySerializedAs("nameInput")] private TMP_InputField _nameInput;
        [SerializeField, FormerlySerializedAs("okButton")] private Button _okButton;
        [SerializeField, FormerlySerializedAs("hintText")] private TMP_Text _hintText;

        /// <summary>決定された名前 (正規化済み)。キャンセル不可のため閉じた時点で必ず有効。</summary>
        public string ResultName { get; private set; } = "";

        private void Awake()
        {
            _nameInput.characterLimit = PlayerNameValidator.MAX_LENGTH + 2; // 入力中の前後空白ぶん余裕
            _nameInput.onValueChanged.AddListener(_ => Refresh());
            _okButton.onClick.AddListener(OnOkClicked);
            Refresh();
        }

        private void Refresh()
        {
            bool valid = PlayerNameValidator.Validate(_nameInput.text, out _, out var error);
            _okButton.interactable = valid;
            _hintText.text = valid ? "" : error;
        }

        private async void OnOkClicked()
        {
            if (!PlayerNameValidator.Validate(_nameInput.text, out var normalized, out _))
            {
                return;
            }
            ResultName = normalized;
            _okButton.interactable = false; // 連打防止
            await CloseAsync();
        }
    }
}
