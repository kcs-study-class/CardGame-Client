# 拡張メソッド

名前空間: `UnityFramework.Extensions`

## NullCheckExtensions

UnityEngine.Object の **fake null** (Destroy 済みオブジェクトが C# 的に null でない状態) を正しく判定します。

```csharp
using UnityFramework.Extensions;

// 純C#オブジェクトもUnity Objectも同じAPIで判定
if (obj.IsNotNull()) obj.DoSomething();
if (obj.IsNull()) return;

// 破棄済みUnityObjectをC#的なnullに変換 (null合体演算子と相性◎)
var go = mayBeDestroyed.OrNull() ?? fallback;
```

| メソッド | 説明 |
|---|---|
| `IsNull<T>()` | null または破棄済みなら true |
| `IsNotNull<T>()` | 生存中なら true |
| `OrNull<T>()` | Unity Object 専用。破棄済みなら C# null を返す |

## GameObjectExtensions

```csharp
using UnityFramework.Extensions;

var rb = go.GetOrAddComponent<Rigidbody>();      // 無ければ追加
if (go.HasComponent<Collider>()) { /* ... */ }
go.DestroyChildren();
go.SetLayerRecursively(LayerMask.NameToLayer("UI"));
go.SetActiveIfNeeded(true);                       // 差分がある場合のみSetActive
```

## ComponentExtensions

```csharp
var rb = component.GetOrAddComponent<Rigidbody>();
var canvas = component.FindInParents<Canvas>();   // 親階層探索
```

## TransformExtensions

```csharp
transform.ResetLocal();                            // localPosition/Rotation/Scale を初期化
transform.DestroyChildren();                       // Play/Edit 両対応 (DestroyImmediate 自動切替)
transform.SetPositionX(5f);
transform.SetPositionY(2f);
transform.SetPositionZ(0f);
transform.SetLocalPositionX(1f);
```

## CollectionExtensions

```csharp
if (list.IsNullOrEmpty()) return;
if (array.IsNotNullOrEmpty()) { /* ... */ }

var element = list.RandomElement();                // ランダム取得
list.Shuffle();                                    // Fisher-Yates シャッフル

items.ForEach(item => item.Process());
items.ForEach((item, index) => item.Process(index));
```

## StringExtensions

```csharp
if (text.IsNullOrEmpty()) return;
if (text.IsNotNullOrEmpty()) { /* ... */ }
if (text.IsNullOrWhiteSpace()) return;
if (text.IsNotNullOrWhiteSpace()) { /* ... */ }
```

## VectorExtensions

```csharp
// 不変的に成分を書き換え (With系)
var v = transform.position.WithY(0f);
var v2 = Vector2.zero.WithX(5f);

// 相互変換
var screen = worldPos.ToVector2();                 // Vector3 -> Vector2 (z捨て)
var world = uv.ToVector3(z: 1f);                   // Vector2 -> Vector3

// 反転
var inv = direction.Inverse();
```
