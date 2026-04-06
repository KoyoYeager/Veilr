using System.Diagnostics;
using System.Windows;
using Veilr.Helpers;
using Veilr.Services;
using Veilr.ViewModels;

namespace Veilr.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _viewModel = new SettingsViewModel(settingsService);
        DataContext = _viewModel;

        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        Title = Loc.SettingsTitle;

        Tab1.Header = Loc.TabColor;
        Tab2.Header = Loc.TabBehavior;
        Tab3.Header = Loc.TabHotkey;
        Tab4.Header = Loc.TabAppearance;

        LblSheetColor.Text = Loc.SheetColor;
        BtnChange.Content = Loc.ChangeColor;
        LblOpacity.Text = Loc.SheetOpacity;
        LblAlgorithm.Text = Loc.EraseAlgorithm;
        LblChromaKeyDesc.Text = Loc.ChromaKeyDesc;
        LblLabMaskDesc.Text = Loc.LabMaskDesc;
        LblYCbCrDesc.Text = Loc.YCbCrDesc;
        LblTolerance.Text = Loc.EraseTolerance;
        LblToleranceDesc.Text = Loc.EraseToleranceDesc;
        LblStrict.Text = Loc.Strict;
        LblFlexible.Text = Loc.Flexible;

        LblEraseSettings.Text = Loc.EraseSettings;
        LblUpdateRate.Text = Loc.UpdateFrequency;
        LblUpdateRateDesc.Text = Loc.UpdateFrequencyDesc;

        LblHotkeySettings.Text = Loc.HotkeySettings;
        LblToggleSheet.Text = Loc.ToggleSheet;
        LblHotkeyNote.Text = Loc.HotkeyNote;

        LblLanguageNote.Text = Loc.LanguageNote;

        BtnEyedropper.Content = Loc.Eyedropper;
        BtnApply.Content = Loc.Apply;
        BtnCancelBtn.Content = Loc.Cancel;
    }

    private void BtnPickColor_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(_viewModel.ColorR, _viewModel.ColorG, _viewModel.ColorB),
            FullOpen = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _viewModel.SetColorFromDialog(dlg.Color);
        }
    }

    private void BtnEyedropper_Click(object sender, RoutedEventArgs e)
    {
        // Hide settings so the user can pick from any part of the screen
        Hide();

        var result = EyedropperOverlay.Pick();
        if (result.Confirmed)
        {
            _viewModel.SetColorFromDialog(
                System.Drawing.Color.FromArgb(result.PickedR, result.PickedG, result.PickedB));
        }

        Show();
        Activate();
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Apply();
        RestartIfChanged();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Apply();
        if (!RestartIfChanged())
            Close();
    }

    private bool RestartIfChanged()
    {
        if (!_viewModel.HasChanges) return false;

        var exePath = Environment.ProcessPath;
        if (exePath != null)
        {
            Process.Start(exePath);
            Application.Current.Shutdown();
        }
        return true;
    }
}
