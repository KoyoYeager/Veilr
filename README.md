# Veilr

**Screen Color Eraser** — 画面上の指定色を隠す・消すWindows常駐ツール

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/OS-Windows%2010%2F11-blue.svg)]()

[English](README.en.md)

---

## Veilrとは

Veilrは、画面上の特定の色（デフォルト：赤）を隠したり消したりできるWindows用の常駐ツールです。

勉強用の赤シートのデジタル版として使ったり、画面上の不要な色情報を除去したりできます。

## 特徴

- **シートモード** — 乗算ブレンド（実物の赤シートと同じ原理）で同色の文字を隠す
- **消去モード** — 指定色のピクセルを検出し、周囲の色で自然に塗りつぶして完全に消去
- **3つの消去アルゴリズム** — Chroma Key / Lab Mask / YCbCr から選択可能
- **色ファミリー自動検出** — 1つの赤を指定するだけで、明るい赤・暗い赤も自動的に消去
- **全画面対応** — モニターを選択してシートを全画面に拡大
- **画像エクスポート** — 色抜き済みの画像をPNG/JPEG/BMPで保存
- **カラーピッカー** — スポイトで画面上の任意の色を取得（ズームプレビュー付き）
- **前回値の復元** — 色・位置・サイズ・モードを自動記憶
- **日本語/英語対応** — UI言語を切替可能
- **ポータブル** — exe1つで動作、インストール不要

## スクリーンショット

> *（開発後に追加予定）*

## 動作環境

- Windows 10 / 11
- .NET 8 ランタイム（self-contained版はランタイム不要）

## ダウンロード

[Releases](https://github.com/KoyoYeager/Veilr/releases) ページから最新の `Veilr-win-x64.zip` をダウンロードし、展開して `Veilr.exe` を実行してください。

または `dist/Veilr.exe`（self-contained単一exe）を直接ダウンロードしてください。

## 使い方

### 基本操作

1. `Veilr.exe` を起動するとタスクトレイに常駐し、シートウィンドウが表示されます
2. シートのどこでもドラッグして移動できます
3. 角や辺をドラッグしてサイズを調整
4. F5またはスペースキーで画面を再キャプチャ

### ショートカットキー

| キー | 動作 |
|------|------|
| `Ctrl+Shift+E` | シートの表示/非表示 |
| `F5` / `Space` | 画面再キャプチャ（リフレッシュ） |
| `Escape` | 全画面モード解除 |

### モードの違い

**シートモード（デフォルト）**

実物の赤シートと同じ**乗算ブレンド**方式。画面をキャプチャし、シート色でフィルターをかけます。赤い文字が赤い背景に溶け込んで見えなくなります。

**消去モード**

指定色のピクセルを検出し、周囲の背景色で自然に置換します。3つのアルゴリズムから選択可能：

| アルゴリズム | 特徴 | 向いているケース |
|---|---|---|
| **Chroma Key** (デフォルト) | CIE Lab色空間＋ソフトアルファブレンド＋デスピル | アンチエイリアス付きテキスト、グラデーション |
| **Lab Mask** | CIE Lab色空間＋二値マスク＋膨張＋中央値置換 | ベタ塗り、太文字、くっきりした境界 |
| **YCbCr** | 放送業界標準のYCbCr色空間＋ソフトアルファブレンド | 高速処理、幅広い色範囲 |

### ツールバー

| ボタン | 動作 |
|--------|------|
| 消去モードに切替 / シートモードに切替 | モード切替 |
| 全画面 | モニター選択→全画面化 |
| 保存 | シート範囲の色抜き画像を保存 |
| 設定 | 設定ウィンドウを開く |
| ✕（右上） | シートを非表示（トレイに格納） |

### 設定

設定はJSON形式で自動保存されます（`Veilr.exe` と同じフォルダに生成）。

主な設定項目：

- **シートの色** — カラーピッカー、スポイト、HEX入力で指定
- **シート透明度** — シートモードのブレンド強度
- **消去アルゴリズム** — Chroma Key / Lab Mask / YCbCr
- **消去モードの許容範囲** — 厳密〜柔軟のスライダーで調整
- **更新頻度** — 消去モードの処理間隔（100-500ms）
- **ショートカットキー** — カスタマイズ可能
- **UI言語** — 日本語 / 英語（変更時にアプリ再起動）

## 技術詳細

### シートモードの原理

単純な半透明重ね合わせではなく、**乗算ブレンド（Multiply Blend）**を使用。実物のカラーフィルターと同じ原理で、赤い光だけを通し、緑と青を吸収します。

```
結果.R = ピクセル.R × シート色.R / 255
結果.G = ピクセル.G × シート色.G / 255
結果.B = ピクセル.B × シート色.B / 255
```

### 消去モードの原理

**CIE Lab色空間**で色差を計算。HSVと異なり、黒と赤が同じ色相になる問題がありません。

- **色相距離**（chrominance distance）で同系色を判定
- **色ファミリー自動拡張** — Lab色相角度±10°で同系色を自動検出
- **クロマキー方式のソフトアルファ** — 0.0〜1.0の連続値でスムーズな境界
- **段階的デスピル** — 消去境界の色カストを距離に応じて除去
- **中央値背景推定** — 外れ値に強い置換色の計算

詳細は [docs/algorithm.md](docs/algorithm.md) を参照。

## ビルド方法

### 必要なもの

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git

### ビルド手順

```bash
git clone https://github.com/KoyoYeager/Veilr.git
cd Veilr
setup.bat        # NuGetパッケージ復元
build.bat        # デバッグビルド
run.bat          # ビルド＋実行
release.bat      # リリースビルド（dist/Veilr.exe を生成）
```

### バッチファイル一覧

| ファイル | 用途 |
|----------|------|
| `setup.bat` | 初回セットアップ（SDK確認＋NuGet復元） |
| `build.bat` | デバッグビルド |
| `release.bat` | リリースビルド（単一exe生成） |
| `run.bat` | ビルド＋即実行 |
| `clean.bat` | ビルド成果物削除 |
| `publish.bat` | GitHub Releases用のzip作成 |

> Visual StudioのGUIは不要です。VS Code＋C# Dev Kit拡張、またはお好みのエディタで開発できます。

## プロジェクト構成

```
Veilr/
├── README.md / README.en.md
├── LICENSE
├── setup.bat / build.bat / release.bat / ...
├── .github/workflows/build.yml
├── dist/Veilr.exe              （self-contained単一exe）
├── docs/
│   ├── spec.md                 （仕様書）
│   ├── ui-design.md            （UI設計書）
│   ├── algorithm.md            （アルゴリズム仕様）
│   └── improvement-backlog.md  （改善バックログ）
└── src/Veilr/
    ├── Views/          （SheetWindow, SettingsWindow, ColorPickerWindow, EyedropperOverlay）
    ├── ViewModels/     （SheetViewModel, SettingsViewModel）
    ├── Services/       （ScreenCapture, ColorDetector, HotkeyService, SettingsService）
    ├── Helpers/        （Win32Interop, HsvConverter, Loc）
    ├── Models/         （AppSettings, ColorTarget）
    ├── Localization/   （Strings.ja.resx, Strings.en.resx）
    └── Resources/      （テーマ, アイコン）
```

## ライセンス

[MIT License](LICENSE)

## コントリビューション

Issue・Pull Requestを歓迎します。バグ報告や機能提案は [Issues](https://github.com/KoyoYeager/Veilr/issues) からお願いします。
