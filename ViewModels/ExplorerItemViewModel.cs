using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SodiumStudio.Storage;

namespace SodiumStudio.ViewModels;

/// <summary>
/// One visible row of the flattened explorer tree. Expanding a folder inserts its children
/// after it in <see cref="ExplorerViewModel.Items"/>; collapsing removes them.
/// </summary>
public sealed partial class ExplorerItemViewModel : ObservableObject
{
    private readonly ExplorerViewModel _owner;

    public ExplorerItemViewModel(ExplorerViewModel owner, StorageNode node, StorageNode parent, int depth)
    {
        _owner = owner;
        Node = node;
        Parent = parent;
        Depth = depth;
    }

    public StorageNode Node { get; }

    public StorageNode Parent { get; }

    public int Depth { get; }

    public string Name => Node.Name;

    public bool IsDirectory => Node.IsDirectory;

    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    public string Chevron => !IsDirectory ? string.Empty : IsExpanded ? "▾" : "▸";

    public string Icon => !IsDirectory ? "📄" : IsExpanded ? "📂" : "📁";

    // Commands are owned by the explorer; rows pass themselves as the parameter.
    public ICommand ToggleCommand => _owner.ToggleItemCommand;

    public ICommand MenuCommand => _owner.ShowItemMenuCommand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron), nameof(Icon))]
    private bool isExpanded;

    [ObservableProperty]
    private bool isSelected;
}
