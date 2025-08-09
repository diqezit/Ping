#nullable enable

namespace PingTestTool;

public abstract class ObservableBase : INotifyPropertyChanged
{
    private readonly ConcurrentDictionary<string, object> _values = new();
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(T value, [CallerMemberName] string? name = null)
    {
        if (name == null) return false;

        var old = _values.GetOrAdd(name, default(T)!);
        if (EqualityComparer<T>.Default.Equals((T)old, value))
            return false;

        _values[name] = value!;
        OnPropertyChanged(name);
        return true;
    }

    protected T GetProperty<T>(T defaultValue = default!, [CallerMemberName] string? name = null) =>
        name == null ? defaultValue : (T)_values.GetOrAdd(name, defaultValue!);
}