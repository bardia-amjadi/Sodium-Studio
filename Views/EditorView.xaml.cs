using System.ComponentModel;
using CodeEdit.Maui;
using RichEdit.Maui;
using SodiumStudio.Editing;
using SodiumStudio.ViewModels;

namespace SodiumStudio.Views;

/// <summary>
/// Hosts the single CodeEditor control. Open files are kept as CodeDocuments in the view models;
/// switching tabs swaps the control's Document. Everything that must talk to the control
/// (undo/redo, selection, find/replace, syntax decorations) lives here; file and tab logic lives in
/// <see cref="EditorWorkspaceViewModel"/>.
/// </summary>
public partial class EditorView : ContentView
{
    // Above this size highlighting is skipped to keep typing responsive.
    private const int MaxHighlightLength = 300_000;
    private static readonly TimeSpan HighlightDelay = TimeSpan.FromMilliseconds(200);

    // VS Code "Dark+" inspired colors.
    private static readonly Color DefaultColor = Color.FromArgb("#D4D4D4");
    private static readonly Color KeywordColor = Color.FromArgb("#569CD6");
    private static readonly Color StringColor = Color.FromArgb("#CE9178");
    private static readonly Color CommentColor = Color.FromArgb("#6A9955");
    private static readonly Color NumberColor = Color.FromArgb("#B5CEA8");
    private static readonly Color PreprocessorColor = Color.FromArgb("#C586C0");

    private EditorWorkspaceViewModel? _viewModel;
    private EditorTabViewModel? _shownTab;
    private bool _swappingDocument;
    private bool _ignoreCase = true;
    private int _lastMatchStart = -1;
    private int _lastMatchLength;
    private CancellationTokenSource? _highlightCts;
    private IDisposable? _decorationLayer;

    public EditorView()
    {
        InitializeComponent();

        // Lambdas keep this independent of the exact event delegate types.
        CodeBox.TextChanged += (_, _) => OnTextChanged();
        CodeBox.SelectionChanged += (_, _) => UpdateCaret();
        UpdateCaseButton();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Settings.Changed -= ApplySettings;
        }

        _viewModel = BindingContext as EditorWorkspaceViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Settings.Changed += ApplySettings;
        ApplySettings();
        ShowTab(_viewModel.ActiveTab);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorWorkspaceViewModel.ActiveTab) && _viewModel is not null)
        {
            ShowTab(_viewModel.ActiveTab);
        }
    }

    private void ApplySettings()
    {
        if (_viewModel is null)
        {
            return;
        }

        var settings = _viewModel.Settings;
        CodeBox.IndentSize = settings.IndentSize;
        CodeBox.UseTabs = settings.UseTabs;
        CodeBox.WordWrap = settings.WordWrap;
        CodeBox.ShowLineNumbers = settings.ShowLineNumbers;
        CodeBox.FontSize = settings.FontSize;
    }

    // ------------------------------------------------------------ tab switching

    private void ShowTab(EditorTabViewModel? tab)
    {
        if (ReferenceEquals(tab, _shownTab))
        {
            return;
        }

        CancelHighlight();
        DisposeLayer();
        _lastMatchStart = -1;

        if (_shownTab is not null)
        {
            _shownTab.PropertyChanged -= OnShownTabPropertyChanged;
        }

        _shownTab = tab;
        if (tab is null)
        {
            if (_viewModel is not null)
            {
                _viewModel.CaretText = string.Empty;
            }

            return;
        }

        tab.PropertyChanged += OnShownTabPropertyChanged;

        // Assigning a document must not be mistaken for a user edit.
        _swappingDocument = true;
        try
        {
            CodeBox.Document = tab.Document;
            CodeBox.IsReadOnly = tab.IsReadOnly;
        }
        finally
        {
            _swappingDocument = false;
        }

        UpdateCaret();
        ScheduleHighlight(immediate: true);
    }

    private void OnShownTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_shownTab is null)
        {
            return;
        }

        if (e.PropertyName == nameof(EditorTabViewModel.Language))
        {
            ScheduleHighlight(immediate: true); // e.g. after Save As with a new extension
        }
        else if (e.PropertyName == nameof(EditorTabViewModel.IsReadOnly))
        {
            CodeBox.IsReadOnly = _shownTab.IsReadOnly;
        }
    }

    // ------------------------------------------------------------ editor events

    private void OnTextChanged()
    {
        if (_swappingDocument || _shownTab is null)
        {
            return;
        }

        // Conservative: any edit marks the tab dirty, even if undone back to the saved text.
        _shownTab.IsDirty = true;
        _lastMatchStart = -1;
        ScheduleHighlight(immediate: false);
    }

    private void UpdateCaret()
    {
        if (_viewModel is null || _shownTab is null)
        {
            return;
        }

        var position = CodeBox.CaretPosition;
        _viewModel.CaretText = $"Ln {position.Line}, Col {position.Column}";
    }

    // ------------------------------------------------------------ syntax highlighting

    private async void ScheduleHighlight(bool immediate)
    {
        CancelHighlight();
        var tab = _shownTab;
        if (tab is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _highlightCts = cts;

        try
        {
            if (!immediate)
            {
                await Task.Delay(HighlightDelay, cts.Token);
            }

            await HighlightAsync(tab, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request.
        }
        catch (Exception ex)
        {
            // Highlighting is cosmetic; never let it crash the editor.
            System.Diagnostics.Debug.WriteLine($"Highlighting failed: {ex}");
        }
    }

    private async Task HighlightAsync(EditorTabViewModel tab, CancellationToken ct)
    {
        var document = tab.Document;
        if (!ReferenceEquals(CodeBox.Document, document))
        {
            return;
        }

        // Capture on the UI thread, analyze on a worker thread, publish on the UI thread.
        var snapshot = document.CurrentSnapshot;
        var text = snapshot.Text;
        if (text.Length == 0 || text.Length > MaxHighlightLength)
        {
            DisposeLayer();
            return;
        }

        var language = tab.Language;
        var spans = await Task.Run(() => BuildSpans(text, language), ct);
        if (ct.IsCancellationRequested || !ReferenceEquals(CodeBox.Document, document))
        {
            return;
        }

        var decorations = spans
            .Select(s => new RichTextDecoration(
                new RichTextRange(s.Start, s.Length),
                new RichTextDecorationStyle { ForegroundColor = s.Color }))
            .ToArray();

        var layer = CodeBox.Decorations.CreateLayer();
        if (!layer.TrySet(snapshot.Revision, decorations))
        {
            layer.Dispose(); // the text changed while we were analyzing; a newer pass is coming
            return;
        }

        var previous = _decorationLayer;
        _decorationLayer = layer;
        previous?.Dispose();
    }

    /// <summary>
    /// Token colors plus default-colored gaps, so the whole text is covered by one layer and the
    /// base text color does not depend on the control's own theme defaults.
    /// </summary>
    private static List<(int Start, int Length, Color Color)> BuildSpans(string text, LanguageKind language)
    {
        var spans = new List<(int, int, Color)>();
        var position = 0;

        foreach (var token in SyntaxHighlighter.Tokenize(text, language))
        {
            if (token.Start > position)
            {
                spans.Add((position, token.Start - position, DefaultColor));
            }

            spans.Add((token.Start, token.Length, ColorFor(token.Kind)));
            position = token.Start + token.Length;
        }

        if (position < text.Length)
        {
            spans.Add((position, text.Length - position, DefaultColor));
        }

        return spans;
    }

    private static Color ColorFor(TokenKind kind) => kind switch
    {
        TokenKind.Keyword => KeywordColor,
        TokenKind.String => StringColor,
        TokenKind.Comment => CommentColor,
        TokenKind.Number => NumberColor,
        TokenKind.Preprocessor => PreprocessorColor,
        _ => DefaultColor,
    };

    private void CancelHighlight()
    {
        _highlightCts?.Cancel();
        _highlightCts = null;
    }

    private void DisposeLayer()
    {
        _decorationLayer?.Dispose();
        _decorationLayer = null;
    }

    // ------------------------------------------------------------ toolbar

    private void OnUndoClicked(object? sender, EventArgs e)
    {
        CodeBox.Undo();
        UpdateCaret();
    }

    private void OnRedoClicked(object? sender, EventArgs e)
    {
        CodeBox.Redo();
        UpdateCaret();
    }

    private void OnToggleFindClicked(object? sender, EventArgs e)
    {
        FindBar.IsVisible = !FindBar.IsVisible;
        if (FindBar.IsVisible)
        {
            FindEntry.Focus();
        }
    }

    private void OnCloseFindClicked(object? sender, EventArgs e) => FindBar.IsVisible = false;

    // ------------------------------------------------------------ find / replace

    private void OnFindTextChanged(object? sender, TextChangedEventArgs e)
    {
        _lastMatchStart = -1;
        FindStatus.Text = string.Empty;
    }

    private void OnCaseClicked(object? sender, EventArgs e)
    {
        _ignoreCase = !_ignoreCase;
        _lastMatchStart = -1;
        UpdateCaseButton();
    }

    private void UpdateCaseButton() =>
        CaseButton.BackgroundColor = _ignoreCase ? Colors.Transparent : Color.FromArgb("#094771");

    private void OnFindNextClicked(object? sender, EventArgs e) => FindStep(forward: true);

    private void OnFindPreviousClicked(object? sender, EventArgs e) => FindStep(forward: false);

    private void FindStep(bool forward)
    {
        var term = FindEntry.Text ?? string.Empty;
        if (term.Length == 0 || _shownTab is null)
        {
            FindStatus.Text = string.Empty;
            return;
        }

        var text = CodeBox.Document.Text;
        var caret = CodeBox.GetOffset(CodeBox.CaretPosition);
        var onLastMatch = IsCaretOnLastMatch(caret);

        var index = forward
            ? TextSearch.FindNext(text, term, onLastMatch ? _lastMatchStart + _lastMatchLength : caret, _ignoreCase)
            : TextSearch.FindPrevious(text, term, onLastMatch ? _lastMatchStart : caret, _ignoreCase);

        if (index < 0)
        {
            _lastMatchStart = -1;
            FindStatus.Text = "No matches";
            return;
        }

        FindStatus.Text = string.Empty;
        SelectMatch(index, term.Length);
    }

    private bool IsCaretOnLastMatch(int caret) =>
        _lastMatchStart >= 0 && caret >= _lastMatchStart && caret <= _lastMatchStart + _lastMatchLength;

    private void SelectMatch(int start, int length)
    {
        CodeBox.SelectedRange = new RichTextRange(start, length);
        CodeBox.ScrollIntoView(CodeBox.SelectedRange);

        _lastMatchStart = start;
        _lastMatchLength = length;
    }

    private void OnReplaceClicked(object? sender, EventArgs e)
    {
        var term = FindEntry.Text ?? string.Empty;
        if (term.Length == 0 || _shownTab is null || _shownTab.IsReadOnly)
        {
            return;
        }

        var replacement = ReplaceEntry.Text ?? string.Empty;
        var text = CodeBox.Document.Text;
        var caret = CodeBox.GetOffset(CodeBox.CaretPosition);
        var comparison = _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var onMatch = IsCaretOnLastMatch(caret)
            && _lastMatchStart + term.Length <= text.Length
            && string.Compare(text, _lastMatchStart, term, 0, term.Length, comparison) == 0;

        if (!onMatch)
        {
            FindStep(forward: true); // first press selects the next match, second press replaces it
            return;
        }

        var start = _lastMatchStart;
        CodeBox.SelectedRange = new RichTextRange(start, term.Length);
        CodeBox.Selection.ReplaceText(replacement);
        _lastMatchStart = -1;

        var next = TextSearch.FindNext(CodeBox.Document.Text, term, start + replacement.Length, _ignoreCase);
        if (next < 0)
        {
            FindStatus.Text = "No more matches";
        }
        else
        {
            SelectMatch(next, term.Length);
        }
    }

    private void OnReplaceAllClicked(object? sender, EventArgs e)
    {
        var term = FindEntry.Text ?? string.Empty;
        if (term.Length == 0 || _shownTab is null || _shownTab.IsReadOnly)
        {
            return;
        }

        var replacement = ReplaceEntry.Text ?? string.Empty;
        var matches = TextSearch.FindAll(CodeBox.Document.Text, term, _ignoreCase);
        if (matches.Count == 0)
        {
            FindStatus.Text = "No matches";
            return;
        }

        // Back to front so earlier offsets stay valid. Each replacement is its own undo step.
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            CodeBox.SelectedRange = new RichTextRange(matches[i], term.Length);
            CodeBox.Selection.ReplaceText(replacement);
        }

        _lastMatchStart = -1;
        FindStatus.Text = $"Replaced {matches.Count}";
    }
}
