using Cysharp.Text;
using KTC.SaveData;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityFramework.Audio;
using UnityFramework.UI;

namespace KTC.UI
{
    /// <summary>
    /// 設定モーダル (音量 / 演出ON・OFF)。
    /// スライダー操作は即時 SoundController に反映し、閉じたときにセーブへ書き込む。
    /// </summary>
    public class SettingsModal : ModalBase
    {
        [SerializeField, FormerlySerializedAs("masterSlider")] private Slider _masterSlider = null;
        [SerializeField, FormerlySerializedAs("bgmSlider")] private Slider _bgmSlider = null;
        [SerializeField, FormerlySerializedAs("seSlider")] private Slider _seSlider = null;
        [SerializeField, FormerlySerializedAs("effectsButton")] private Button _effectsButton = null;
        [SerializeField, FormerlySerializedAs("effectsLabel")] private TMP_Text _effectsLabel = null;
        [SerializeField, FormerlySerializedAs("closeButton")] private Button _closeButton = null;

        protected override bool CloseOnBackdropClick => true;

        private SaveDataService _saveService = null;
        private PlayerData _data = null;

        private void Awake()
        {
            _saveService = SaveDataService.CreateDefault();
            _data = _saveService.Load();

            InitSlider(_masterSlider, _data.MasterVolume, value =>
            {
                _data.MasterVolume = value;
                if (SoundController.HasInstance) SoundController.Instance.MasterVolume = value;
            });
            InitSlider(_bgmSlider, _data.BgmVolume, value =>
            {
                _data.BgmVolume = value;
                if (SoundController.HasInstance) SoundController.Instance.BGMVolume = value;
            });
            InitSlider(_seSlider, _data.SeVolume, value =>
            {
                _data.SeVolume = value;
                if (SoundController.HasInstance) SoundController.Instance.SEVolume = value;
            });

            RefreshEffectsLabel();
            _effectsButton.onClick.AddListener(() =>
            {
                _data.EffectsEnabled = !_data.EffectsEnabled;
                RefreshEffectsLabel();
            });
            _closeButton.onClick.AddListener(OnCloseClicked);
        }

        private static void InitSlider(Slider slider, float initial, UnityEngine.Events.UnityAction<float> onChanged)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(initial);
            slider.onValueChanged.AddListener(onChanged);
        }

        private void RefreshEffectsLabel()
        {
            _effectsLabel.text = ZString.Format("演出 : {0}", _data.EffectsEnabled ? "ON" : "OFF");
        }

        private async void OnCloseClicked()
        {
            _closeButton.interactable = false;
            await CloseAsync();
        }

        /// <summary>閉じ演出フック。背景クリック閉じを含む全経路でここを通るため、保存はここで行う。</summary>
        protected override Awaitable OnCloseAsync(System.Threading.CancellationToken cancellationToken)
        {
            _saveService.Save(_data);
            return UnityFramework.Awaitables.Completed;
        }
    }
}
