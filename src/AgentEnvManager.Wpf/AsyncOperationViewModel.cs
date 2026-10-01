namespace AgentEnvManager.Wpf;

public abstract class AsyncOperationViewModel : ObservableObject
{
    private readonly Func<Task>? _refreshOperations;
    private bool _isBusy;
    private string _statusMessage;

    protected AsyncOperationViewModel(
        Func<Task>? refreshOperations,
        string initialStatusMessage)
    {
        _refreshOperations = refreshOperations;
        _statusMessage = initialStatusMessage;
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        protected set => SetProperty(ref _statusMessage, value);
    }

    protected async Task RunBusyAsync(Func<Task> operation)
    {
        IsBusy = true;
        try
        {
            await operation();
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task RefreshOperationsAsync()
    {
        if (_refreshOperations is not null)
        {
            await _refreshOperations();
        }
    }

    protected void SetError(string operation, Exception exception)
    {
        StatusMessage = $"{operation}失败：{exception.Message}";
    }

    protected abstract void RaiseCommandStates();
}
