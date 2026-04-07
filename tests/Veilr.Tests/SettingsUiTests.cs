using System.Windows;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTabItem = System.Windows.Controls.TabItem;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using Veilr.Helpers;
using Veilr.Services;
using Veilr.Views;

namespace Veilr.Tests;

/// <summary>
/// Settings UI controls existence test.
/// Verifies XAML and code-behind are correctly wired.
/// </summary>
public class SettingsUiTests
{
    [Fact]
    public void SettingsWindow_AutoRefreshControls_Exist()
    {
        // WPF requires STA thread
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var ss = new SettingsService();
                // Don't load from file — use defaults
                Loc.SetLanguage("ja");

                var win = new SettingsWindow(ss);

                // Find controls by x:Name
                var chk = (WpfCheckBox?)win.FindName("ChkAutoRefresh");
                var lbl = (WpfTextBlock?)win.FindName("LblAutoRefreshDesc");
                var tab2 = (WpfTabItem?)win.FindName("Tab2");

                Assert.NotNull(chk);
                Assert.NotNull(lbl);
                Assert.NotNull(tab2);

                // Verify localized content was set
                Assert.Equal("自動更新", chk!.Content?.ToString());
                Assert.Contains("自動的に再キャプチャ", lbl!.Text);
                Assert.Equal("動作", tab2!.Header?.ToString());

                // Verify binding works
                Assert.False(chk.IsChecked); // default = false

                // Verify Tab2 has the expected children
                var sp = tab2.Content as WpfStackPanel;
                Assert.NotNull(sp);
                // Should have: LblEraseSettings, ChkAutoRefresh, LblAutoRefreshDesc, Grid (slider), LblUpdateRateDesc
                Assert.True(sp!.Children.Count >= 5,
                    $"Tab2 should have ≥5 children, got {sp.Children.Count}");

                // Verify fps display property
                var vm = win.DataContext as Veilr.ViewModels.SettingsViewModel;
                Assert.NotNull(vm);
                Assert.Contains("fps", vm!.UpdateIntervalDisplay);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(10000);

        if (error != null) throw error;
    }

    [Fact]
    public void SettingsWindow_UpdateIntervalDisplay_ShowsFps()
    {
        var ss = new SettingsService();
        var vm = new Veilr.ViewModels.SettingsViewModel(ss);

        // Default 200ms = 5fps
        Assert.Equal("200ms (5fps)", vm.UpdateIntervalDisplay);

        // Change to 16ms = 62fps
        vm.UpdateIntervalMs = 16;
        Assert.Equal("16ms (62fps)", vm.UpdateIntervalDisplay);

        // Change to 100ms = 10fps
        vm.UpdateIntervalMs = 100;
        Assert.Equal("100ms (10fps)", vm.UpdateIntervalDisplay);

        // Change to 500ms = 2fps
        vm.UpdateIntervalMs = 500;
        Assert.Equal("500ms (2fps)", vm.UpdateIntervalDisplay);
    }

    [Fact]
    public void SettingsViewModel_AutoRefreshEnabled_DefaultFalse()
    {
        var ss = new SettingsService();
        var vm = new Veilr.ViewModels.SettingsViewModel(ss);

        Assert.False(vm.AutoRefreshEnabled);
    }

    [Fact]
    public void SettingsViewModel_AutoRefreshEnabled_ToggleDetectsChange()
    {
        var ss = new SettingsService();
        var vm = new Veilr.ViewModels.SettingsViewModel(ss);

        Assert.False(vm.HasChanges);

        vm.AutoRefreshEnabled = true;
        Assert.True(vm.HasChanges);
    }

    [Fact]
    public void SettingsViewModel_UpdateIntervalMs_Clamps16to500()
    {
        var ss = new SettingsService();
        var vm = new Veilr.ViewModels.SettingsViewModel(ss);

        vm.UpdateIntervalMs = 5; // below minimum
        Assert.Equal(16, vm.UpdateIntervalMs);

        vm.UpdateIntervalMs = 999; // above maximum
        Assert.Equal(500, vm.UpdateIntervalMs);
    }
}
