using System;
using UnityEngine;
using UnityEngine.SceneManagement;
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
            SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Logo, SceneIdExtensions.ToSceneName);
        }
    }
}