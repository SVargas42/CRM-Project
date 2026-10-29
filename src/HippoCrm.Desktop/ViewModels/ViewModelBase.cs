using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace HippoCrm.Desktop.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T objField, T objValue, [CallerMemberName] string? strPropertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(objField, objValue)) return false;
        objField = objValue;
        Notify(strPropertyName);
        return true;
    }

    protected void Notify([CallerMemberName] string? strPropertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(strPropertyName));
}

public sealed class RelayCommand(Action actExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? objParameter) => true;
    public void Execute(object? objParameter) => actExecute();
}
