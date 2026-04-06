# Veilr - Screen Color Eraser 仕様書

**バージョン**: v1.0  
**作成日**: 2026-04-06  
**対応OS**: Windows 10/11  
**開発言語**: C# (.NET 8 / WPF)  
**ライセンス**: MIT  
**UI言語**: 日本語 / 英語（切替可）  
**配布形式**: exe単体（ポータブル、インストール不要）

---

## 1. 概要

画面上の指定範囲において、ターゲット色（デフォルト：赤）のテキストや要素をリアルタイムで除去・隠蔽するタスクトレイ常駐型ツール。

### 1.1 動作モード

| モード | 名称 | 動作 | 画面更新 |
|--------|------|------|----------|
| モードA | **シートモード（デフォルト）** | 色付き半透明ウィンドウをユーザが自由に移動して、同色の文字を隠す | 更新不要 |
| モードB | **消去モード** | シートウィンドウが重なっている範囲のみ、ターゲット色を周囲色で塗りつぶして消す | 常時更新 |

---

## 2. 機能一覧

### 2.1 コア機能

#### 2.1.1 シートモード（デフォルト）
- 色付き半透明ウィンドウを画面上に表示（デフォルト：赤 #FF0000）
- ユーザが**自由にドラッグ移動・リサイズ**できる
- シート色と同色の文字がシート越しに同化して見えなくなる原理
- 画像処理は一切不要（単なる色付き半透明ウィンドウ）
- シートの色・透明度はユーザが設定で変更可能
- ウィンドウ上の文字やボタンも半透明で表示
- 最前面固定（`Topmost`）
- 複数シートの同時表示に対応
- 枠のドラッグで移動、角・辺のドラッグでリサイズ
- **全画面モード**：モニターを選択してシートをそのモニター全体に拡大

#### 2.1.2 消去モード
- **シートウィンドウが重なっている範囲のみ**を処理対象とする（全画面時はモニター全体）
- シートウィンドウの位置・サイズをそのまま処理範囲として使用（別途の範囲選択は不要）
- ターゲット色のピクセルを検出し、**隣接する非ターゲット色**で塗りつぶし
  - 塗りつぶしロジック：左右→上下の優先順で最も近い非ターゲット色ピクセルの色を採用
- 処理済み画像をシートウィンドウ上に描画（半透明シートの代わりに処理画像を表示）
- 更新頻度：100〜500ms間隔（設定可能）
- モード切替はウィンドウ上のボタンで行う

#### 2.1.3 全画面モード
- ツールバーの[🖥全画面]ボタンで起動
- マルチモニター環境では対象モニターを選択するサブメニューを表示
  - 例：`モニター1 (1920×1080)` / `モニター2 (2560×1440)`
- シートウィンドウを選択モニターの全画面サイズに拡大
- ツールバーは画面下部に常時表示（半透明）
- 全画面解除ボタン or ESCキーで元のサイズ・位置に復帰
- シートモード・消去モードどちらでも全画面対応

#### 2.1.5 色指定
- **ターゲット色**（消したい色）：デフォルト赤（#FF0000）
- **ターゲット色の履歴**：使用した色を自動で履歴に追加（最大20件、新しい順）
- **プリセット**：よく使う色を保存（最大10色）。履歴からワンクリックでプリセットに追加可能
- **シート色**（シートモード用）：ターゲット色と連動（個別指定も可）
- カラーピッカーUI搭載（画面上の任意の色をスポイトで取得）
- 色の許容範囲（閾値）をスライダーで調整（HSV空間で判定）
  - **プレビュー機能**：閾値変更時、対象ピクセルをリアルタイムでハイライト表示し、ユーザが確認してから適用
  - **厳密モード（デフォルト）**：閾値を狭く設定し、明確にターゲット色と一致するピクセルのみ対象
  - **柔軟モード**：閾値を広げて類似色も対象にする（ユーザが明示的に切替）
  - 閾値はH/S/V各チャネル個別にスライダーで調整可能

#### 2.1.6 画像エクスポート
- **シートウィンドウが重なっている範囲のみ**をキャプチャし、色抜き済みの画像として保存
- シートウィンドウ自体は画像に含めない（シートの下の画面からターゲット色を消した結果を保存）
- 全画面モード時はモニター全体が対象
- ウィンドウ上の📷ボタンから実行
- 対応フォーマット：PNG / JPEG / BMP
- 保存先：ユーザ指定（デフォルトはデスクトップ）
- ファイル名自動生成：`Veilr_YYYYMMDD_HHmmss.png`

### 2.2 タスクトレイ機能

#### 2.2.1 トレイメニュー
```
[トレイアイコン右クリック]
├── ▶ シートを表示 / 非表示
├── ⚙ 設定...
├── ──────────────
└── ✕ 終了
```

※ モード切替・画像エクスポートはシートウィンドウ上のボタンで操作

#### 2.2.2 ホットキー
| キー | 動作 |
|------|------|
| `Ctrl+Shift+E` | シートウィンドウの表示/非表示トグル |

※ 設定から変更可能

### 2.3 UI テーマ・カラー設定
- ダークテーマ / ライトテーマ切替
- アクセントカラーのカスタマイズ
- 設定ウィンドウ・選択枠の色もテーマに連動
- 設定はJSON形式でローカル保存

---

## 3. 技術設計

### 3.1 技術選定比較

| 項目 | C# WPF (.NET 8) | Python + PyQt6 | Python + tkinter |
|------|-----------------|----------------|------------------|
| **UI品質** | ◎ XAML＋アニメーション標準 | ○ Qt標準テーマ | △ 見た目が古い |
| **透明オーバーレイ** | ◎ `AllowsTransparency` 標準対応 | ○ `setWindowFlags` で可能 | △ win32api直叩き必須 |
| **クリック透過** | ◎ `WS_EX_TRANSPARENT` ネイティブ | ○ ctypes経由 | △ ctypes経由 |
| **最前面固定** | ◎ `Topmost=True` 1行 | ○ `setWindowFlag` | ○ `wm_attributes` |
| **タスクトレイ** | ◎ `NotifyIcon` ネイティブ | ○ `QSystemTrayIcon` | △ `pystray` 外部 |
| **画像処理速度** | ◎ Span<T>/SIMD活用可 | ○ numpy高速 | △ PIL単体は遅い |
| **配布** | ◎ 単一exe（self-contained） | △ PyInstaller肥大化 | △ 同左 |
| **開発速度** | ○ | ◎ | ○ |

**推奨: C# WPF (.NET 8)**  
理由：透明ウィンドウ・クリック透過・最前面固定がすべてネイティブで、UIも最も綺麗。単一exeで配布もしやすい。

**次点: Python + PyQt6**  
C#が難しい場合の代替。UIはQt標準テーマで十分綺麗。

### 3.2 使用技術（C# WPF構成）

| 技術 | 用途 |
|------|------|
| .NET 8 / WPF | UI・設定画面・タスクトレイ |
| `System.Drawing` / `SkiaSharp` | スクリーンキャプチャ・画像処理 |
| Win32 Interop (`user32.dll`) | オーバーレイウィンドウ制御 |
| `System.Text.Json` | 設定ファイル読み書き |
| `Hardcodet.NotifyIcon.Wpf` | タスクトレイ（NuGet） |
| `ColorPicker.WPF` or 自作 | カラーピッカーUI |
| グローバルホットキー（Win32） | ショートカット登録 |

### 3.3 オーバーレイウィンドウ仕様

#### 必須要件

| 要件 | 実装方法 |
|------|----------|
| **最前面固定** | `Topmost = true` + `HWND_TOPMOST`（他の最前面ウィンドウとの競合対策） |
| **クリック透過** | `WS_EX_TRANSPARENT` + `WS_EX_LAYERED`（マウス操作は下のウィンドウに貫通） |
| **透明背景** | `AllowsTransparency = true` + `WindowStyle = None` + `Background = Transparent` |
| **枠線表示** | 選択範囲の境界に薄い点線枠を描画（ユーザが範囲を視認できるように） |

```xml
<!-- WPF XAML: オーバーレイウィンドウ定義 -->
<Window
    AllowsTransparency="True"
    WindowStyle="None"
    Background="Transparent"
    Topmost="True"
    ShowInTaskbar="False"
    IsHitTestVisible="False">
    <!-- IsHitTestVisible=False でWPFレベルのクリック透過 -->
    <!-- 加えてWin32 WS_EX_TRANSPARENT でOS レベルでも透過 -->
</Window>
```

```csharp
// Win32レベルのクリック透過設定
const int GWL_EXSTYLE = -20;
const int WS_EX_TRANSPARENT = 0x00000020;
const int WS_EX_LAYERED = 0x00080000;

var hwnd = new WindowInteropHelper(overlayWindow).Handle;
var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
```

#### 描画更新

| モード | 更新方式 |
|--------|----------|
| 消去モード | タイマー（100〜500ms）で繰り返しキャプチャ→処理→`WriteableBitmap`に描画 |
| 重畳モード | 初回のみキャプチャ→静的`Image`描画。手動リフレッシュで再描画 |

### 3.4 アーキテクチャ

```
┌──────────────────────────────────────────┐
│              App (WPF .NET 8)            │
│  ┌───────────┐  ┌─────────────────────┐  │
│  │ NotifyIcon │  │  SettingsManager    │  │
│  │ (トレイ)   │  │  (JSON永続化)      │  │
│  └─────┬─────┘  └──────────┬──────────┘  │
│        │                   │             │
│  ┌─────▼───────────────────▼──────────┐  │
│  │         MainController             │  │
│  │  ┌─────────────┐ ┌──────────────┐  │  │
│  │  │ RegionSelect │ │ HotkeyService│  │  │
│  │  │ (透明Window) │ │ (Win32 Hook) │  │  │
│  │  └─────────────┘ └──────────────┘  │  │
│  └─────────────────┬──────────────────┘  │
│                    │                     │
│  ┌─────────────────▼──────────────────┐  │
│  │        ProcessingEngine            │  │
│  │  ┌───────────┐ ┌────────────────┐  │  │
│  │  │ ScreenCap  │ │ ColorDetector  │  │  │
│  │  │ (GDI+/DX)  │ │ (HSV判定)     │  │  │
│  │  └───────────┘ └────────────────┘  │  │
│  └─────────────────┬──────────────────┘  │
│                    │                     │
│  ┌─────────────────▼──────────────────┐  │
│  │      OverlayRenderer               │  │
│  │  (透明Window + WriteableBitmap)     │  │
│  │  最前面固定 + クリック透過           │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
```

### 3.5 色検出アルゴリズム（擬似コード）

```csharp
// HSV空間での色判定
bool IsTargetColor(Color pixel, HsvColor targetHsv, HsvThreshold threshold)
{
    var pixelHsv = pixel.ToHsv();
    return Math.Abs(pixelHsv.H - targetHsv.H) <= threshold.H
        && Math.Abs(pixelHsv.S - targetHsv.S) <= threshold.S
        && Math.Abs(pixelHsv.V - targetHsv.V) <= threshold.V;
}

// 消去モード：周囲色での塗りつぶし
Color FillWithNeighbor(Bitmap image, int x, int y)
{
    int[][] dirs = { new[]{-1,0}, new[]{1,0}, new[]{0,-1}, new[]{0,1} };
    foreach (var d in dirs)
    {
        int nx = x + d[0], ny = y + d[1];
        if (InBounds(nx, ny) && !IsTargetColor(image.GetPixel(nx, ny)))
            return image.GetPixel(nx, ny);
    }
    return defaultBackgroundColor;
}
```

---

## 4. 設定ファイル構造

```json
{
  "version": "1.0",
  "mode": "redsheet",
  "target_color": {
    "rgb": [255, 0, 0],
    "threshold": { "h": 15, "s": 50, "v": 50 },
    "threshold_mode": "strict"
  },
  "overlay_color": {
    "rgb": [255, 0, 0],
    "opacity": 0.5
  },
  "update_interval_ms": 200,
  "hotkeys": {
    "toggle_sheet": "ctrl+shift+e"
  },
  "ui_theme": {
    "mode": "dark",
    "accent_color": [0, 120, 215],
    "language": "ja"
  },
  "color_presets": [],
  "color_history": [],
  "last_session": {
    "target_color_rgb": [255, 0, 0],
    "threshold": { "h": 15, "s": 50, "v": 50 },
    "threshold_mode": "strict",
    "overlay_color_rgb": [255, 0, 0],
    "overlay_opacity": 0.5,
    "mode": "redsheet",
    "update_interval_ms": 200,
    "sheets": [
      { "x": 100, "y": 200, "w": 300, "h": 200 }
    ]
  }
}
```

### 4.2 前回値の記憶・復元

- **自動保存タイミング**：非表示時・終了時・設定変更時に `last_session` へ自動書き込み
- **起動時の復元**：前回の設定値（ターゲット色・閾値・モード・シート色・透明度・更新頻度）を自動ロード
- **シート位置の復元**：前回のシートウィンドウの位置・サイズを記憶し、次回起動時に復元
- **復元スキップ**：ユーザがシートを移動・リサイズした場合は前回値を上書き
```

---

## 5. 画面フロー

```
起動 → タスクトレイ常駐 + シートウィンドウ表示（シートモード）
         │
         ├── Ctrl+Shift+E → シート表示/非表示トグル
         │
         ├── シートウィンドウ上の操作
         │     ├── ドラッグ → 移動
         │     ├── 角・辺ドラッグ → リサイズ
         │     ├── [🔄モード] → シート ↔ 消去モード切替
         │     ├── [🖥全画面] → モニター選択 → 全画面化（ESCで解除）
         │     ├── [📷保存] → エクスポートダイアログ（シート範囲のみ保存）
         │     ├── [⚙設定] → 設定ウィンドウ
         │     └── [✕閉じる] → 非表示（トレイに格納）
         │
         └── トレイ右クリック → 終了
```

---

## 6. 制約・注意事項

- Windows専用（win32 API依存）
- 管理者権限不要（通常のスクリーンキャプチャ権限で動作）
- GPU負荷：消去モードの高頻度更新時にCPU使用率が上がる可能性あり（Span&lt;T&gt;/SIMD最適化で対応）
- マルチモニター対応：各モニターで独立した範囲指定が可能
- DPIスケーリング：高DPI環境での座標補正が必要

---

## 7. 開発フェーズ

| フェーズ | 内容 | 優先度 |
|----------|------|--------|
| Phase 1 | タスクトレイ常駐＋範囲選択＋消去モード基本実装 | 高 |
| Phase 2 | 重畳モード＋カラーピッカー＋閾値調整 | 高 |
| Phase 3 | UIテーマ＋設定画面＋ホットキー | 中 |
| Phase 4 | プリセット保存＋複数範囲＋パフォーマンス最適化 | 中 |
| Phase 5 | マルチモニター対応＋DPI対応 | 低 |

---

## 8. 開発環境・ビルド方針

### 8.1 基本方針

**VS GUIは使わない。** すべてCLI（`dotnet` コマンド）＋エディタ（VS Code等）で開発する。

必要なもの：
- .NET 8 SDK（`winget install Microsoft.DotNet.SDK.8`）
- エディタ：VS Code + C# Dev Kit拡張（推奨）、またはお好みのエディタ
- Git

### 8.2 バッチファイル一覧

プロジェクトルートに以下のバッチファイルを配置：

| ファイル | 用途 |
|----------|------|
| `setup.bat` | 初回セットアップ（SDK確認＋NuGet復元） |
| `build.bat` | デバッグビルド |
| `release.bat` | リリースビルド（単一exe生成） |
| `run.bat` | ビルド＋即実行 |
| `clean.bat` | ビルド成果物削除 |
| `publish.bat` | GitHub Releases用のzip作成 |

```bat
@echo off
REM === setup.bat ===
echo [1/2] .NET SDK確認...
dotnet --version || (echo .NET SDK が見つかりません。 && exit /b 1)
echo [2/2] NuGetパッケージ復元...
dotnet restore
echo セットアップ完了
```

```bat
@echo off
REM === build.bat ===
dotnet build -c Debug
if %ERRORLEVEL% NEQ 0 (echo ビルド失敗 && exit /b 1)
echo ビルド成功
```

```bat
@echo off
REM === release.bat ===
dotnet publish -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o .\dist
echo リリースビルド完了: dist\Veilr.exe
```

```bat
@echo off
REM === run.bat ===
call build.bat
if %ERRORLEVEL% NEQ 0 exit /b 1
dotnet run
```

```bat
@echo off
REM === clean.bat ===
dotnet clean
if exist dist rmdir /s /q dist
echo クリーン完了
```

```bat
@echo off
REM === publish.bat ===
call release.bat
if %ERRORLEVEL% NEQ 0 exit /b 1
powershell -Command "Compress-Archive -Path '.\dist\Veilr.exe' -DestinationPath '.\dist\Veilr-win-x64.zip' -Force"
echo パッケージ作成完了: dist\Veilr-win-x64.zip
```

### 8.3 プロジェクト初期化手順

```bat
REM 1. プロジェクト作成（VS不要）
dotnet new wpf -n Veilr
cd Veilr

REM 2. NuGetパッケージ追加
dotnet add package Hardcodet.NotifyIcon.Wpf
dotnet add package System.Drawing.Common

REM 3. ビルド確認
dotnet build
```

---

## 9. GitHub リポジトリ構成

```
Veilr/
├── README.md
├── LICENSE                    （MIT）
├── .gitignore                 （dotnet テンプレート）
├── setup.bat
├── build.bat
├── release.bat
├── run.bat
├── clean.bat
├── publish.bat
├── .github/
│   └── workflows/
│       └── build.yml          （GitHub Actions CI）
├── src/
│   └── Veilr/
│       ├── Veilr.csproj
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── Models/
│       │   ├── AppSettings.cs
│       │   └── ColorTarget.cs
│       ├── Services/
│       │   ├── ScreenCaptureService.cs
│       │   ├── ColorDetectorService.cs
│       │   ├── HotkeyService.cs
│       │   └── SettingsService.cs
│       ├── Views/
│       │   ├── SheetWindow.xaml         （メインUI：シート＋ツールバー）
│       │   ├── SettingsWindow.xaml
│       │   ├── ColorPickerWindow.xaml
│       │   ├── ExportDialog.xaml
│       │   └── ThresholdPreview.xaml
│       ├── ViewModels/
│       │   ├── SheetViewModel.cs
│       │   └── SettingsViewModel.cs
│       ├── Helpers/
│       │   ├── Win32Interop.cs
│       │   └── HsvConverter.cs
│       ├── Localization/
│       │   ├── Strings.ja.resx         （日本語）
│       │   └── Strings.en.resx         （英語）
│       └── Resources/
│           └── tray-icon.ico
└── docs/
    └── 仕様書.md
```

### 9.1 GitHub Actions CI（build.yml）

```yaml
name: Build
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore src/Veilr
      - run: dotnet build src/Veilr -c Release --no-restore
      - run: dotnet publish src/Veilr -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./dist
      - uses: actions/upload-artifact@v4
        with:
          name: Veilr-win-x64
          path: ./dist/Veilr.exe
```

### 9.2 リリースタグ運用

```bat
REM バージョンタグ付けてpush → GitHub Actionsでビルド → Releasesにexe添付
git tag v1.0.0
git push origin v1.0.0
```
