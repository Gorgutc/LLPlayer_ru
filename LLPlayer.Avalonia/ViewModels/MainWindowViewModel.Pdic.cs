namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    readonly HashSet<Task> pdicOperations = [];
    public Func<string, int, CancellationToken, Task>? PdicSender { get; set; }

    void SendPdicSelection(Services.WordSelectionSnapshot selection)
    {
        Task operation = SendAsync();
        pdicOperations.Add(operation);
        _ = RetireAsync();
        async Task RetireAsync()
        {
            try { await operation; }
            finally { pdicOperations.Remove(operation); }
        }
        async Task SendAsync()
        {
            try
            {
                if (PdicSender == null) throw new InvalidOperationException("Open PDIC settings to configure the Windows dictionary bridge.");
                await PdicSender(selection.Text, selection.WordOffset, browserWaitCancellation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!disposed && !wordActionsStopping)
                    ShowToast("PDIC unavailable", error is ArgumentException or InvalidOperationException or IOException or PlatformNotSupportedException
                        ? error.Message : "The PDIC bridge could not finish this lookup. Check PDIC settings and select the word again.", ToastVariant.Destructive);
            }
        }
    }
    public Task DrainPdicOperationsAsync() => Task.WhenAll(pdicOperations.ToArray());
}
