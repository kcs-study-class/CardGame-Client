using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using UnityFramework.Resource;

namespace UnityFramework.UI
{
    /// <summary>
    /// モーダルウィンドウを管理するシングルトン。
    /// プレハブは Addressables (<see cref="ResourceController"/>) からアドレス指定でロードし、
    /// スタック管理・背景ディム・入力ブロックを提供する。
    ///
    /// 使い方:
    /// <code>
    /// var modal = await ModalController.Instance.OpenAsync&lt;ShopModal&gt;("Modals/Shop");
    /// await modal.WaitUntilClosedAsync();
    /// </code>
    /// プレハブのルートには <see cref="ModalBase"/> 派生コンポーネントを付けておくこと。
    /// </summary>
    [DisallowMultipleComponent]
    public class ModalController : SingletonMonoBehaviour<ModalController>
    {
        [Header("Modal Canvas")]
        [SerializeField] private int _sortingOrder = 30000; // TransitionCanvas (32767) より下
        [SerializeField] private Color _dimColor = new Color(0f, 0f, 0f, 0.6f);
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        private Canvas _canvas;
        private RectTransform _contentRoot;
        private Image _dim;
        private readonly List<ModalBase> _stack = new List<ModalBase>();

        /// <summary>開いているモーダルがあるか。</summary>
        public bool HasOpenModal => _stack.Count > 0;

        /// <summary>最前面のモーダル。なければ null。</summary>
        public ModalBase Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            EnsureSetup();
        }

        private void EnsureSetup()
        {
            if (_canvas != null) return;

            var canvasGo = new GameObject("[ModalCanvas]");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);
            canvasGo.layer = 5;

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = _sortingOrder;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            _contentRoot = (RectTransform)canvasGo.transform;

            // 背景ディム (モーダル表示中の入力ブロック + クリック閉じ)
            var dimGo = new GameObject("Dim");
            dimGo.transform.SetParent(_contentRoot, false);
            dimGo.layer = 5;
            var dimRt = dimGo.AddComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            _dim = dimGo.AddComponent<Image>();
            _dim.color = _dimColor;
            _dim.raycastTarget = true;
            var dimButton = dimGo.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(OnBackdropClicked);
            dimGo.SetActive(false);
        }

        /// <summary>
        /// Addressables アドレスからモーダルプレハブをロードして開く。
        /// プレハブのルートに T (ModalBase 派生) が付いていない場合は null を返す。
        /// </summary>
        public async Awaitable<T> OpenAsync<T>(string address, CancellationToken cancellationToken = default) where T : ModalBase
        {
            EnsureSetup();

            var prefab = await ResourceController.Instance.LoadAsync<GameObject>(address, cancellationToken);
            if (prefab == null)
            {
                SafeLogger.LogError($"[ModalController] モーダルのロードに失敗しました: {address}");
                return null;
            }

            var instanceGo = Instantiate(prefab, _contentRoot);
            var modal = instanceGo.GetComponent<T>();
            if (modal == null)
            {
                SafeLogger.LogError($"[ModalController] プレハブ '{address}' のルートに {typeof(T).Name} がありません。");
                Destroy(instanceGo);
                ResourceController.Instance.Release(address);
                return null;
            }

            modal.Setup(this, address);
            _stack.Add(modal);
            UpdateDim();
            await modal.PlayOpenAsync(cancellationToken);
            return modal;
        }

        /// <summary>指定モーダルを閉じる (演出 → 破棄 → アセット解放)。</summary>
        public async Awaitable CloseAsync(ModalBase modal, CancellationToken cancellationToken = default)
        {
            if (modal == null || !_stack.Contains(modal))
            {
                return;
            }

            await modal.PlayCloseAsync(cancellationToken);
            _stack.Remove(modal);
            string address = modal.Address;
            Destroy(modal.gameObject);
            ResourceController.Instance.Release(address);
            UpdateDim();
        }

        /// <summary>最前面のモーダルを閉じる。</summary>
        public Awaitable CloseTopAsync(CancellationToken cancellationToken = default)
        {
            return Top != null ? CloseAsync(Top, cancellationToken) : Awaitables.Completed;
        }

        /// <summary>すべてのモーダルを上から順に閉じる。</summary>
        public async Awaitable CloseAllAsync(CancellationToken cancellationToken = default)
        {
            while (Top != null)
            {
                await CloseAsync(Top, cancellationToken);
            }
        }

        private void OnBackdropClicked()
        {
            var top = Top;
            if (top != null && top.CloseOnBackdropClick)
            {
                _ = CloseAsync(top);
            }
        }

        private void UpdateDim()
        {
            bool visible = _stack.Count > 0;
            _dim.gameObject.SetActive(visible);
            if (visible)
            {
                // ディムは常に最前面モーダルの直下に置く
                var top = _stack[_stack.Count - 1];
                int topIndex = top.transform.GetSiblingIndex();
                _dim.transform.SetSiblingIndex(Mathf.Max(0, topIndex - 1));
            }
        }
    }
}
