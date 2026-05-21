# 頭部動揺のカメラシミュレーション — レンズシフトと視点オフセット

> プロジェクト全体: [urp-heightfield-lod-design.ja.md](urp-heightfield-lod-design.ja.md)（[英語](urp-heightfield-lod-design.md)）

## 記号

| 記号 | 意味 | 実装 |
| --- | --- | --- |
| `z_f` | 収束深度（m） | `_focusDistance` |
| `d` | 接線平面変位（m） | `ViewMotion.Evaluate` |
| `d_eye` | 視点平行移動（ローカル） | `(-d.x, -d.y, 0)` |
| `V₀` | 既定ビュー行列 | `Reset` 後の `worldToCameraMatrix` |
| `P` | 投影行列 | `projectionMatrix` |

> 実装: `HeadSwayLensShiftCamera.cs`（駆動）、`ConvergingLensShift.cs`（`P`/`V`）、`ViewMotion.cs`（`d`）

---

## 目的

リグの **Transform は固定**のまま、毎フレーム `P` と `V` を更新する。深度 `z_f` 上は画面上ほぼ静止し、手前・奥だけパララックスが変わる。`ViewMotion` の `d(t)` で頭部の微細な揺れを与える。

---

## なぜ `P` と `V` の二段か

`P` だけでは深度ごとに見え方が変わる。収束面 `z_f` を固定するには、**同じ `d`・同じ `z_f`** で投影ずれと視点平行移動を組む。

- `d_eye = (-d.x, -d.y, 0)`
- 透視: `s_x = d.x · (near / z_f)`、`s_y = d.y · (near / z_f)`
- 正射影: `k_x = d.x / z_f`、`k_y = d.y / z_f`

---

## レンズシフト（`P`）

視空間は **forward = -Z**。`LateUpdate` で物理カメラを切り、既定 `P`/`V` をリセットしてから上書き。

### 透視

```text
P = Frustum(-right+s_x, right+s_x, -top+s_y, top+s_y, near, far)
s_xy = d_xy · (near / z_f)
```

### 正射影

```text
P = Ortho(...) · Shear(k_x, k_y)
k_xy = d_xy / z_f
```

---

## 視点オフセット（`V`）

1. `V₀` = `Reset` 後の既定 `worldToCamera`
2. `d_w = TransformVector(d_eye)`
3. `V = V₀ · Translate(-d_w)`

---

## 頭振り（`d`）

| モード | 内容 |
| --- | --- |
| 円 | 緩い周回 |
| ノイズ | 微細な揺れ |
| 呼吸 | 縦の呼吸・心拍風 |
| 慣性揺れ | バネ・ダンパ＋ノイズ |
| 回転 | 合成後を小角度で回す |

揺れの大きさは **Head sway** の各振幅（m）で調整する。

---

## 1 フレーム

```text
LateUpdate:
  d ← ViewMotion.Evaluate(...)
  ConvergingLensShift.Apply(cam, d, z_f)

Disable:
  ConvergingLensShift.Reset(cam)
```

---

## まとめ

1. **`ConvergingLensShift`** — `d` と `z_f` から `P`・`V` を一括更新。  
2. **`HeadSwayLensShiftCamera`** — `d` の時間合成と `z_f` の保持のみ。  
3. Transform は動かさず **行列のみ** 更新。
