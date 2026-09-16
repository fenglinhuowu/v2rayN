using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

/// <summary>
/// ThemeSettingView.xaml
/// </summary>
public partial class ThemeSettingView : ReactiveUserControl<ThemeSettingViewModel>
{
    public Button BtnVpnLogout => btnVpnLogout;

    private ProfilesViewModel? _profilesViewModel;

    public void UpdateVpnLogoutVisible(bool visible)
    {
        btnVpnLogout.IsVisible = visible;
        sepVpnLogout.IsVisible = visible;
    }

    public void BindVpnAuth(ProfilesViewModel profilesViewModel)
    {
        _profilesViewModel = profilesViewModel;
        UpdateVpnLogoutVisible(profilesViewModel.VpnLoggedIn);
    }

    private async void BtnVpnLogout_Click(object? sender, RoutedEventArgs e)
    {
        if (_profilesViewModel is null)
        {
            return;
        }

        CloseFlyout();
        await _profilesViewModel.LogoutVpnAsync();
    }

    private void CloseFlyout()
    {
        if (btnMore.Flyout is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    public ThemeSettingView()
    {
        InitializeComponent();
        ViewModel = new ThemeSettingViewModel();

        btnVpnLogout.Click += BtnVpnLogout_Click;

        cmbCurrentTheme.ItemsSource = Utils.GetEnumNames<ETheme>();
        cmbCurrentFontSize.ItemsSource = Enumerable.Range(Global.MinFontSize, Global.MinFontSizeCount).ToList();
        cmbCurrentLanguage.ItemsSource = Global.Languages;

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.CurrentTheme, v => v.cmbCurrentTheme.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CurrentFontSize, v => v.cmbCurrentFontSize.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CurrentLanguage, v => v.cmbCurrentLanguage.SelectedValue).DisposeWith(disposables);
        });
    }
}
