using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityFramework.Resource;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;

namespace KTC.Scene
{
    public class Boot : MonoBehaviour
    {
        private void Awake()
        {
            enabled = false;
            // Packages側でもっと良い感じにしたい...
#if false
            SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Logo, SceneIdExtensions.ToSceneName);
#else
            SceneController.Instance.LoadSceneViaTransitionSceneAsync(SceneId.Title, SceneId.TransitionLoading,
                SceneIdExtensions.ToSceneName, minimumDuration: 1.5f);
#endif


            ResourceController.Instance.ReleaseAll(); // FIXME : Addressables の初期化処理作成して呼び替え変更、参照修正.
        }
    }
}