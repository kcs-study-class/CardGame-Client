using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace UnityFramework.SceneManagement
{
    /// <summary>
    /// シーン遷移を一元管理するシングルトン。
    /// Awaitable ベースの非同期 API と進捗イベントを提供する。
    /// エラー時は例外を投げず、SafeLogger に記録して安全な既定値を返す方針。
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneController : SingletonMonoBehaviour<SceneController>
    {
        /// <summary>シーンロード開始時に発火する。引数はロード対象シーン名。</summary>
        public event Action<string> SceneLoadStarted;

        /// <summary>シーンロード中に毎フレーム発火する。引数は (シーン名, 進捗 0..1)。</summary>
        public event Action<string, float> SceneLoadProgress;

        /// <summary>シーンロード完了時に発火する。引数はロード完了したシーン名。</summary>
        public event Action<string> SceneLoadCompleted;

        /// <summary>シーンアンロード完了時に発火する。引数はアンロードしたシーン名。</summary>
        public event Action<string> SceneUnloadCompleted;

        public bool IsLoading { get; private set; }

        public string ActiveSceneName => UnitySceneManager.GetActiveScene().name;
        public int ActiveSceneBuildIndex => UnitySceneManager.GetActiveScene().buildIndex;

        /// <summary>
        /// アクティブシーンの <see cref="IScenePreparer"/> をすべて実行する。
        /// フェード付きロードでは「画面を見せる前」に呼ばれるため、
        /// Addressables のロード/DL や UI 構築をここで完了させられる。
        /// </summary>
        private static async Awaitable RunScenePreparersAsync(CancellationToken cancellationToken)
        {
            Scene scene = UnitySceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (IScenePreparer preparer in root.GetComponentsInChildren<IScenePreparer>(true))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await preparer.PrepareAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        // 準備の失敗で遷移全体 (フェードイン) を巻き込まない。
                        // 画面が真っ暗のまま止まるより、壊れたシーンでも見せて原因を追える方が良い
                        SafeLogger.LogError($"[SceneController] シーン準備 '{preparer.GetType().Name}' が失敗しました: {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// シーンを非同期にロードする。
        /// </summary>
        public async Awaitable LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken cancellationToken = default)
        {
            if (IsLoading)
            {
                SafeLogger.LogWarning($"[SceneController] 既にロード中のため、{sceneName} のリクエストを無視しました。");
                return;
            }

            IsLoading = true;
            SceneLoadStarted?.Invoke(sceneName);

            try
            {
                AsyncOperation operation = UnitySceneManager.LoadSceneAsync(sceneName, mode);
                if (operation == null)
                {
                    SafeLogger.LogError($"[SceneController] シーン '{sceneName}' のロードを開始できません。Build Settings に含まれているか確認してください。");
                    return;
                }

                operation.allowSceneActivation = true;

                while (!operation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SceneLoadProgress?.Invoke(sceneName, operation.progress);
                    await Awaitable.NextFrameAsync(cancellationToken);
                }

                SceneLoadProgress?.Invoke(sceneName, 1f);
                SceneLoadCompleted?.Invoke(sceneName);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// シーンを 90% までロードした後、最低 <paramref name="minimumDuration"/> 秒待ってからアクティベートする。
        /// ロード画面の最小表示時間を確保したい場合に利用する。
        /// </summary>
        public async Awaitable LoadSceneWithMinimumDurationAsync(string sceneName, float minimumDuration, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken cancellationToken = default)
        {
            if (IsLoading)
            {
                SafeLogger.LogWarning($"[SceneController] 既にロード中のため、{sceneName} のリクエストを無視しました。");
                return;
            }

            IsLoading = true;
            SceneLoadStarted?.Invoke(sceneName);

            try
            {
                AsyncOperation operation = UnitySceneManager.LoadSceneAsync(sceneName, mode);
                if (operation == null)
                {
                    SafeLogger.LogError($"[SceneController] シーン '{sceneName}' のロードを開始できません。Build Settings に含まれているか確認してください。");
                    return;
                }

                operation.allowSceneActivation = false;
                float elapsed = 0f;

                // 0..0.9 が実ロード、0.9..1.0 はアクティベート段階。
                while (operation.progress < 0.9f)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    elapsed += Time.unscaledDeltaTime;
                    SceneLoadProgress?.Invoke(sceneName, Mathf.Clamp01(operation.progress / 0.9f * 0.99f));
                    await Awaitable.NextFrameAsync(cancellationToken);
                }

                while (elapsed < minimumDuration)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    elapsed += Time.unscaledDeltaTime;
                    SceneLoadProgress?.Invoke(sceneName, 0.99f);
                    await Awaitable.NextFrameAsync(cancellationToken);
                }

                operation.allowSceneActivation = true;

                while (!operation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Awaitable.NextFrameAsync(cancellationToken);
                }

                SceneLoadProgress?.Invoke(sceneName, 1f);
                SceneLoadCompleted?.Invoke(sceneName);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Additive モードで読み込んだシーンを非同期にアンロードする。
        /// </summary>
        public async Awaitable UnloadSceneAsync(string sceneName, CancellationToken cancellationToken = default)
        {
            AsyncOperation operation = UnitySceneManager.UnloadSceneAsync(sceneName);
            if (operation == null)
            {
                SafeLogger.LogWarning($"[SceneController] シーン '{sceneName}' はアンロードできません。ロード済みか確認してください。");
                return;
            }

            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            SceneUnloadCompleted?.Invoke(sceneName);
        }

        /// <summary>
        /// アクティブシーンを設定する。Additive ロード後にアクティブシーンを切り替えたい場合に利用。
        /// </summary>
        public bool SetActiveScene(string sceneName)
        {
            Scene scene = UnitySceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid())
            {
                SafeLogger.LogWarning($"[SceneController] シーン '{sceneName}' は有効ではありません。ロード済みか確認してください。");
                return false;
            }
            return UnitySceneManager.SetActiveScene(scene);
        }

        // ---- Transition 付きロード ----

        /// <summary>
        /// フェード (または CustomTransition) を挟んでシーンをロードする。
        /// <see cref="TransitionController"/> が未生成の場合は自動生成される。
        /// </summary>
        public async Awaitable LoadSceneWithFadeAsync(
            string sceneName,
            LoadSceneMode mode = LoadSceneMode.Single,
            float fadeOutDuration = 0.3f,
            float fadeInDuration = 0.3f,
            CancellationToken cancellationToken = default)
        {
            if (IsLoading)
            {
                SafeLogger.LogWarning($"[SceneController] 既にロード中のため、{sceneName} のリクエストを無視しました。");
                return;
            }

            IsLoading = true;
            SceneLoadStarted?.Invoke(sceneName);

            try
            {
                TransitionController transition = TransitionController.Instance;
                await transition.PlayOutAsync(fadeOutDuration, cancellationToken);

                AsyncOperation operation = UnitySceneManager.LoadSceneAsync(sceneName, mode);
                if (operation == null)
                {
                    SafeLogger.LogError($"[SceneController] シーン '{sceneName}' のロードを開始できません。Build Settings を確認してください。");
                    return;
                }

                while (!operation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SceneLoadProgress?.Invoke(sceneName, operation.progress);
                    await Awaitable.NextFrameAsync(cancellationToken);
                }
                SceneLoadProgress?.Invoke(sceneName, 1f);

                // 画面を見せる前にシーンの準備 (UI構築・アセットロード等) を完了させる
                await RunScenePreparersAsync(cancellationToken);

                await transition.PlayInAsync(fadeInDuration, cancellationToken);

                SceneLoadCompleted?.Invoke(sceneName);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Transition 用シーン (例: "Loading") を挟んでターゲットシーンをロードする。
        /// <para>処理の流れ:</para>
        /// <list type="number">
        /// <item>フェードアウト</item>
        /// <item>Transition シーンへ切替 (Single)</item>
        /// <item>フェードイン (Transition シーンを表示)</item>
        /// <item>ターゲットシーンを背後ロード (allowSceneActivation=false)</item>
        /// <item>進捗 90% 到達 + 最低表示時間を満たすまで待機</item>
        /// <item>フェードアウト</item>
        /// <item>ターゲットを活性化 (Transition シーンは自動置換)</item>
        /// <item>フェードイン</item>
        /// </list>
        /// Transition シーン側では <see cref="SceneLoadProgress"/> を購読してローディング UI を更新できる。
        /// </summary>
        public async Awaitable LoadSceneViaTransitionSceneAsync(
            string targetSceneName,
            string transitionSceneName,
            float minimumDuration = 0f,
            float fadeDuration = 0.3f,
            CancellationToken cancellationToken = default)
        {
            if (IsLoading)
            {
                SafeLogger.LogWarning($"[SceneController] 既にロード中のため、{targetSceneName} のリクエストを無視しました。");
                return;
            }

            IsLoading = true;
            SceneLoadStarted?.Invoke(targetSceneName);

            try
            {
                TransitionController transition = TransitionController.Instance;

                // 1. フェードアウト (現在のシーンを覆う)
                await transition.PlayOutAsync(fadeDuration, cancellationToken);

                // 2. Transition シーンへ切替
                AsyncOperation transitionOp = UnitySceneManager.LoadSceneAsync(transitionSceneName, LoadSceneMode.Single);
                if (transitionOp == null)
                {
                    SafeLogger.LogError($"[SceneController] Transition シーン '{transitionSceneName}' のロードを開始できません。");
                    return;
                }
                while (!transitionOp.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Awaitable.NextFrameAsync(cancellationToken);
                }

                // Transition シーン自身の準備も見せる前に済ませる
                await RunScenePreparersAsync(cancellationToken);

                // 3. フェードイン (Transition シーンを表示)
                await transition.PlayInAsync(fadeDuration, cancellationToken);

                // 4. ターゲットを背後ロード (活性化はまだ)
                AsyncOperation targetOp = UnitySceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Single);
                if (targetOp == null)
                {
                    SafeLogger.LogError($"[SceneController] ターゲットシーン '{targetSceneName}' のロードを開始できません。");
                    return;
                }
                targetOp.allowSceneActivation = false;

                float elapsed = 0f;
                while (targetOp.progress < 0.9f)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    elapsed += Time.unscaledDeltaTime;
                    SceneLoadProgress?.Invoke(targetSceneName, Mathf.Clamp01(targetOp.progress / 0.9f * 0.99f));
                    await Awaitable.NextFrameAsync(cancellationToken);
                }

                // 5. 最低表示時間を確保 (ローディング画面のチラつき防止)
                while (elapsed < minimumDuration)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    elapsed += Time.unscaledDeltaTime;
                    SceneLoadProgress?.Invoke(targetSceneName, 0.99f);
                    await Awaitable.NextFrameAsync(cancellationToken);
                }

                // 6. フェードアウト (Transition シーンを隠す)
                await transition.PlayOutAsync(fadeDuration, cancellationToken);

                // 7. ターゲットを活性化 (Single モードなので Transition シーンは自動置換)
                targetOp.allowSceneActivation = true;
                while (!targetOp.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Awaitable.NextFrameAsync(cancellationToken);
                }
                SceneLoadProgress?.Invoke(targetSceneName, 1f);

                // ターゲットシーンの準備 (UI構築・アセットロード等) を見せる前に完了させる
                await RunScenePreparersAsync(cancellationToken);

                // 8. フェードイン (ターゲットシーンを表示)
                await transition.PlayInAsync(fadeDuration, cancellationToken);

                SceneLoadCompleted?.Invoke(targetSceneName);
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ---- Build Index ベースのロード ----

        /// <summary>
        /// Build Index 指定でシーンを非同期にロードする。生成された <c>SceneId.X.ToBuildIndex()</c> または <c>(int)SceneId.X</c> と組み合わせて使う。
        /// </summary>
        public async Awaitable LoadSceneAsync(int buildIndex, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken cancellationToken = default)
        {
            if (IsLoading)
            {
                SafeLogger.LogWarning($"[SceneController] 既にロード中のため、buildIndex={buildIndex} のリクエストを無視しました。");
                return;
            }

            string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            string sceneName = string.IsNullOrEmpty(path) ? $"#{buildIndex}" : System.IO.Path.GetFileNameWithoutExtension(path);

            IsLoading = true;
            SceneLoadStarted?.Invoke(sceneName);

            try
            {
                AsyncOperation operation = UnitySceneManager.LoadSceneAsync(buildIndex, mode);
                if (operation == null)
                {
                    SafeLogger.LogError($"[SceneController] buildIndex={buildIndex} のロードを開始できません。Build Settings の範囲を確認してください。");
                    return;
                }

                operation.allowSceneActivation = true;

                while (!operation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SceneLoadProgress?.Invoke(sceneName, operation.progress);
                    await Awaitable.NextFrameAsync(cancellationToken);
                }

                SceneLoadProgress?.Invoke(sceneName, 1f);
                SceneLoadCompleted?.Invoke(sceneName);
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ---- Enum-based API ----
        // 生成された SceneId enum + SceneIdExtensions.ToSceneName() と組み合わせて型安全に呼び出す。
        // 例: await SceneController.Instance.LoadSceneAsync(SceneId.MainMenu, id => id.ToSceneName());

        /// <summary>enum でシーンをロード。</summary>
        public Awaitable LoadSceneAsync<TEnum>(TEnum sceneId, Func<TEnum, string> nameResolver, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken cancellationToken = default)
            where TEnum : struct, Enum
        {
            if (nameResolver == null)
            {
                SafeLogger.LogError("[SceneController] nameResolver が null です。LoadScene をスキップしました。");
                return Awaitables.Completed;
            }
            return LoadSceneAsync(nameResolver(sceneId), mode, cancellationToken);
        }

        /// <summary>enum で最低表示時間付きロード。</summary>
        public Awaitable LoadSceneWithMinimumDurationAsync<TEnum>(TEnum sceneId, Func<TEnum, string> nameResolver, float minimumDuration, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken cancellationToken = default)
            where TEnum : struct, Enum
        {
            if (nameResolver == null)
            {
                SafeLogger.LogError("[SceneController] nameResolver が null です。LoadSceneWithMinimumDuration をスキップしました。");
                return Awaitables.Completed;
            }
            return LoadSceneWithMinimumDurationAsync(nameResolver(sceneId), minimumDuration, mode, cancellationToken);
        }

        /// <summary>enum でアンロード。</summary>
        public Awaitable UnloadSceneAsync<TEnum>(TEnum sceneId, Func<TEnum, string> nameResolver, CancellationToken cancellationToken = default)
            where TEnum : struct, Enum
        {
            if (nameResolver == null)
            {
                SafeLogger.LogError("[SceneController] nameResolver が null です。UnloadScene をスキップしました。");
                return Awaitables.Completed;
            }
            return UnloadSceneAsync(nameResolver(sceneId), cancellationToken);
        }

        /// <summary>enum でアクティブシーン設定。</summary>
        public bool SetActiveScene<TEnum>(TEnum sceneId, Func<TEnum, string> nameResolver)
            where TEnum : struct, Enum
        {
            if (nameResolver == null)
            {
                SafeLogger.LogError("[SceneController] nameResolver が null です。SetActiveScene をスキップしました。");
                return false;
            }
            return SetActiveScene(nameResolver(sceneId));
        }

        /// <summary>enum でフェード付きロード。</summary>
        public Awaitable LoadSceneWithFadeAsync<TEnum>(TEnum sceneId, Func<TEnum, string> nameResolver, LoadSceneMode mode = LoadSceneMode.Single, float fadeOutDuration = 0.3f, float fadeInDuration = 0.3f, CancellationToken cancellationToken = default)
            where TEnum : struct, Enum
        {
            if (nameResolver == null)
            {
                SafeLogger.LogError("[SceneController] nameResolver が null です。LoadSceneWithFade をスキップしました。");
                return Awaitables.Completed;
            }
            return LoadSceneWithFadeAsync(nameResolver(sceneId), mode, fadeOutDuration, fadeInDuration, cancellationToken);
        }

        /// <summary>enum で Transition シーン挟み込みロード。</summary>
        public Awaitable LoadSceneViaTransitionSceneAsync<TEnum>(TEnum targetSceneId, TEnum transitionSceneId, Func<TEnum, string> nameResolver, float minimumDuration = 0f, float fadeDuration = 0.3f, CancellationToken cancellationToken = default)
            where TEnum : struct, Enum
        {
            if (nameResolver == null)
            {
                SafeLogger.LogError("[SceneController] nameResolver が null です。LoadSceneViaTransitionScene をスキップしました。");
                return Awaitables.Completed;
            }
            return LoadSceneViaTransitionSceneAsync(nameResolver(targetSceneId), nameResolver(transitionSceneId), minimumDuration, fadeDuration, cancellationToken);
        }
    }
}
