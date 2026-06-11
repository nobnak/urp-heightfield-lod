# urp-heightfield-lod

[![Unity Editor 上でのサンプル映像](http://img.youtube.com/vi/xsMIZjyObbE/hqdefault.jpg)](https://youtu.be/xsMIZjyObbE)

---

Unity 6 / URP 向けに、正射影カメラで大規模な heightfield を描画するための開発リポジトリです。  
コア機能は UPM パッケージ **`jp.nobnak.heightfield-lod`** として配布可能な形で `Packages/` に置かれています。

## パッケージ概要

| 項目 | 内容 |
| --- | --- |
| パッケージ名 | `jp.nobnak.heightfield-lod` |
| 表示名 | Height Field LOD |
| バージョン | 0.2.6 |
| Unity | 6000.0 |
| 依存 | `com.unity.render-pipelines.universal` 17.x、`jp.nobnak.snoise` 2.3.5 |
| 対象 | Windows（DX11/12, Shader Model 5.0） |

正射影カメラの視野全体を覆う heightfield を、チャンク単位の適応 LOD で描画します。

- チャンク LOD メッシュ（LOD 段ごとに事前構築）
- ラプラシアン曲率 + max reduction による LOD 分類
- GPU 上での分類・ヒステリシス・隣接チャンク制約
- `DrawMeshInstancedIndirect` による間接インスタンス描画
- 高さデータは外部供給（`IHeightFieldSource`）、曲率計算と描画は LOD モジュール側

主な用途は、フルスクリーン heightfield の可視化、メディアアート、動的シミュレーションの表示などです。

## アーキテクチャ

Height → LOD → Draw の 3 段パイプラインで構成されます。

| 段 | 責務 | 主な型 |
| --- | --- | --- |
| 1. Height | 高さ RT を書く | `IHeightFieldSource` |
| 2. LOD | 法線・曲率・分類・インスタンスバッファ | `HeightFieldLodCompute`（`ILodSource`） |
| 3. Draw | チャンクメッシュの間接描画 | `HeightFieldChunkMeshDrawer` |

**N** = Height RT 本数、**K** = LOD Compute 数、**M** = Drawer（描画レイヤー）数。  
K と M は多対多で、同一の `ILodSource` を複数 Drawer が参照して LOD 計算を共有できます。

サンプル（`Assets/Samples/HeightField/`）は任意の `IHeightFieldSource` 実装と `HeightFieldBridge` による Rig 配線を提供し、コア UPM（`jp.nobnak.heightfield-lod`）の契約・LOD・描画を利用します。

### 典型 Rig（シーン構成）

`HeightFieldRig` に `HeightFieldLayoutHost`、`HeightFieldBridge`、Height ソース（例: `SineHeightFieldSource`）、`HeightFieldLodCompute`、`HeightFieldChunkMeshDrawer` を配置します。

フレーム更新は Drawer 起点の **pull** モデルです。`beginCameraRendering` で Height → LOD → Draw の順に `EnsureUpdated` が呼ばれます。

## リポジトリ構成

```text
urp-heightfield-lod/
├── Packages/
│   └── jp.nobnak.heightfield-lod/   # UPM コアパッケージ
│       ├── package.json
│       ├── Runtime/                   # asmdef: HeightFieldLod
│       └── Samples~/                  # UPM サンプル配布用（git 管理）
├── Assets/
│   ├── Samples/HeightField/           # 開発用サンプル（コンパイル時に Samples~ へ同期）
│   ├── Editor/HeightFieldLod.Dev/     # 開発専用（サンプル同期スクリプト）
│   ├── App/                           # デモシーン用ユーティリティ
│   └── Scenes/                        # 検証シーン
└── Doc/                               # 設計ドキュメント
```

### アセンブリ依存

```text
HeightField.Samples.Editor → HeightField.Samples, HeightFieldLod
HeightField.Samples        → HeightFieldLod（契約のみ）
HeightFieldLod             → URP のみ
```

`HeightFieldLod` はサンプルの具象型を知らず、`IHeightFieldSource` 契約のみに依存します。

## パッケージ内部構造

`Packages/jp.nobnak.heightfield-lod/Runtime/` のディレクトリ構成です。

```text
Runtime/
├── Contracts/          # 公開契約
│   ├── IHeightFieldSource.cs   … 高さ RT の確保・更新
│   ├── ILodSource.cs             … LOD 計算結果と描画バッファ
│   └── HeightFieldLayout.cs      … テクスチャ・チャンク・ワールド寸法の単一の真実
├── Layout/
│   └── HeightFieldLayoutHost.cs  … カメラから Layout を生成
├── Compute/
│   └── HeightFieldLodCompute.cs  … 法線・曲率・LOD 分類・インスタンスリスト
├── Draw/
│   ├── HeightFieldChunkMeshDrawer.cs  … DrawMeshInstancedIndirect
│   ├── ChunkMeshBuilder.cs            … LOD 段ごとのチャンクメッシュ構築
│   └── ChunkInstanceData.cs
├── Util/
│   └── HeightFieldSourceUtil.cs  … EnsureUpdated（フレーム単位の重複更新防止）
└── Shaders/
    ├── NormalFromHeight.compute    … 高さから法線 RT
    ├── Curvature.compute           … 曲率計算
    ├── ReductionMax.compute        … max reduction
    ├── ClassifyLOD.compute         … LOD 分類
    ├── NeighborLOD.compute         … 隣接チャンク制約
    ├── HeightFieldLit.shader       … URP Lit 相当
    └── HeightFieldToon.shader      … トゥーンシェーダ
```

### 主要コンポーネント

| コンポーネント | 役割 |
| --- | --- |
| `HeightFieldLayoutHost` | カメラの pixel サイズ・ortho・barrier から `HeightFieldLayout` を生成（layout 専用、描画フィルタには使わない） |
| `HeightFieldBridge` | Rig の初期化（`Allocate` + `Configure`）。layout 変化時に再設定 |
| `IHeightFieldSource` | 高さ RT（`HeightTex`）のライフサイクルと更新 |
| `HeightFieldLodCompute` | GPU 上で法線・曲率・LOD 分類を行い、`ILodSource` として描画データを提供 |
| `HeightFieldChunkMeshDrawer` | `ILodSource` を参照して間接描画。Rig のレイヤーが `cullingMask` に含まれるカメラで描画 |

## サンプル

Package Manager から **Height Field** サンプルをインポートするか、リポジトリ内の `Assets/Samples/HeightField/` を参照してください。

| サンプル | 内容 |
| --- | --- |
| `SineHeightFieldSource` | 正弦波による高さフィールド |
| `MusgraveHeightFieldSource` | Musgrave ノイズによる高さフィールド（`jp.nobnak.snoise` 使用） |
| `HeightFieldBridge` | Rig コンポーネントの配線 |
| `HeightField.unity` | デモシーン |

サンプルインポート後、メニュー **GameObject → Height Field → Setup Sample Rig** で Rig を自動配置できます。

開発時は `Assets/Samples/HeightField/` がコンパイルのたびに `Packages/.../Samples~/` へ同期されます（`Assets/Editor/HeightFieldLod.Dev/`）。

## 設計ドキュメント

詳細なアルゴリズム・座標系・シェーダ仕様は `Doc/` を参照してください。

| ドキュメント | 内容 |
| --- | --- |
| [Doc/heightfield-lod-layered-design.ja.md](Doc/heightfield-lod-layered-design.ja.md) | モジュール境界、コンテキストマップ、ランタイム構成 |
| [Doc/urp-heightfield-lod-design.ja.md](Doc/urp-heightfield-lod-design.ja.md) | 曲率 LOD、チャンクメッシュ、座標系、描画仕様 |
| [Doc/head-sway-lens-shift-camera.md](Doc/head-sway-lens-shift-camera.md) | カメラ行列のみの揺れ（ジオメトリ固定） |

## ライセンス

[LICENSE](LICENSE)（MIT License）を参照してください。
