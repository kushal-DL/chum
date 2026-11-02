using System.ComponentModel;

namespace Chum.App.Models;

/// <summary>
/// Represents a category tag pill in the OverlayWindow with reactive selection state.
/// </summary>
public sealed class CategoryPillItem : INotifyPropertyChanged
{
    private bool _isActive;

    public string Name { get; }

    public string DisplayLabel => Name.Length switch
    {
        0 => string.Empty,
        1 => char.ToUpperInvariant(Name[0]).ToString(),
        _ => char.ToUpperInvariant(Name[0]) + Name[1..]
    };

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    public CategoryPillItem(string name, bool isActive = false)
    {
        Name = name ?? string.Empty;
        _isActive = isActive;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
