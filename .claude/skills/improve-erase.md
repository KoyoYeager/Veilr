---
name: improve-erase
description: 消去モードの色除去アルゴリズムを自動改善ループで実行する
user_invocable: true
---

# 消去モード改善ループ

Veilrの消去モード（CIE Lab色差ベースの色除去）を反復的に改善するスキル。
設定ファイルに保存されたウィンドウ位置・ターゲット色を使って、ビルド→起動→スクリーンショット→分析→修正を繰り返す。

## 手順

以下のループを**赤文字が完全に消え、かつ黒文字や他の色に影響がなくなるまで**繰り返す。

### 1. 現在の設定確認

```bash
cat C:/workspace/Veilr/build/settings.json
```

ターゲット色(target_color.rgb)、閾値(threshold)、ウィンドウ位置(last_session.sheets)を確認。

### 2. ビルド

```bash
powershell -Command "Get-Process -Name 'Veilr' -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Seconds 1"
dotnet build src/Veilr -c Debug
```

### 3. 起動 & スクリーンショット取得

```bash
cd C:/workspace/Veilr && ./build/Veilr.exe &
sleep 5
```

PowerShellでウィンドウを検出し、そのエリアをスクリーンショットとして `C:/tmp/veilr_improve_N.png` に保存する。

```powershell
# ウィンドウ検出 → クロップ → 保存のテンプレート
Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public class WImprove {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc f, IntPtr p);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static RECT Find(int pid) { RECT r = new RECT(); EnumWindows((h, p) => { int fp; GetWindowThreadProcessId(h, out fp); if (fp == pid && IsWindowVisible(h)) { GetWindowRect(h, out r); return false; } return true; }, IntPtr.Zero); return r; }
}
'@
$procs = @(Get-Process -Name 'Veilr' -ErrorAction SilentlyContinue)
$r = [WImprove]::Find($procs[0].Id)
$s = [System.Windows.Forms.Screen]::PrimaryScreen
$b = New-Object System.Drawing.Bitmap($s.Bounds.Width, $s.Bounds.Height)
$g = [System.Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen($s.Bounds.Location, [System.Drawing.Point]::Empty, $s.Bounds.Size)
$c = $b.Clone((New-Object System.Drawing.Rectangle($r.L, $r.T, ($r.R-$r.L), ($r.B-$r.T))), $b.PixelFormat)
$c.Save("C:/tmp/veilr_improve_N.png", [System.Drawing.Imaging.ImageFormat]::Png)
$c.Dispose(); $g.Dispose(); $b.Dispose()
```

### 4. スクリーンショット分析

Readツールで画像を確認し、以下を評価：

| チェック項目 | 合格基準 |
|---|---|
| 黒文字の保持 | 黒テキストが欠けたり薄くなったり崩れていない |
| 赤文字の消去 | 赤テキスト・赤マーカー(▶、→等)が完全に見えない |
| 「例題」ラベル | 茶色/オレンジの「例題」ヘッダーとその背景が元の色・形のまま保持 |
| 罫線・枠線 | 表の枠線や区切り線が消えたり欠けていない |
| 背景色 | ページの白/クリーム色の背景が汚れたり変色していない |
| 数字・記号 | 数値（0.8626等）やカッコ・ドットが欠損していない |
| 置換品質 | 消去跡が周囲の背景色に自然に溶け込み、白い穴や色のにじみがない |

### 5. 問題があれば修正

修正対象ファイル: `src/Veilr/Services/ColorDetectorService.cs`

**調整可能なパラメータ:**
- `IsTarget()` の判定ロジック（Lab色相距離、角度マッチ、彩度閾値）
- 条件付き膨張の隣接数閾値・角度閾値
- `FindNearestClean()` の探索半径
- settings.json の `threshold.h` 値

**よくある問題と対策:**

| 症状 | 原因 | 対策 |
|---|---|---|
| 黒文字が消える | 無彩色フィルターが不十分 | `pixelChroma > N` のNを上げる |
| 赤文字が残る | 色相距離閾値が狭い | `maxDist` を広げるか角度マッチ閾値を緩和 |
| 茶色が巻き込まれる | 色相角度閾値が広い | `angleDiffDeg` 閾値を狭める |
| AA辺が残る | 膨張条件が厳しい | 隣接数を減らすか角度閾値を広げる |

### 6. 再ビルド → 3に戻る

プロセスを停止してからビルドすること:
```bash
powershell -Command "Get-Process -Name 'Veilr' -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Seconds 1"
dotnet build src/Veilr -c Debug
```

### 7. 合格したらコミット

```bash
git add src/Veilr/Services/ColorDetectorService.cs
git commit -m "Improve erase mode color detection algorithm"
```

## 改善バックログ

`docs/improvement-backlog.md` に優先度付きの改善案リストがある。改善ループで次に何を試すか迷ったらこのファイルを参照。

## 参照ファイル

- 改善バックログ: `docs/improvement-backlog.md`（次に何を試すか）
- アルゴリズム仕様: `docs/algorithm.md`
- 色検出サービス: `src/Veilr/Services/ColorDetectorService.cs`
- 設定ViewModel: `src/Veilr/ViewModels/SettingsViewModel.cs`（許容範囲→閾値マッピング）
- HSVコンバータ: `src/Veilr/Helpers/HsvConverter.cs`（参考用、現在はLabベース）

## 重要な原則

- **CIE Lab色空間**を使用（HSVは使わない）
- **色相距離**（a,bコンポーネント）で判定、明度(L)は無視
- **色相角度**でアンチエイリアス辺を検出
- **無彩色フィルター**: `pixelChroma > 3` で黒/白/灰色を除外
- 閾値調整は `settings.json` の `threshold.h` で制御（`maxDist = h * 1.35`）
