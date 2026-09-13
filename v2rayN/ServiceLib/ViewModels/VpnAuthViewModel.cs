namespace ServiceLib.ViewModels;

public partial class VpnAuthViewModel : MyReactiveObject, ICloseable
{
    public event EventHandler? RequestClose;

    private readonly Config _vpnConfig;

    [Reactive]
    public partial string Email { get; set; }

    [Reactive]
    public partial string Password { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> LoginCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> RegisterCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelCmd { get; }

    public VpnAuthViewModel()
    {
        _vpnConfig = AppManager.Instance.Config;
        LoginCmd = ReactiveCommand.CreateFromTask(async () => await AuthAsync(VpnApiService.Instance.LoginAsync));
        RegisterCmd = ReactiveCommand.CreateFromTask(async () => await AuthAsync(VpnApiService.Instance.RegisterAsync));
        CancelCmd = ReactiveCommand.Create(Cancel);
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private async Task AuthAsync(Func<Config, string, string, Task<bool>> auth)
    {
        if (Email.IsNullOrEmpty() || Password.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseFillEmailPassword);
            return;
        }
        if (Password.Length != 6)
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseFillCorrectPassword);
            return;
        }

        if (await auth(_vpnConfig, Email, Password))
        {
            await ConfigHandler.SaveConfig(_vpnConfig);
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }
}
