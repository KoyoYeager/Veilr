namespace Veilr.Helpers;

public static class Loc
{
    private static string _lang = "ja";

    public static void SetLanguage(string lang) => _lang = lang;

    public static string SheetMode => _lang == "en" ? "Sheet mode" : "シートモード";
    public static string EraseMode => _lang == "en" ? "Erase mode" : "消去モード";
    public static string SheetModeFullScreen => _lang == "en" ? "Sheet mode (full screen)" : "シートモード（全画面）";
    public static string EraseModeFullScreen => _lang == "en" ? "Erase mode (full screen)" : "消去モード（全画面）";
    public static string SwitchToEraseMode => _lang == "en" ? "Switch to erase" : "消去モードに切替";
    public static string SwitchToSheetMode => _lang == "en" ? "Switch to sheet" : "シートモードに切替";
    public static string FullScreen => _lang == "en" ? "🖥 Full screen" : "🖥 全画面";
    public static string ExitFullScreen => _lang == "en" ? "🖥 Exit" : "🖥 解除";
    public static string Export => _lang == "en" ? "📷 Save" : "📷 保存";
    public static string Settings => _lang == "en" ? "⚙ Settings" : "⚙ 設定";
    public static string ShowHideSheet => _lang == "en" ? "Show / Hide sheet" : "シートを表示 / 非表示";
    public static string SettingsMenu => _lang == "en" ? "Settings..." : "設定...";
    public static string Exit => _lang == "en" ? "Exit" : "終了";

    // Settings window
    public static string SettingsTitle => _lang == "en" ? "Veilr Settings" : "Veilr 設定";
    public static string TabColor => _lang == "en" ? "Color" : "色設定";
    public static string TabBehavior => _lang == "en" ? "Behavior" : "動作";
    public static string TabHotkey => _lang == "en" ? "Hotkeys" : "ホットキー";
    public static string TabAppearance => _lang == "en" ? "Appearance" : "外観";

    public static string SheetColor => _lang == "en" ? "Sheet color" : "シートの色";
    public static string ChangeColor => _lang == "en" ? "Change..." : "変更...";
    public static string SheetOpacity => _lang == "en" ? "Sheet opacity" : "シート透明度";
    public static string EraseTolerance => _lang == "en" ? "Erase mode tolerance" : "消去モードの許容範囲";
    public static string EraseToleranceDesc => _lang == "en"
        ? "Higher values will also erase colors similar to the target"
        : "値が大きいほど、ターゲット色に近い色も消去対象になります";
    public static string Strict => _lang == "en" ? "Strict" : "厳密";
    public static string Flexible => _lang == "en" ? "Flexible" : "柔軟";
    public static string EraseAlgorithm => _lang == "en" ? "Erase algorithm" : "消去アルゴリズム";
    public static string ChromaKeyDesc => _lang == "en"
        ? "Smooth alpha blending at edges. Best for anti-aliased text and gradual color boundaries."
        : "境界をアルファブレンドで滑らかに処理。アンチエイリアス付きテキストや色の境界が緩やかな場合に最適。";
    public static string LabMaskDesc => _lang == "en"
        ? "Sharp binary removal. Best for solid color blocks, bold text, and cases where chroma key over-blends."
        : "くっきり二値消去。ベタ塗り、太文字、クロマキーがぼやける場合に最適。";
    public static string YCbCrDesc => _lang == "en"
        ? "Broadcast industry standard. Luminance-independent keying on CbCr plane. Fast and robust."
        : "放送業界標準。輝度に依存しないCbCr平面でのキーイング。高速で堅牢。";

    public static string EraseSettings => _lang == "en" ? "Erase mode settings" : "消去モード設定";
    public static string UpdateFrequency => _lang == "en" ? "Update rate:" : "更新頻度:";
    public static string UpdateFrequencyDesc => _lang == "en"
        ? "Lower values are smoother but use more CPU"
        : "値が小さいほど滑らかですが、CPU負荷が増加します";

    public static string HotkeySettings => _lang == "en" ? "Keyboard shortcut" : "ショートカットキー設定";
    public static string ToggleSheet => _lang == "en" ? "Toggle sheet:" : "シート表示/非表示:";
    public static string HotkeyNote => _lang == "en"
        ? "* Hotkey changes take effect after restart"
        : "※ ホットキーの変更はアプリの再起動後に反映されます";

    public static string LanguageLabel => "言語 / Language";
    public static string LanguageNote => _lang == "en"
        ? "* The app will restart when language is changed"
        : "※ 言語を変更するとアプリが再起動されます";

    public static string Apply => _lang == "en" ? "Apply" : "適用";
    public static string Cancel => _lang == "en" ? "Cancel" : "キャンセル";

    // Color picker
    public static string ColorPickerTitle => _lang == "en" ? "Color Picker" : "🔍 カラーピッカー";
    public static string EyedropperMode => _lang == "en" ? "Eyedropper" : "スポイトモード";
    public static string EyedropperButton => _lang == "en" ? "🔍 Pick from screen" : "🔍 画面から色を取得";
    public static string PickedColor => _lang == "en" ? "Picked color" : "取得した色";
    public static string ManualInput => _lang == "en" ? "Manual input" : "手動入力";
    public static string UseThisColor => _lang == "en" ? "Use this color" : "この色を使う";
    public static string Eyedropper => _lang == "en" ? "🔍 Eyedropper" : "🔍 スポイト";
}
