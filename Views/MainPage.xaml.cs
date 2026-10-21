using System.ComponentModel;
using SodiumStudio.ViewModels;

namespace SodiumStudio.Views;

public partial class MainPage : ContentPage
{
    // Below this width (in dp) the explorer becomes an overlay drawer (phones).
    private const double CompactBreakpoint = 720;

    private readonly MainViewModel _viewModel;
    private bool _initialized;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _viewModel.Explorer.InitializeAsync(); // restores the previous folder; reports errors in the UI
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0)
        {
            return;
        }

        _viewModel.IsCompact = width < CompactBreakpoint;
        ApplyLayout();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_viewModel.IsCompact && _viewModel.IsExplorerOpen)
        {
            _viewModel.IsExplorerOpen = false;
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsCompact) or nameof(MainViewModel.IsExplorerOpen))
        {
            ApplyLayout();
        }
    }

    /// <summary>
    /// Phone: workspace fills the window and the explorer is a drawer over a scrim.
    /// Tablet / Chromebook: explorer is a persistent left pane next to the workspace.
    /// </summary>
    private void ApplyLayout()
    {
        var width = Width;
        if (width <= 0)
        {
            return;
        }

        if (_viewModel.IsCompact)
        {
            Grid.SetColumn(WorkspacePane, 0);
            Grid.SetColumnSpan(WorkspacePane, 2);

            Grid.SetColumn(ExplorerPane, 0);
            Grid.SetColumnSpan(ExplorerPane, 2);
            ExplorerPane.HorizontalOptions = LayoutOptions.Start;
            ExplorerPane.WidthRequest = Math.Min(320, width * 0.85);
            ExplorerPane.IsVisible = _viewModel.IsExplorerOpen;

            Scrim.IsVisible = _viewModel.IsExplorerOpen;
        }
        else
        {
            Grid.SetColumn(WorkspacePane, 1);
            Grid.SetColumnSpan(WorkspacePane, 1);

            Grid.SetColumn(ExplorerPane, 0);
            Grid.SetColumnSpan(ExplorerPane, 1);
            ExplorerPane.HorizontalOptions = LayoutOptions.Fill;
            ExplorerPane.WidthRequest = width >= 1000 ? 360 : 300;
            ExplorerPane.IsVisible = true;

            Scrim.IsVisible = false;
        }
    }
}
