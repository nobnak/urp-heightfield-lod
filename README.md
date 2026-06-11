# urp-heightfield-lod

[![Unity Editor 上でのサンプル映像](http://img.youtube.com/vi/xsMIZjyObbE/mqdefault.jpg)](https://youtu.be/xsMIZjyObbE)
[![サンプル映像 2](http://img.youtube.com/vi/atnA-4gOfQI/mqdefault.jpg)](https://youtube.com/shorts/atnA-4gOfQI)
[![サンプル映像 3](http://img.youtube.com/vi/kKyzijjkOc8/mqdefault.jpg)](https://youtube.com/shorts/kKyzijjkOc8)

---

正射影 URP カメラ向けの適応 heightfield LOD パッケージ **[jp.nobnak.heightfield-lod](https://openupm.com/packages/jp.nobnak.heightfield-lod/)** の開発リポジトリです。

## パッケージ概要

正射影カメラの視野全体を覆う heightfield を、チャンク単位の適応 LOD で描画します。高さデータは `IHeightFieldSource` で外部供給し、曲率計算・LOD 分類・間接インスタンス描画はパッケージ側が担当します。

主な用途はフルスクリーン heightfield の可視化、メディアアート、動的シミュレーションの表示などです。アーキテクチャの詳細は [Doc/heightfield-lod-layered-design.ja.md](Doc/heightfield-lod-layered-design.ja.md) を参照してください。

## インストール

[OpenUPM](https://openupm.com/packages/jp.nobnak.heightfield-lod/) からインストールします。

### 1. Scoped Registry の登録

**Edit → Project Settings → Package Manager → Scoped Registries** で以下を追加します。

| 項目 | 値 |
| --- | --- |
| Name | `package.openupm.com` |
| URL | `https://package.openupm.com` |
| Scope(s) | `jp.nobnak` |

### 2. パッケージの追加

Package Manager の **+ → Add package by name…** で `jp.nobnak.heightfield-lod` を指定します。

依存パッケージは OpenUPM 経由で自動解決されます。バージョンや要件の詳細は Package Manager 上のパッケージ情報を参照してください。

## サンプル

Package Manager でパッケージ **jp.nobnak.heightfield-lod** を選択し、**Samples → Height Field → Import** でサンプルを取り込みます。

| ファイル | 内容 |
| --- | --- |
| `SineHeightFieldSource` | 正弦波による高さフィールド |
| `MusgraveHeightFieldSource` | Musgrave ノイズによる高さフィールド |
| `HeightFieldBridge` | Rig コンポーネントの配線 |
| `HeightField.unity` | デモシーン |

インポート後、メニュー **GameObject → Height Field → Setup Sample Rig** で Rig を自動配置できます。

## ドキュメント

| ドキュメント | 内容 |
| --- | --- |
| [Doc/heightfield-lod-layered-design.ja.md](Doc/heightfield-lod-layered-design.ja.md) | アーキテクチャ、モジュール境界、ランタイム構成 |
| [Doc/urp-heightfield-lod-design.ja.md](Doc/urp-heightfield-lod-design.ja.md) | 曲率 LOD、チャンクメッシュ、座標系、描画仕様 |
| [Doc/head-sway-lens-shift-camera.md](Doc/head-sway-lens-shift-camera.md) | カメラ行列のみの揺れ（ジオメトリ固定） |

## ライセンス

[LICENSE](LICENSE)（MIT License）を参照してください。
