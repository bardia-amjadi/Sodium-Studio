using System.Windows.Input;
using CodeEdit.Maui;
using CommunityToolkit.Mvvm.ComponentModel;
using SodiumStudio.Editing;
using SodiumStudio.Storage;

namespace SodiumStudio.ViewModels;

/// <summary>One open file: its in-memory <see cref="CodeDocument"/> (which owns the undo history) and state.</summary>
public sealed partial class EditorTabViewModel : ObservableObject
{
    private readonly EditorWorkspaceViewModel _owner;

    public EditorTabViewModel(EditorWorkspaceViewModel owner, StorageNode file, StorageNode parent, DecodedText content)
    {
        _owner = owner;
        node = file;
        Parent = parent;
        Document = CodeDocument.FromPlainText(content.Text);
        LineEnding = content.LineEnding;
        HasBom = content.HasBom;
    }

    public CodeDocument Document { get; }

    /// <summary>Folder containing the file; used by Save As.</summary>
    public StorageNode Parent { get; }

    public LineEnding LineEnding { get; }

    public bool HasBom { get; }

    /// <summary>Prevents overlapping saves of the same tab.</summary>
    public bool IsSaving { get; set; }

    public string Title => IsMissing ? $"{Node.Name} (deleted)" : Node.Name;

    public string DirtyMark => IsDirty ? "●" : string.Empty;

    public LanguageKind Language => LanguageInfo.FromFileName(Node.Name);

    public bool IsReadOnly => !Node.CanWrite;

    public ICommand SelectCommand => _owner.SelectTabCommand;

    public ICommand CloseCommand => _owner.CloseTabCommand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Language), nameof(IsReadOnly))]
    private StorageNode node;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirtyMark))]
    private bool isDirty;

    [ObservableProperty]
    private bool isActive;

    /// <summary>The file no longer exists in storage (deleted or moved outside the editor).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool isMissing;
}
