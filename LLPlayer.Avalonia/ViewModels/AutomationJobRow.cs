using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class AutomationJobRow : ObservableObject
{
    ControlJob job;

    public AutomationJobRow(ControlJob job, Func<ControlJob, Task> cancel)
    {
        this.job = job;
        CancelCommand = new AsyncRelayCommand(() => cancel(this.job), () => CanCancel);
    }

    public Guid JobId => job.JobId;
    public string Operation => job.OperationId;
    public string Status => job.Status.ToString();
    public string Progress => job.Progress is { } progress ? progress.ToString("P0") : "";
    public string Stage => job.Stage ?? "";
    public bool CanCancel => job.Status is ControlJobStatus.Accepted or ControlJobStatus.WaitingForResource
        or ControlJobStatus.Running or ControlJobStatus.AwaitingTranslation or ControlJobStatus.AwaitingCleanup;
    public IAsyncRelayCommand CancelCommand { get; }

    public void Refresh(ControlJob updated)
    {
        if (updated == job) return;
        job = updated;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(Stage));
        OnPropertyChanged(nameof(CanCancel));
        CancelCommand.NotifyCanExecuteChanged();
    }
}
