using InternetMeter.Services;

namespace InternetMeter.ViewModels;

public sealed class AdapterOption : NotifyBase
{
    private bool _isSelected;

    public AdapterOption(NetworkAdapterInfo adapter, bool isSelected)
    {
        Id = adapter.Id;
        Name = adapter.Name;
        Description = adapter.Description;
        Status = adapter.IsUp ? $"Connected · {adapter.Type}" : $"Disconnected · {adapter.Type}";
        _isSelected = isSelected;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Status { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

