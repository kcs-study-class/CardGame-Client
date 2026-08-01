using System.Threading;
using UnityEngine;

namespace UnityFramework.SceneManagement
{
    /// <summary>
    /// シーンの「準備処理」の契約。
    /// <see cref="SceneController"/> はシーンロード後、フェードインで画面を見せる前に、
    /// 新しいシーンのルート配下からこのインターフェースを収集して完了まで待つ。
    ///
    /// 用途: Addressables のロード/ダウンロード、UI構築、サーバーからの初期データ取得など、
    /// 「画面を見せる前に終わっているべき処理」をここに書く。
    ///
    /// 実装側の注意:
    /// - 二重呼び出しに耐えること (SceneController 経由と Start フォールバックの両方から呼ばれ得る)
    /// - 失敗してもハングせず戻ること (戻らないとフェードが開かない)
    /// </summary>
    public interface IScenePreparer
    {
        Awaitable PrepareAsync(CancellationToken cancellationToken);
    }
}
