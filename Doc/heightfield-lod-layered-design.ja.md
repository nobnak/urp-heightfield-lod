# Heightfield LOD — 多レイヤー設計

> 英語版: [heightfield-lod-layered-design.md](heightfield-lod-layered-design.md)  
> ベース: [urp-heightfield-lod-design.ja.md](urp-heightfield-lod-design.ja.md)

## 経緯の整理

| 論点 | 結論 |
| --- | --- |
| Nanite 的か | 任意メッシュ仮想化ではなく、**正射影向け適応 heightfield** |
| 複数レイヤー | 現状は **1 Rig = 1 Height + 1 LOD + 1 Draw** が前提。Transform はほぼ未使用 |
| モード enum | **不要**。App が RT を何本用意するか + レイヤーが **参照** するだけで「同じ/別 HF」を表現 |
| パイプライン | **3 段階**に分離: (1) Height 計算 (2) 曲率/LOD (3) Chunk Mesh 描画 |
| `h` の座標 | 設計意図は **オブジェクト空間 -Z**。実装は layout 由来の **ワールド XY + ワールド -h** で、Rig/Camera が identity のときだけ一致 |
| 深度・重ね | 層の前後は **レイヤー Transform**（剛体）+ 同一 HF なら同じ `h`。**view 深度**として解釈するのが表示として自然 |
| LOD 閾値 | **HeightTex ごとに同一**（シーン内で可変にしない）。Compute のパラメータ差による分岐は作らない |
| LOD 共有 | 同じ HeightTex を使う場合は **ILodSource 参照で共有**（キャッシュは不要） |
| `GetData` | `HeightFieldLodRenderer.BuildInstanceLists` 内 **1 箇所のみ**。同一 HeightTex の複数レイヤーでは 1 回/フレームに抑える |

---

## 目的

- シーン上の **複数レイヤー** を、各 GameObject の **Transform** で空間・深度関係を表現する。
- Height は **アプリが 1 本または複数** 書き込む。レイヤーは **自分で計算** するか **他レイヤー（ILodSource）を参照** するだけ。
- 既存の Chunk LOD・曲率分類・間接描画を維持しつつ、**座標系のバグ（ワールド直書き）を正す**。

## 非目的（初期スコープ外）

- Nanite 的クラスタ階層・メッシュストリーミング
- レイヤーごとに異なる `HeightFieldLayout`（解像度・カメラ）
- GPU バケット化による `GetData` 完全排除（将来拡張として記載）

---

## アーキテクチャ（採用モデル）

**N 本の Height、K 個の LOD Compute、M 個の Drawer（M ≥ 1, K ≥ 1）**。  
参照は「使う側が持つ」を徹底し、**Drawer は `ILodSource` を共有**できる。Compute→Compute の参照は不要。

```text
App
  └─ HeightTex × N          (N ≥ 1)

HeightFieldLodCompute × K
  入力:
    · RenderTexture _height   （必須）
  出力:
    Normal RT, Curvature, LodBuffer, InstanceBuffers+Args

HeightFieldChunkMeshDrawer × M
  入力:
    · ILodSource _lod              （必須・Compute を参照。複数 Drawer で共有可）
    · Material, Camera, Transform
  描画:
    _lod.HeightTex を VS でサンプル、_lod の instance buffers で间接 draw
```

| 記号 | 意味 |
| --- | --- |
| **N** | アプリが生成・更新する Height RT の本数 |
| **K** | シーン上の LOD Compute 数（HeightTex ごとに 1 つ、または任意） |
| **M** | シーン上の Drawer 数（レイヤー数） |

典型配置: 1 GameObject = `HeightFieldChunkMeshDrawer`（Transform と見た目）。LOD Compute は別に置いて参照する。  
Compute と Drawer の対応は **K: M**（多対多）で、**同一 Compute を複数 Drawer が共有**する。

### 参照パターン

| パターン | 参照 | コスト |
| --- | --- | --- |
| A. Height ごとに Compute 1 つ | Compute が `_height` を保持 | HeightTex 数に比例 |
| B. 同じ Compute を複数 Drawer が参照 | Drawer が同じ `ILodSource` を共有 | **LOD 計算は 1 回、描画だけ M 回** |
| C. 同じ HeightTex に Compute を複数置く | それぞれ `_height` 同一 | 余計に計算（許容はするが非推奨） |

### 3 段階の責務

| 段階 | コンポーネント | 入力 | 出力 |
| --- | --- | --- | --- |
| **1** | App（`IHeightFieldSource` 等） | `HeightFieldLayout` | `HeightTex` × **N** |
| **2** | `HeightFieldLodCompute` × **K** | `_height` | GPU LOD 成果物（上表） |
| **3** | `HeightFieldChunkMeshDrawer` × **M** | `_lod` | `DrawMeshInstancedIndirect` |

**モード enum なし。** Drawer が `ILodSource` を参照するだけ。

### 参照の方向（依存と更新の設計）

参照は常に「使う側が持つ」＝ **フォワード参照**に統一する。

- `HeightFieldChunkMeshDrawer` → `ILodSource` のみ
- `HeightFieldLodCompute` は他 Compute を参照しない（依存関係を作らない）

フレーム更新の制御は **pull** を基本とする（Drawer が描画直前に `EnsureUpdated` を呼ぶ）。  
これにより Host/レジストリが不要になる。

| 制御 | 呼び出しの向き | 特徴 |
| --- | --- | --- |
| pull | Drawer が `ILodSource.EnsureUpdated(frameId)` を呼ぶ | **最小構成**。参照が明示で循環なし |
| push | Host が Compute を Tick、Drawer は描画のみ | 大規模化したときの一元管理用（任意） |

### オーケストレーション（`HeightFieldLayoutHost`）

Stack/Layer という名前ではなく、**Layout + フレーム順序**だけを持つ薄いホスト（任意・1 シーン 1 つ推奨）:

```text
1. Layout.Rebuild?  (camera pixel / ortho)
2. App → 各 HeightTex 更新
3. （push 型を採る場合のみ）Compute を Tick
4. Drawer を sort key 順に Draw（beginCameraRendering）
```

※ ルート強制はしない。同一 HeightTex に Compute を複数置けば重複計算になるが、参照で共有すれば重複は起きない。

### Drawer と Compute の 1:1 について

`HeightFieldLodCompute` : `HeightFieldChunkMeshDrawer` は **K : M** が基本。  
複数 Drawer が同一 Compute を参照してよい（むしろそれが共有の本体）。Compute 単体（デバッグ用）も可。

---

## 座標系と `h` の適用

### 設計原則（Unity Quad 継承）

| 項目 | 規約 |
| --- | --- |
| 平面 | レイヤー **ローカル XY** |
| 法線 | **-Z**（ローカル） |
| 高さ | `P_os = (x_os, y_os, z_skirt - h(uv))`、**h はメートル** |
| ワールドへ | `P_ws = TransformObjectToWorld(P_os)` |
| クリップへ | `P_cs = TransformWorldToHClip(P_ws)` |

**禁止（現状のショートカット）:** layout から **ワールド XY を直書き**し、**ワールド Z に -h** するだけの経路。

### なぜ現状が「たまたま」動くか

- Rig **rotation = identity**、カメラ **rotation = identity** のとき、ローカル -Z = ワールド -Z、layout のワールド XY = ローカル XY に一致。
- Rig の Transform を回しても **今の VS は h をワールド -Z のまま** → カメラ向き配置の意図とずれる。

### Layout と Transform の役割分担

| データ | 空間 | 説明 |
| --- | --- | --- |
| `HeightFieldLayout` | 共有（Stack または参照カメラから生成） | テクセル数、チャンク数、`PixelWorldX/Y`。**全レイヤー同一**を前提 |
| `ChunkInstanceData` | **レイヤーローカル** | チャンク中心・スケールを **ローカル XY** で保持（ワールド直書きしない） |
| `Transform` | レイヤーごと | 位置・回転・スケール。多レイヤーの **空間オフセット・板の向き** |
| `h` | テクセルスカラー | 形状。同一 `HeightTex` 参照 → **同一形状** |

### カメラ・view 深度について

- **h** は「像平面に垂直な厚み」として **オブジェクト -Z** に載せるのが第一義（Rig をカメラ向きに置けば視線方向の凹凸になる）。
- **層の前後**は主に **Transform の位置**（特にカメラから見た深度）。同一 HF・同一 UV で Transform もほぼ同じ → 面が重なる（Z-fight）。**別 Transform / 別 HF / 描画順**で解決。
- **頭振り**（`worldToCameraMatrix` / `projectionMatrix` のみ変更）: ジオメトリはワールド（レイヤー）固定、カメラが動く — 現行設計と同様。**view 空間で h を足す**方式は、カメラ回転と「板固定」を両立したい場合の将来オプション（初期は OS -Z + ObjectToWorld で十分）。

### 法線・曲率 Compute

- テクセル格子は layout の **ワールド軸に整列したスカラー場** として扱う（`NormalFromHeight` / `Curvature` の勾配は texel ↔ `PixelWorldX/Y`）。
- レイヤー **回転** 時: 法線 RT は「layout 軸上の勾配」で計算し、Draw で `normalOS` 相当を **ObjectToWorld の 3×3** でワールドへ回すか、VS で `TransformObjectToWorldNormal` を使う（実装時にどちらかに統一）。

---

## コンポーネント詳細

### App（段階 1）× N

既存 `IHeightFieldSource` を N 個（または 1 個が 1 RT を書き、他は参照専用）。  
`Allocate(layout)` / `UpdateHeight(layout, time)` → `HeightTexture`。

### `HeightFieldLodCompute`（段階 2）× M

| フィールド | 説明 |
| --- | --- |
| `_height` | ルート入力。`_lodSource` と **排他** |
| `_lodSource` | 別 Compute の成果物をそのまま使う（パターン C/D） |
| `_curvatureScale`, LOD 閾値 | **HeightTex ごとに同一**（シーン内で可変にしない）。`_lodSource == null` のときのみ参照される |
| `LayoutHost` | 省略時はシーンの Host から layout 取得 |

`HeightFieldLodCompute` は `ILodSource` を実装する。  
**公開プロパティ（Drawer 用）:** `HeightTexture`, `NormalTexture`, `InstanceBuffers`, `ArgsBuffers`, `Meshes`（skirt 込み LOD meshes は共有アセット）。

**Tick:** ルートなら Normal→Curvature→Classify→Neighbor→`GetData`→instances。委譲ならソースの Tick 済みを前提に参照のみ。

### `HeightFieldChunkMeshDrawer`（段階 3）× M

| フィールド | 説明 |
| --- | --- |
| `_lod` | 対応する `ILodSource`（必須） |
| `_material` | Lit / Toon |
| `_camera` | 省略時は Host のカメラ |
| `_sortOrder` | 同一カメラ内の描画順 |

**Transform:** この GameObject（または親）の `localToWorld` で OS の `h` とチャンク配置を変換。

### `HeightFieldLayoutHost`（任意・推奨）

| 責務 | 内容 |
| --- | --- |
| Layout | `FromCamera`、全 Compute/Drawer で **同一 layout** を共有 |
| Registry | （任意）同一 HeightTex に対するルート Compute の集約。**キャッシュは不要** |
| 順序 | §オーケストレーション |

`HeightFieldBridge` は Host + 単一 Compute + 単一 Drawer への移行用ラッパ。

### `ILodSource`（新規インターフェース案）

`IHeightFieldSource` と同様に「参照先の型」を統一するためのインターフェース。

```csharp
public interface ILodSource
{
    RenderTexture HeightTexture { get; }
    RenderTexture NormalTexture { get; }
    ComputeBuffer[] InstanceBuffers { get; }
    ComputeBuffer[] ArgsBuffers { get; }
    Mesh[] LodMeshes { get; }

    // pull 制御を採る場合（推奨）
    void EnsureUpdated(in HeightFieldLayout layout, int frameId);
}
```

`HeightFieldLodCompute`（ルート/委譲）は `ILodSource` を実装し、Drawer は `ILodSource` のみを参照して描画する。

---

## フレームパイプライン

```text
Host:
  1. layout.Rebuild? (pixelW/H, orthoSize)
  2. App: UpdateHeight → HeightTex[0..N-1]
  3. ルート LodCompute をユニークキーで Tick（キャッシュ hit なら GPU スキップ）
  4. _lodSource のみの Compute は順序付け後に委譲解決（追加 GPU なし）
  5. beginCameraRendering:
       foreach Drawer (sortOrder):
         DrawMeshInstancedIndirect(_lod, transform, material, camera)
```

### 描画順

| 方式 | 用途 |
| --- | --- |
| `sortingOrder` / `RenderQueue` on material | 同一深度付近の重なり |
| Stack が Layer リスト順 | 奥 → 手前 |
| Transform Z（OS → WS） | 物理的な層間隔 |

---

## `GetData` と共有

**現状:**

```csharp
// HeightFieldLodRenderer.BuildInstanceLists
_lodBuffer.GetData(_lodData);
```

**共有時:**

- 同一 `HeightFieldLodCache` を参照する N レイヤー → **`GetData` は 1 回/フレーム**
- Draw は N 回（Material / Transform / Queue が異なる）

**将来:** append/consume で GPU バケット化 → 設計上の `BuildInstanceLists` を Cache 内に閉じたまま差し替え。

---

## シェーダ変更（Draw）

`HeightFieldLitCommon.hlsl` の `HFVert` 案:

```hlsl
// 1) プロシージャル: チャンク local XY (+ skirt z in mesh)
float2 localXY = (v.positionOS.xy - 0.5) * _LocalScaleCenter.xy + _LocalScaleCenter.zw;
float2 heightUv = v.uv * _UvScaleOffset.xy + _UvScaleOffset.zw;
float h = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, heightUv, 0).r;

// 2) オブジェクト空間（設計どおり）
float3 posOS = float3(localXY.x, localXY.y, v.positionOS.z - h);

// 3) ワールド・クリップ
float3 posWS = TransformObjectToWorld(posOS);
o.positionWS = posWS;
o.positionCS = TransformWorldToHClip(posWS);
```

`ChunkInstanceData`: `WorldScaleCenter` → `LocalScaleCenter`（layout チャンク矩形を **親 Transform のローカル** で表現。Stack 原点配置時は従来と同値）。

---

## 既存コードとの対応

| 現状 | 移行後 |
| --- | --- |
| `HeightFieldBridge` | `HeightFieldLayoutHost` + 1 Compute + 1 Drawer |
| `HeightFieldLodRenderer` | `HeightFieldLodCompute` + `HeightFieldChunkMeshDrawer` |
| `ChunkInstanceData` | ローカル中心 + Drawer の Transform |
| 単一 Rig メニュー | Host + Compute/Drawer ペア |

**互換:** N=1, M=1, identity Transform → 見た目は現行に近い（OS 経由に修正）。

---

## 実装フェーズ

| Phase | 内容 | 成果 |
| --- | --- | --- |
| **0** | 設計書（本ドキュメント） | 合意 |
| **1** | VS + `ChunkInstanceData` を OS 化。単一レイヤーで回帰確認 | Transform が効く |
| **2** | `HeightFieldLodCompute` / `Draw` 分割。Cache + `GetInstanceID` キー | 同一 HF の二重 LOD 排除 |
| **3** | Host + Compute/Drawer × M（M≥2） | 多レイヤー・参照 |
| **4** | 法線の回転対応、描画順、Editor セットアップ | 実用 |

---

## テスト観点

| ケース | 期待 |
| --- | --- |
| 1 レイヤー、identity | 現行と同等の見た目 |
| 2 レイヤー、同一 HeightTex、Transform Z オフセット | 平行な 2 面、Z-fight なし |
| 2 レイヤー、別 HeightTex | 独立した形状 |
| 2 レイヤー、同一 HeightTex、`_lodCompute` 共有 | Profiler で Curvature/Classify 1 回、`GetData` 1 回 |
| Rig Yaw 45° | h がローカル -Z に沿って傾く（Phase 1 後） |
| Head sway | 板固定・カメラのみ動く（現行同等） |

---

## 未決・将来

- レイヤーごとに LOD 閾値だけ変えたい → キャッシュキーに閾値を含める（共有しない）
- view 空間 h（カメラ回転と板の関係を変えたい場合）
- Clipmap / geomorph
- UPM 化

---

## 関連ドキュメント

- [urp-heightfield-lod-design.ja.md](urp-heightfield-lod-design.ja.md) — 単体 Rig・曲率 LOD・Quad 規約
- [head-sway-lens-shift-camera.md](head-sway-lens-shift-camera.md) — 行列のみのカメラ変化
