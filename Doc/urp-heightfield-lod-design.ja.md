# urp-heightfield-lod

> 英語版: [urp-heightfield-lod-design.md](urp-heightfield-lod-design.md)

## 概要

正射影カメラ向けの URP 適応型 Heightfield レンダリング:

- Chunk LOD Mesh（LOD 段階ごとの事前生成メッシュ）
- 曲率駆動 LOD（ラプラシアン + max リダクション）
- GPU 分類、ヒステリシス、隣接チャンク制約
- インスタンス間接描画（`DrawMeshInstancedIndirect`）
- 高さは外部供給、曲率・描画は LOD モジュール側

主な用途: フルスクリーン Heightfield 可視化、メディアアート、動的シミュレーション表示。

多レイヤー・コンテキストマップ・全体構成: [heightfield-lod-layered-design.ja.md](heightfield-lod-layered-design.ja.md)（アルゴリズム詳細は本書）

対象プラットフォーム: **Windows**（DX11/12、Shader Model 5.0）。

---

# 座標系（Unity Quad）

公式 Quad の規約に従う（[Create a quad mesh via script](https://docs.unity3d.com/6000.0/Documentation/Manual/Example-CreatingaBillboardPlane.html)）。

| 項目 | 規約 |
| --- | --- |
| 平面 | ローカル **XY** |
| 表面前方向の法線 | **-Z**（`-Vector3.forward`） |
| 巻き順 | 視空間で時計回り（法線 -Z 側がカメラに見える面） |
| 既定カメラ | **+Z** 向き（例: 位置 `(0,0,-d)`、回転 identity） |
| 高さ変位 | 法線方向 → **-Z**（`HeightTex` からメートル） |
| Skirt | **+Z** のみ（法線の反対）、ワールド深さで押し出し、エッジ頂点は `HeightTex` をサンプル |

```text
        +Y
         |
         |
    -----+----- +X
        /
       /
     +Z  (skirt はこちらへ)

カメラは -Z 側、forward = +Z、法線 -Z の面が見える。
```

ローカル空間での Heightfield 位置:

```math
P_{local} = (x,\ y,\ -h(x,y))
```

`h` は `HeightTex` の R チャンネル（**メートル**）。

手続きメッシュで `Mesh.RecalculateNormals()` は使わない。頂点法線はシャドウバイアス用に **-Z** のまま。

**ライティングと DepthNormals** は、高さ勾配から求めた GPU **法線マップ**（ワールド空間、[法線マップ](#法線マップ高さから生成)）を使う。迷ったら Scene View で Unity **Quad** と向きを比較する。

---

# プロジェクト構成（初期）

`Assets/` 内をフォルダ + asmdef で分割。安定後に UPM パッケージ化。

```text
Assets/
  HeightFieldLod/        # asmdef: HeightFieldLod（契約 + LOD + 描画）
    Contracts/             # IHeightFieldSource, HeightFieldLayout, ILodSource
    Layout/ Compute/ Draw/ Util/ Shaders/
  Samples/HeightField/   # asmdef: HeightField.Samples（サンプル Height 実装）
  App/                   # asmdef: App + App.Editor
Doc/
```

| アセンブリ | 責務 |
| --- | --- |
| `HeightFieldLod` | 契約（`IHeightFieldSource`, `HeightFieldLayout`）、曲率/LOD/描画、シェーダ |
| `HeightField.Samples` | サンプル Height 実装（Sine / Musgrave） |
| `App` | Bridge、任意で `HeadSwayLensShiftCamera` + `ViewMotion` |

エディタメニュー: **GameObject → Height Field → Setup Sample Rig**。

---

# HeightFieldLayout（単一の真実の源）

**`HeightFieldLod`**（`Contracts/`）で定義。**Bridge**（`HeightFieldBridge`）が生成し、Height / LOD に渡す。

```csharp
struct HeightFieldLayout
{
    int BarrierChunks;           // B、各辺のチャンク数（Bridge Inspector）
    int CoreWidth, CoreHeight;   // Align32(camera.pixelWidth/Height)
    int TexWidth, TexHeight;
    int ChunkCountX, ChunkCountY;
    float TotalWorldWidth, TotalWorldHeight;
    float PixelWorldX, PixelWorldY;
}
```

### サイズ

```text
chunkPixelSize = 32
coreW = AlignUp(camera.pixelWidth,  32)
coreH = AlignUp(camera.pixelHeight, 32)
texW  = coreW + 2 * B * chunkPixelSize
texH  = coreH + 2 * B * chunkPixelSize
chunkCountX = texW / 32
chunkCountY = texH / 32
```

`HeightTex`、曲率 RT、チャンクグリッドはすべて同じ **`texW × texH`**（バリア込み）。

### ワールド範囲（中心原点）

```text
coreWorldW = 2 * orthographicSize * aspect
coreWorldH = 2 * orthographicSize
pixelWorldX = coreWorldW / camera.pixelWidth
pixelWorldY = coreWorldH / camera.pixelHeight
totalWorldW = texW * pixelWorldX
totalWorldH = texH * pixelWorldY
```

チャンク `(ix, iy)` の中心（グリッド原点 = ワールド中心）:

```text
chunkWorldW = 32 * pixelWorldX
chunkWorldH = 32 * pixelWorldY
centerX = -totalWorldW/2 + (ix + 0.5) * chunkWorldW
centerY = -totalWorldH/2 + (iy + 0.5) * chunkWorldH
```

UV タイル:

```text
uvOffset = (ix * 32 / texW, iy * 32 / texH)
uvScale  = (32 / texW, 32 / texH)
```

チャンク内メッシュのローカル座標: XY 上で `x,y ∈ [0,1]`。頂点シェーダが `ChunkInstanceData` でワールドへマップ。

### コアとバリア

| 領域 | 説明 |
| --- | --- |
| **コア** | 中央の `coreW × coreH` テクセル（シミュレーション / 主領域） |
| **バリア** | 各辺 `B` チャンク分の外周。同じ Height RT、**コアと同じ LOD ルール** |
| **Height VS** | `clamp` サンプラー（変位用に境界高さを延長） |
| **曲率** | 境界は別ルール（下記）— ラプラシアンに clamp を流用しない |

**バリア LOD 上限は設けない**（`min(lod, barrierMaxLod)` などは使わない）。最適化のために外周を粗 LOD に固定せず、曲率・分類側で境界アーティファクトを解消する。

---

# Transform / カメラ

| ルール | 内容 |
| --- | --- |
| Renderer Transform | エディタでシーン原点（またはカメラ中心）に **一度だけ** 配置。**実行時追従なし** |
| リビルド条件 | `pixelWidth`、`pixelHeight`、`orthographicSize` |
| リビルドしない | `lensShift`、位置・回転、カスタム `projectionMatrix` / `worldToCameraMatrix`（頭振りなど） |
| LOD 指標 | 曲率 / 複雑度 — **カメラ距離ではない** |
| 描画カメラ | `CameraType.Preview` 以外で、Rig の **レイヤー**（`gameObject.layer`）が `cullingMask` に含まれるカメラすべて |
| 頭振り（任意） | 正射影カメラに `HeadSwayLensShiftCamera`。リグ Transform 固定で `ConvergingLensShift` が行列更新 — [頭部動揺カメラ](#頭部動揺カメラ任意) |

---

# ジオメトリ: Chunk LOD Mesh

| LOD | 1 辺あたりセグメント（クアッド） | 1 辺あたり頂点数 |
| --- | --- | --- |
| LOD0 | 32 | 33 |
| LOD1 | 16 | 17 |
| LOD2 | 8 | 9 |
| LOD3 | 4 | 5 |

- チャンクのスクリーン/ワールド占有: チャンクあたり **32×32 px** 固定。
- 共有メッシュ 4 種（`ChunkMeshBuilder`）、`IndexFormat.UInt32`。
- 格子は整数 `(gx, gy)` で構築。Skirt は明示的な辺ループ（UV の `RoundToInt` は使わない）。
- **Skirt**: 境界を **+Z** に `skirtDepthMeters` だけ押し出し。Skirt 頂点のローカル `z = skirtDepth`。VS で全頂点が Height をサンプル。
- **隣接制約**: 1 パス、4 近傍、`|lod_i - lod_j| <= 1`。

---

# Height Field（`HeightField.Samples` / 外部実装）

- 契約 `IHeightFieldSource` は **`HeightFieldLod/Contracts`** で定義。
- `RenderTexture`: `RFloat`、サイズ = `layout.TexWidth × layout.TexHeight`。
- `HeightField.Samples`: `SineHeightFieldSource`, `MusgraveHeightFieldSource`。
- 外部シミュレーションも同契約を実装。Bridge が `HeightFieldLodCompute` / `HeightFieldChunkMeshDrawer` へ接続。

毎フレームの更新順（Bridge `Update`）:

```text
1. ピクセルサイズまたは orthoSize 変化時にリビルド
2. IHeightFieldSource.UpdateHeight(layout, time)   // ComputeShader.Dispatch
3. HeightFieldLodCompute.EnsureUpdated(layout, height)（Drawer が pull）
```

---

# 曲率（`HeightFieldLod`）

### ラプラシアン Compute

`HeightTex`（メートル）上の 3×3 離散ラプラシアン:

```text
lap = |4h - h(-1,0) - h(+1,0) - h(0,-1) - h(0,+1)| * scale
scale = curvatureScale / (pixelWorldX)²
```

### 境界サンプル（重要）

ラプラシアンのステンシル近傍に **clamp を使わない**。テクスチャ端で clamp すると境界テクセルが隣として複製され、`x=0` や `x=texW-1` 付近でラプラシアンが **過大** になりやすい。

最外周バリアチャンクはその端テクセル上に乗るため、チャンク内 max 曲率がほぼ常に最大 → **LOD0 固定** になっていた。

範囲外ステンシルオフセットには **ミラー座標** を使う:

```text
MirrorCoord(p + offset, maxP)
  x < 0        → x = -x
  x > maxP.x   → x = 2*maxP.x - x
  （y も同様）
```

Height **描画** は clamp のまま。ミラーは曲率パスのみ。

### 曲率 RT の初期化

確保 / リビルド時に曲率（およびリダクションミップ）をゼロクリア。

---

# LOD 分類

### チャンクごとの指標

各チャンクの `32×32` テクセルブロックでラプラシアンの **max** を取る。

**テクスチャ境界 1px**（`x==0`、`y==0`、`x==texW-1`、`y==texH-1`）は max から **除外**。ミラー化後も端のスパイク 1 つで最外周リングが LOD0 になるのを防ぐ。

有効テクセルが無い場合（退化）はチャンク中心テクセルにフォールバック。

### 閾値（調整用プレースホルダ）

```text
LOD0: curvature > 0.7
LOD1: 0.4 .. 0.7
LOD2: 0.15 .. 0.4
LOD3: < 0.15

ヒステリシス（細かくするには粗くするより高い指標が必要）:
  細分化: metric < DownThreshold(prevLod) なら据え置き
  DownThreshold: LOD0→0.6, LOD1→0.45, LOD2→0.12
```

初回フレーム: `_PrevLod` は全チャンク **LOD3**（最粗）。

### リダクションピラミッド

毎フレーム **max** で解像度半分のチェーンを生成。将来拡張用。**分類はフル解像度曲率** をチャンク 32×32 ループで max。

平均のみのリダクションは避ける。

---

# 隣接 LOD 制約

`|lod_i - lod_j| <= 1`（4 近傍、**1 パス**）。

### ピンポンバッファ（必須）

**同一 `_Lod` バッファを 1 dispatch で読み書きしない。** インプレース更新は GPU レースを起こし、LOD が非決定的になり、時間経過で **歯抜け / チャンク欠落** になる。

```text
Classify:  _PrevLod → _LodBuffer
Neighbor:  _LodIn = _LodBuffer  →  _LodOut = _LodScratch
           swap(_LodBuffer, _LodScratch)   // 結果は _LodBuffer
```

neighbor パスで `clamp(lo, hi)` する前に、近傍 min/max で `lo > hi` なら入れ替える。

---

# LOD パイプライン（毎フレーム）

```text
1. HeightTex 更新           (IHeightFieldSource / Samples)
2. 法線マップ生成             (NormalFromHeight.compute、ミラー境界)
3. 曲率 Compute             (ミラー境界)
4. max リダクションピラミッド  (任意 / 将来)
5. LOD 分類                   (境界テクセルを max から除外)
6. 隣接制約                   (LodIn → LodOut、スワップ)
7. GetData + LOD 別インスタンス振り分け (CPU、暗黙 GPU 同期)
8. LOD を _PrevLod にコピー   (次フレームのヒステリシス)
9. 描画                       (beginCameraRendering)
```

### インスタンスリストと描画

- GPU パス後、`_LodBuffer.GetData` で CPU `_lodData` を取得（GPU 完了を待つ）。
- LOD ごとに `ChunkInstanceData` リストを組み、`ComputeBuffer` にアップロード。
- 間接 args（`indexCount`、`instanceCount`）は CPU `_argsCpu` に保持 — 描画コールバックで args を **GetData しない**。
- `_ChunkInstances` は `MaterialPropertyBlock` で渡す（LOD 間で Material の buffer 状態を共有しない）。
- `MaterialPropertyBlock` はフィールド初期化子ではなく **`Awake` で生成**（Unity の構築ルール）。

### 手続きインスタンシングシェーダ

- インスタンスあたり float4×2 の `StructuredBuffer`（`worldScaleCenter`、`uvScaleOffset`）。
- `#pragma instancing_options procedural:SetupProcedural`
- シェーダにグローバル `struct` インスタンス変数を置かない（D3D11 制限）。

---

# GPU: ChunkInstanceData

```csharp
struct ChunkInstanceData
{
    float4 WorldScaleCenter; // xy = チャンクのワールドサイズ, zw = ワールド中心 xy
    float4 UvScaleOffset;    // xy = UV スケール, zw = UV オフセット
}
```

LOD 番号はどの draw / バッファかで暗黙的に決まる。

---

# 法線マップ（高さから生成）

毎フレーム、曲率の前に `HeightFieldLodCompute` が **`NormalFromHeight.compute`** を `HeightTex` と同サイズの `ARGBHalf` RT（`_NormalTex`）へ dispatch する。

### 勾配 → ワールド法線

テクスチャ **+Y = ワールド +Y**。ワールド位置は `z = -h`。カメラ側（-Z）の表層外向き法線:

```text
∂h/∂x ≈ (hR - hL) / 2
∂h/∂y ≈ (hN - hS) / 2   （hS = 小さい tex y、hN = 大きい tex y）

n ∝ (-∂h/∂x · pixelWorldY, -∂h/∂y · pixelWorldX, -pixelWorldX · pixelWorldY)
```

エンコード: `normalRT = n * 0.5 + 0.5`。ステンシル用 Height は **ミラー**（曲率と同系）。

**符号:** Y 成分は **`-∂h/∂y`** 必須。`+∂h/∂y` だとワールド Y 向きライトだけ破綻（例: 下向き光で +Y 斜面が誤って Shadow）。X だけ正しく見えることがある。

### シェーダでの利用

`HeightFieldLitCommon.hlsl`:

- VS: `_HeightTex`（clamp）で `positionWS.z -= h`
- FS / DepthNormals / ShadowCaster バイアス: `SampleHeightFieldNormalWS(heightUv)` — RT を ×2−1 して normalize。**ライト計算にメッシュ法線 -Z は使わない**

---

# シェーディング（URP）

| シェーダ | パス |
| --- | --- |
| `HeightFieldLit` | URP `LightingPhysicallyBased`（簡易 PBR） |
| `HeightFieldToon` | `diffuse = saturate(N·L) * 減衰 * シャドウ`、`lerp(ShadowColor, LightColor, diffuse)` |

共通: `HeightFieldLitCommon.hlsl`、手続きインスタンス、`_HeightTex` + `_NormalTex`。

| Pass | LightMode | 用途 |
| --- | --- | --- |
| ForwardLit | `UniversalForward` | カラー |
| DepthNormals | `DepthNormals` | `_NormalTex` 由来のワールド法線 |
| ShadowCaster | `ShadowCaster` | 変位後位置、高さ法線でバイアス |

メインライト: `GetMainLight`（シャドウあり時は shadow coord）。平行光で `distanceAttenuation < 0.5` のとき **1 にクランプ**。間接光・追加ライトは未実装。

---

# 描画（URP）

| 項目 | 内容 |
| --- | --- |
| フック | `RenderPipelineManager.beginCameraRendering` |
| 描画 | LOD メッシュごとに `Graphics.DrawMeshInstancedIndirect` |
| カメラ | Preview 以外で Rig レイヤーをマスクに含むカメラ。シャドウパスも同条件 |
| 深度 | 書き込み ON |
| マテリアル | `HeightFieldChunkMeshDrawer` に Lit または Toon |
| 避ける | ジオメトリシェーダ、CPU メッシュ再構築、ランタイムトポロジ、HW テッセレーション |

---

# 頭部動揺カメラ（任意）

正射影カメラに **`App.HeadSway.HeadSwayLensShiftCamera`** を付ける。リグ Transform は動かさず、収束深度 `z_f` 付近を画面上で保ちつつ `d` でパララックスだけ変える。

| 要素 | 役割 |
| --- | --- |
| `ViewMotion` | 接線平面変位 `d`（m）を合成（円・ノイズ・呼吸・慣性揺れ・回転） |
| `ConvergingLensShift` | `d` と `z_f` から `V`（視点）と `P`（非対称視錐 / 正射影シア）を更新 |
| `_focusDistance` | 収束深度 `z_f`（m） |

詳細: [head-sway-lens-shift-camera.md](head-sway-lens-shift-camera.md)。

`HeightFieldBridge` のリビルド条件には **含めない**（行列のみの変更）。

---

# クラック防止（初期）

1. Skirt（+Z、Height サンプル）
2. LOD 隣接差 ≤ 1（ピンポン neighbor パス）
3. （将来）stitch / geomorph

---

# 推奨初期値

| パラメータ | 値 |
| --- | --- |
| チャンクピクセルサイズ | 32 |
| バリアチャンク数 `B` | 2（Bridge Inspector） |
| LOD 段数 | 4 |
| Skirt 深さ | 約 0.5–2.0 m（調整可） |
| 曲率スケール | 1.0（閾値と併せて調整） |
| Compute スレッドグループ | 8×8 |
| 初回フレーム LOD | 3 |
| Rig レイヤー | Bridge / LOD オブジェクトに設定。このレイヤーを描画するカメラだけが Heightfield を描く |

---

# 実装メモ

### 解決済みの問題（参照）

| 症状 | 原因 | 対処 |
| --- | --- | --- |
| 時間経過で歯抜け | 隣接 LOD のインプレース GPU レース | LodIn / LodOut + バッファスワップ |
| 最外周バリアが常に LOD0 | 端での clamp ラプラシアン + max がスパイクを拾う | ミラーステンシル、分類 max から境界テクセル除外 |
| LOD1（16×16）が出ない | 曲率スケールを `/(pixelWorld)²` と二重適用 | Compute で `scale / dx²` を 1 回だけ |
| コンパイル: `unity_InstanceID` | 手続きインスタンス variant 未定義 | `SetupProcedural` 内で `#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED` |
| コンパイル: グローバル struct `_Instance` | D3D11 シェーダ制限 | float4 個別 + バッファ参照 |
| `MaterialPropertyBlock` 生成エラー | MonoBehaviour のフィールド初期化 | `Awake` で生成 |
| Y 向きライトで +Y 斜面だけ Shadow が誤る、X は正常 | 法線 Y が `+∂h/∂y`（`z=-h` では `-∂h/∂y` が正） | `NormalFromHeight`: `n.y = -dhdy * px` |
| `Camera` のコンパイルエラー | 名前空間 `App.Camera` が `UnityEngine.Camera` を隠す | `App.HeadSway` に改名 |

### 使用しないもの（プラットフォーム / API 制約）

- `Graphics.WaitForAllAsyncGPUOperations`（本プロジェクトの Unity API では未提供）
- `ComputeBuffer` 間の `Graphics.CopyBuffer`（バッファスワップで代替）
- ハードウェア / hull-domain テッセレーション
- バリア専用 LOD 上限（最適化案のみ、未採用）

---

# 将来の拡張

- `GetData` を避ける GPU インスタンス振り分け（append/consume）
- より厳密な収束のための隣接制約マルチパス
- 分類でのリダクションミップ利用
- GPU カリング、時間安定化、非同期 Compute
- Clipmap、geomorph / stitch
- UPM パッケージ分割
- 動的シミュレーション連携
