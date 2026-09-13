using v2rayN.Desktop.Base;

namespace v2rayN.Desktop.Views;

public partial class VpnAuthWindow : WindowBase<VpnAuthViewModel>
{
    public VpnAuthWindow()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.Email, v => v.txtEmail.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Password, v => v.pwdPassword.Text).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.LoginCmd, v => v.btnLogin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegisterCmd, v => v.btnRegister).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CancelCmd, v => v.btnCancel).DisposeWith(disposables);
        });
    }
}
