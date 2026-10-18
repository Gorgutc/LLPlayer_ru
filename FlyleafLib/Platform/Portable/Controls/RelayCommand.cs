using System.Windows.Input;

// F-13 portable (Linux) TFM: the Windows RelayCommand (Controls/WPF/RelayCommand.cs) hooks WPF's CommandManager,
// which does not exist off Windows. Same namespace and public surface, so shared code (Config, filters, translate
// settings) compiles unchanged; CanExecuteChanged is raised explicitly through OnCanExecuteChanged().
namespace FlyleafLib.Controls.WPF;

public class RelayCommand : ICommand
{
    private Action<object> execute;

    private Predicate<object> canExecute;

    public RelayCommand(Action<object> execute) : this(execute, DefaultCanExecute) { }

    public RelayCommand(Action<object> execute, Predicate<object> canExecute)
    {
        this.execute    = execute ?? throw new ArgumentNullException(nameof(execute));
        this.canExecute = canExecute ?? throw new ArgumentNullException(nameof(canExecute));
    }

    public event EventHandler CanExecuteChanged;

    private static bool DefaultCanExecute(object parameter) => true;
    public bool CanExecute(object parameter) => canExecute != null && canExecute(parameter);

    public void Execute(object parameter) => execute(parameter);

    public void OnCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public void Destroy()
    {
        canExecute = _ => false;
        execute = _ => { return; };
    }
}
