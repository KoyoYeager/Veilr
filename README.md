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

- **シートモード** — 色付き半透明ウィンドウを画面に重ねて、同色の文字を隠す（赤シートの原理）
- **消去モード** — 指定色のピクセルを周囲の色で塗りつぶし、完全に消去
- **全画面対応** — モニターを選択してシートを全画面に拡大
- **画像エクスポート** — 色抜き済みの画像をPNG/JPEG/BMPで保存
- **カラーピッカー** — スポイトで画面上の任意の色を取得
- **色の履歴・プリセット** — 使った色を自動記録、よく使う色はプリセット保存
- **前回値の復元** — 色・位置・サイズ・モードを自動記憶
- **ダーク/ライトテーマ** — アクセントカラーもカスタマイズ可能
- **日本語/英語対応** — UI言語を切替可能
- **ポータブル** — exe1つで動作、インストール不要

## スクリーンショット

> *（開発後に追加予定）*

## 動作環境

- Windows 10 / 11
- .NET 8 ランタイム（self-contained版はランタイム不要）

## ダウンロード

[Releases](https://github.com/KoyoYeager/Veilr/releases) ページから最新の `Veilr-win-x64.zip` をダウンロードし、展開して `Veilr.exe` を実行してください。

## 使い方

### 基本操作

1. `Veilr.exe` を起動するとタスクトレイに常駐し、シートウィンドウが表示されます
2. シートをドラッグして隠したい場所に移動
3. 角や辺をドラッグしてサイズを調整

### ショートカットキー

| キー | 動作 |
|------|------|
| `Ctrl+Shift+E` | シートの表示/非表示 |

※設定から変更可能

### ツールバー

シートウィンドウ下部のツールバーで操作できます。

| ボタン | 動作 |
|--------|------|
| 消去モードに切替 / シートモードに切替 | モード切替 |
| 🖥 全画面 | モニター選択→全画面化 |
| 📷 保存 | シート範囲の色抜き画像を保存 |
| ⚙ | 設定ウィンドウを開く |
| ✕ | シートを非表示（トレイに格納） |

### モードの違い

**シートモード（デフォルト）**

色付き半透明ウィンドウを重ねるだけ。赤シートと同じ原理で、同系色の文字が同化して見えなくなります。

**消去モード**

シートが重なっている範囲の画面をキャプチャし、指定色のピクセルを周囲の色で塗りつぶして完全に消去します。

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

## 設定

設定はJSON形式で自動保存されます（`Veilr.exe` と同じフォルダに生成）。

主な設定項目：

- ターゲット色とHSV閾値
- シート色と透明度
- 消去モードの更新頻度
- テーマ（ダーク/ライト）とアクセントカラー
- UI言語（日本語/英語）
- ショートカットキー
- 前回セッションの復元設定

## プロジェクト構成

```
Veilr/
├── README.md
├── README.en.md
├── LICENSE
├── setup.bat / build.bat / release.bat / ...
├── .github/workflows/build.yml
├── src/Veilr/
│   ├── Views/          （SheetWindow, SettingsWindow, ...）
│   ├── ViewModels/     （SheetViewModel, SettingsViewModel）
│   ├── Services/       （ScreenCapture, ColorDetector, ...）
│   ├── Helpers/        （Win32Interop, HsvConverter）
│   ├── Localization/   （Strings.ja.resx, Strings.en.resx）
│   └── Resources/      （テーマ, スタイル）
└── docs/               （仕様書, UI設計書）
```

## ライセンス

[MIT License](LICENSE)

## コントリビューション

Issue・Pull Requestを歓迎します。バグ報告や機能提案は [Issues](https://github.com/KoyoYeager/Veilr/issues) からお願いします。
