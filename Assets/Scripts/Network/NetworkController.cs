using UnityEngine;
using UnityFramework.Network;

public class NetworkController : NetworkControllerBase
{
    // FIXME : 接続先をconst 定義で参照.
    protected override string BaseUrl { get; }
}
