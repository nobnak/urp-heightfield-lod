# 頭部動揺のカメラシミュレーション — レンズシフトと視点オフセット

## 記号

| 記号 | 意味 | 実装での対応例 |
| --- | --- | --- |
| `z_f` | 収束深度（m） | `focusDistance` |
| `u` | 正規化オフセット `∈ ℝ²`（無次元） | シフト駆動の共通入力 |
| `A` | オフセット振幅（m、`u=±1` の目安） | `shiftAmplitude` |
| `d` | 接線平面変位（m） | `ViewMotion.Evaluate` の戻り値 |
| `d_eye` | 視点平行移動（カメラローカル, m） | `viewLocal` |
| `d_w` | 視点平行移動（ワールド, m） | `worldOffset` |
| `V₀` | Transform 由来の既定ビュー行列 | `baseWorldToCamera` |
| `V` | 適用後ビュー行列 | `worldToCameraMatrix` |
| `P` | 投影行列 | `projectionMatrix` |
| `link` | 同じ `u` で `P` と `V` を連動 | `applyStereoMatchedViewTranslation` |

> 実装: `Assets/App/HeadSway/HeadSwayLensShiftCamera.cs`、`Assets/App/ViewMotion/ViewMotion.cs`

---

## 目的

リグの **Transform は固定**のまま、毎フレーム `P` と（`link` 時は）`V` を更新し、深度 `z_f` 上は画面上ほぼ静止、手前・奥だけパララックスが変わるようにする。`ViewMotion` で得た `d(t)` から `u` を作り、**頭部の微細な揺れ** を与える。

---

## なぜ `P` と `V` の二段か

`P` だけ（レンズずらし相当）では深度ごとに見え方が変わる。収束面 `z_f` を固定するには、投影のずれと視点の平行移動を **同じ `u`・同じ `z_f`** で組む。

- `d_eye = (-A·u.x, -A·u.y, 0)`（`link` 時）  
- 透視の視錐ずれ: `w = A·(near/z_f)`、`s_x = u.x·w`、`s_y = u.y·w`

`P` だけ・`V` だけでは `z_f` 面上の静止は保てない。

---

## レンズシフト（`P`）

視空間は **forward = -Z**。`LateUpdate` で物理カメラを切り、既定 `P`/`V` をリセットしてから上書き。

### 透視

```text
top   = near · tan(fovY/2)
right = top · aspect
s_x   = u.x · A · (near / z_f)
s_y   = u.y · A · (near / z_f)

P = Frustum(-right+s_x, right+s_x, -top+s_y, top+s_y, near, far)
```

### 正射影

```text
k_x = u.x · A / z_f
k_y = u.y · A / z_f
P = Ortho(...) · Shear(k_x, k_y)    // x' = x + k_x·z, y' = y + k_y·z
```

`u` は無次元、`A` で m に換算。

---

## 視点オフセット（`V`）

`link` 時:

1. `V₀` = `Reset` 後の既定 `worldToCamera`  
2. `d_eye = (-A·u.x, -A·u.y, 0)` → `d_w = TransformVector(d_eye)`  
3. `V = V₀ · Translate(-d_w)`

`u` の符号は `P` と共有。`link` オフ時は `P` のみ（`z_f` 面上の固定は弱い）。

---

## 収束深度 `z_f`

`P`・`V` の両方に入る。`z_f` が大きいほど同じ `u` に対する `s`・`k` は小さい。シーンに合わせて定数でも、レイピック＋ディオプター平滑でもよい。

---

## 頭振り: `d` → `u`

`ViewMotion` が接線平面の `d`（m）を合成する。

| モード | 内容 |
| --- | --- |
| 円 | 緩い周回 |
| ノイズ | 微細な無意識の揺れ |
| 呼吸 | 縦の呼吸・心拍風 |
| 慣性揺れ | バネ・ダンパ＋ノイズ（`p`,`v` を保持） |
| 回転 | 合成後を小角度で回す（首のわずかなねじれ） |

```text
d ← Evaluate(params, t, dt, state)   // 有効モードは加算、回転は最後
u ← d / A                            // A≈0 に注意
```

### 調整目安

| 現象 | 目安 |
| --- | --- |
| ゆるい周回 | 円、周期 5–8 s、振幅 1–3 cm → `A` でスケール |
| 微揺れ | ノイズまたは慣性揺れ、バネ・減衰で固さ |
| 呼吸 | 呼吸モード（主に `u.y`） |
| 首のねじれ | 回転＋小角度 |

慣性揺れは `dt ← min(dt, 0.05)`。`dt=0` では位置を保持。

---

## 1 フレーム

```text
Update:
  z_f ← 更新（任意）
  d ← ViewMotion.Evaluate(...)

LateUpdate:
  u ← d / A
  z_f ← max(ε, z_f)
  V₀ ← Reset 後の worldToCamera

  if link:
    d_eye ← (-A·u.x, -A·u.y, 0)
    d_w ← TransformVector(d_eye)
    V ← V₀ · Translate(-d_w)

  P ← BuildFrustumOrShear(u, z_f, near, ...)

Disable: Reset V, P
```

`LateUpdate` は他の `Update` 後に行列を確定するため。

---

## まとめ

1. **レンズシフト (`P`)** — 非対称 `Frustum` または `Ortho×Shear`（透視 `∝ near/z_f`、正射影 `∝ 1/z_f`）。  
2. **視点オフセット (`V`)** — `V = V₀·Translate(-d_w)`、`d_eye = (-A·u.xy, 0)`。  
3. **同じ `u` と `z_f`** で `P` と `V` を駆動 → `z_f` 上は画面上ほぼ固定。  
4. **頭振り** — `ViewMotion` で `d`（m）を合成し `u = d/A` として `P`・`V` に渡す。  
5. Transform は動かさず **行列のみ** 更新。
