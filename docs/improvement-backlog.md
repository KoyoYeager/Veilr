# Veilr 消去モード改善バックログ

## 1. 原因分析: なぜクロマキーに辿り着けなかったか

### 思考の経緯

| 段階 | アプローチ | 問題 |
|---|---|---|
| 初期 | HSV色相(H)で判定 | 黒(H=0)と赤(H=0)が同一 |
| 改善1 | CIE Lab色差距離 | 正しい方向だが二値マスクのまま |
| 改善2 | 膨張(dilation)でAA辺対策 | 過剰/不足の調整が困難 |
| 改善3 | 置換品質を平均化 | 効果はあるがハードエッジの根本問題が残る |
| **ユーザー提案** | **クロマキー合成** | **連続アルファで解決** |

### 根本原因

1. **問題のフレーミングが狭かった**
   - 「色検出 → 置換」（画像処理）として捉えた
   - 「色分離 → 合成」（映像制作）として捉えるべきだった
   - 同じ「特定色を除去する」問題が映像制作では50年以上の歴史がある

2. **二値マスクという前提を疑わなかった**
   - ピクセルを「消す or 残す」の二択で考えた
   - 連続的なアルファ値（0.0〜1.0）という発想が出なかった
   - 結果として膨張・平均化・エッジスキップなどの「パッチ」を積み重ねた

3. **検索クエリの偏り**
   - 検索: "color detection HSV threshold", "CIE Lab Delta E"
   - 検索すべきだった: "chroma key algorithm", "color keying", "alpha matting"
   - 隣接分野（映像制作、VFX）のキーワードを使わなかった

### 教訓

- **問題を解く前に、同じ問題を既に解いている分野を探す**
- 画像処理の問題 → 映像制作、印刷、医療画像、ゲームの分野を確認
- 「自分で一から考える」前に「業界ではどうしているか」を調べる
- 特に50年以上の歴史がある技術（クロマキーは1960年代〜）は最初に参照すべき

---

## 2. 改善案リスト

### 優先度: 最高

#### K. 色ファミリー自動拡張 ✅ ループ3で実装済み

Lab色相角度ベースの色ファミリーマッチを全アルゴリズム(ChromaKey/LabMask)に追加。
同じ色相方向(±10°)で彩度>10のピクセルを自動的に検出。

**結果**: ✅ ターゲットRGB(194,70,48)でRGB(230,1,0)の純赤テキストも検出可能に。
ただし「例題」ヘッダー(Lab角度差15.6°)のAA辺が若干影響を受ける。
閾値を`similarity * 0.5`に調整して妥協点を確保。

**発見**: 色相角度の差で赤(1°差)とオレンジ(15.6°差)は明確に分離可能。

---

#### D. デスピル後処理（Spill Suppression） ✅ ループ1で実装済み

クロマキーのPass 4としてデスピルを追加。ターゲット色の支配的チャネルを隣接ピクセルで抑制。
アライメント問題（GetWindowRect/Stretch="Fill"/Border除去）も同時修正。

**結果**: △ 実装完了だが、テスト画像の赤色がターゲット色(194,70,48)と大きく異なる純赤(230,1,0)だったため効果確認が限定的。デスピル自体のロジックは正常動作。

---

### 優先度: 高

#### B. YCbCr色空間オプション ✅ ループ2で実装済み

UIに3つ目のアルゴリズム選択肢として追加。CbCr平面での距離計算＋ソフトアルファブレンド。
**結果**: △ 実装完了。ターゲット色ミスマッチ問題（項目K）により効果検証は限定的。アルゴリズム自体は正常動作。

**概要**: クロマキー業界標準の色空間。輝度(Y)と色差(Cb,Cr)が完全分離。

**なぜ効果的か**: クロマキーが最も成功している色空間。CbCr平面での距離計算はLabより計算が軽く、クロマキーアルゴリズムとの親和性が高い。

**実装方針**:
```
Y  =  0.299R + 0.587G + 0.114B
Cb = -0.169R - 0.331G + 0.500B + 128
Cr =  0.500R - 0.419G - 0.081B + 128

距離 = sqrt((Cb_pixel - Cb_target)² + (Cr_pixel - Cr_target)²)
```

**メリット**: Labより計算が軽い（cbrt不要）。3つ目のアルゴリズム選択肢として追加。

**難易度**: 低 | **期待効果**: 中〜高

---

#### G. 背景推定の中央値化

**概要**: 置換色の算出を平均値から中央値に変更。

**なぜ効果的か**: 平均値は外れ値（AA辺のピンクがかったピクセル）に影響される。中央値は外れ値に強く、より代表的な背景色を返す。

**実装方針**:
```
12サンプルのR/G/B各チャネルをソートし、中央の2値の平均を使用
```

**難易度**: 低 | **期待効果**: 中

---

### 優先度: 中

#### A. OKLab色空間オプション

**概要**: CIE Labの改良版。2020年にBjörn Ottossonが発表。特に青の色相ずれが改善。

**なぜ効果的か**: CIE Labは青系の色差計算で色相が不正確にずれる。OKLabはこの問題を解決。赤系では差が小さいが、青・紫系の色除去で改善される。

**実装方針**: RGB→Linear RGB→LMS→OKLab変換。Labと同様のAPI。

**参考**: [A perceptual color space for image processing](https://bottosson.github.io/posts/oklab/)

**難易度**: 低 | **期待効果**: 低〜中（赤系では微差、青紫系で効果大）

---

#### E2. 適応的閾値

**概要**: 画像全体で固定閾値ではなく、ローカルな色分布に基づいて閾値を自動調整。

**なぜ効果的か**: 背景が白い領域と薄いクリーム色の領域では、最適な閾値が異なる。固定閾値はどちらかに妥協する。

**実装方針**:
```
画像をNxNブロックに分割
各ブロックの背景色の標準偏差を計算
標準偏差が低い（均一な背景）→ 閾値を厳しく
標準偏差が高い（多様な色）→ 閾値を緩く
```

**難易度**: 中 | **期待効果**: 中

---

#### F2. 段階的デスピル

**概要**: 消去境界からの距離に応じてデスピル強度を変化。近いほど強く、遠いほど弱く。

**なぜ効果的か**: 単純なデスピルは全体に一律適用するため、遠くのピクセルまで変色する。段階的にすることで自然な結果になる。

**実装方針**:
```
マーク済みピクセルからの距離マップを生成
距離1-3px: 強デスピル（80%補正）
距離4-6px: 中デスピル（40%補正）
距離7+px: なし
```

**難易度**: 中 | **期待効果**: 高

---

### 優先度: 低

#### C. Delta E 2000

**概要**: CIE76（現在のユークリッド距離）より知覚的に正確な色差計算。

**現状**: CIE76で十分な結果が出ているため優先度低。ただし将来的に微妙な色差の判定が必要になれば有効。

**難易度**: 中（複雑な式） | **期待効果**: 低

---

#### H2. マルチスケール処理

**概要**: 異なる解像度で検出してマージ。大きな色ブロックは低解像度で、細かいテキストは高解像度で処理。

**難易度**: 高 | **期待効果**: 中

---

### 優先度: 将来検討

#### I. ベイズマッティング

**概要**: 前景/背景の色分布を確率モデルで推定し、各ピクセルの最適なアルファ値をベイズ推定で算出。

**参考**: [A Bayesian approach to digital matting](https://www.researchgate.net/publication/3940780)

**難易度**: 高 | **期待効果**: 最高（理論的に最適解）

---

#### J. GrabCut

**概要**: グラフカットベースのセグメンテーション。エネルギー最小化で最適な前景/背景境界を算出。

**参考**: [GrabCut - Interactive Foreground Extraction](https://pub.ista.ac.at/~vnk/papers/grabcut_siggraph04.pdf)

**難易度**: 最高（OpenCV依存） | **期待効果**: 高

---

## 3. 参考文献

- [Greenscreen code and hints](http://gc-films.com/chromakey.html) — クロマキー実装の詳細解説
- [Production-ready green screen in the browser](https://jameshfisher.com/2020/08/11/production-ready-green-screen-in-the-browser/) — ブラウザ実装例
- [Deconstructing Despill Algorithms](https://benmcewan.com/blog/understanding-despill-algorithms) — デスピルアルゴリズムの解説
- [A perceptual color space for image processing (OKLab)](https://bottosson.github.io/posts/oklab/) — OKLab色空間
- [OKLab - Wikipedia](https://en.wikipedia.org/wiki/Oklab_color_space) — OKLabの概要
- [Delta E 101](http://zschuessler.github.io/DeltaE/learn/) — 色差計算の基礎
- [CIELAB color space - Wikipedia](https://en.wikipedia.org/wiki/CIELAB_color_space) — CIE Lab
- [Interactive High-Quality Green-Screen Keying](http://yaksoy.github.io/papers/TOG16-keying.pdf) — 高品質キーイング論文
- [GrabCut paper (SIGGRAPH 2004)](https://pub.ista.ac.at/~vnk/papers/grabcut_siggraph04.pdf)
- [A Bayesian approach to digital matting](https://www.researchgate.net/publication/3940780)
- [A color extraction algorithm by segmentation (2023)](https://www.nature.com/articles/s41598-023-48689-y)
