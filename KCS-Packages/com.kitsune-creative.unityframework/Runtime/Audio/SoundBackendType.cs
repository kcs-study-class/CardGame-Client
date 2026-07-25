namespace UnityFramework.Audio
{
    /// <summary>
    /// SoundController のバックエンド種別。
    /// </summary>
    public enum SoundBackendType
    {
        /// <summary>Unity 標準の AudioSource を使う。AudioClip は Addressables 経由でロード。</summary>
        Unity,

        /// <summary>CRI ADX (criware-unity) を使う。<c>UNITY_FRAMEWORK_USE_CRI</c> 定義時のみ有効。</summary>
        CriAdx,
    }
}
