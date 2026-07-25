using Cysharp.Text;
using KTC.SaveData;
using TMPro;
using UnityEngine;
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
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider bgmSlider;
        [SerializeField] private Slider seSlider;
        [SerializeField] private Button effectsButton;
        [SerializeField] private TMP_Text effectsLabel;
        [SerializeField] private Button closeButton;

        protected override bool CloseOnBackdropClick => true;

        private SaveDataService _saveService;
        private PlayerData _data;

        private void Awake()
        {
            _saveService = SaveDataService.CreateDefault();
            _data = _saveService.Load();

            InitSlider(masterSlider, _data.MasterVolume, value =>
            {
                _data.MasterVolume = value;
                if (SoundController.HasInstance) SoundController.Instance.MasterVolume = value;
            });
            InitSlider(bgmSlider, _data.BgmVolume, value =>
            {
                _data.BgmVolume = value;
                if (SoundController.HasInstance) SoundController.Instance.BGMVolume = value;
            });
            InitSlider(seSlider, _data.SeVolume, value =>
            {
                _data.SeVolume = value;
                if (SoundController.HasInstance) SoundController.Instance.SEVolume = value;
            });

            RefreshEffectsLabel();
            effectsButton.onClick.AddListener(() =>
            {
                _data.EffectsEnabled = !_data.EffectsEnabled;
                RefreshEffectsLabel();
            });
            closeButton.onClick.AddListener(OnCloseClicked);
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
            effectsLabel.text = ZString.Format("演出 : {0}", _data.EffectsEnabled ? "ON" : "OFF");
        }

        private async void OnCloseClicked()
        {
            closeButton.interactable = false;
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
