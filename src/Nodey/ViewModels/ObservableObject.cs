using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Nodey.ViewModels;

/// <summary>
/// Minimal INotifyPropertyChanged base for Nodey view models (MVVM is mandatory
/// per PLAN.md §4; no external MVVM package is pinned for Step 1).
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetAndRaise<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }
}
