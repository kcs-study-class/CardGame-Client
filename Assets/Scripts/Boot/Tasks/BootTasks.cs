using System.Threading;
using KTC.SaveData;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityFramework;

namespace KTC.Boot
{
    /// <summary>セーブデータのロード (ローカル実処理)。</summary>
    public sealed class LoadSaveDataTask : IBootTask
    {
        public string DisplayName => "セーブデータ";

        public async Awaitable<BootTaskResult> RunAsync(BootContext context, CancellationToken cancellationToken)
        {
            context.PlayerData = SaveDataService.CreateDefault().Load();
            await Awaitables.Completed;
            return BootTaskResult.Ok();
        }
    }

    /// <summary>
    /// メンテナンスチェック (スタブ)。
    /// TODO(生徒課題): GET /api/v1/maintenance に置き換える (CardGame-Server/Docs/API.md §2)。
    /// メンテ中は Fail を返してブートを停止し、メッセージを表示させる。
    /// </summary>
    public sealed class MaintenanceCheckTask : IBootTask
    {
        public string DisplayName => "メンテナンス確認";

        public async Awaitable<BootTaskResult> RunAsync(BootContext context, CancellationToken cancellationToken)
        {
            await Awaitable.WaitForSecondsAsync(0.1f, cancellationToken); // 通信っぽい待ち (スタブ)
            context.MaintenanceActive = false;
            context.MaintenanceMessage = "";
            return context.MaintenanceActive
                ? BootTaskResult.Fail(context.MaintenanceMessage)
                : BootTaskResult.Ok();
        }
    }

    /// <summary>
    /// ログイン (スタブ)。
    /// TODO(生徒課題): GameHttpClient.Shared で POST /api/v1/login {deviceId, name} に置き換える (API.md §2)。
    /// 取得したトークンは context.AuthToken に格納し、以降の通信で使う。
    /// </summary>
    public sealed class LoginTask : IBootTask
    {
        public string DisplayName => "ログイン";

        public async Awaitable<BootTaskResult> RunAsync(BootContext context, CancellationToken cancellationToken)
        {
            await Awaitable.WaitForSecondsAsync(0.2f, cancellationToken); // 通信っぽい待ち (スタブ)
            context.IsLoggedIn = true;
            context.AuthToken = "local-stub-token";
            return BootTaskResult.Ok();
        }
    }

    /// <summary>
    /// お知らせ取得 (スタブ)。
    /// TODO(生徒課題): GET /api/v1/notices に置き換える (API.md §2)。
    /// </summary>
    public sealed class FetchNoticesTask : IBootTask
    {
        public string DisplayName => "お知らせ";

        public async Awaitable<BootTaskResult> RunAsync(BootContext context, CancellationToken cancellationToken)
        {
            await Awaitable.WaitForSecondsAsync(0.1f, cancellationToken); // 通信っぽい待ち (スタブ)
            context.NoticeTitles = System.Array.Empty<string>();
            return BootTaskResult.Ok();
        }
    }

    /// <summary>
    /// アセット更新チェック (Addressables カタログ)。
    /// 現状は更新の有無をログに出すだけ。ダウンロード実行はリモートカタログ導入時に実装する。
    /// </summary>
    public sealed class AssetUpdateCheckTask : IBootTask
    {
        public string DisplayName => "アセット更新確認";

        public async Awaitable<BootTaskResult> RunAsync(BootContext context, CancellationToken cancellationToken)
        {
            var handle = Addressables.CheckForCatalogUpdates(autoReleaseHandle: false);
            while (!handle.IsDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(cancellationToken);
            }
            var catalogs = handle.Result;
            int count = catalogs != null ? catalogs.Count : 0;
            Addressables.Release(handle);
            if (count > 0)
            {
                SafeLogger.Log($"[Boot] カタログ更新あり: {count}件 (ダウンロードはリモートカタログ導入時に実装)");
            }
            return BootTaskResult.Ok();
        }
    }
}
