using SodiumStudio.ViewModels;

namespace SodiumStudio.Views;

public partial class ProjectSettingsPage : ContentPage
{
    private readonly ProjectSettingsViewModel _viewModel;

    public ProjectSettingsPage(ProjectSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>The Android back button cancels without saving.</summary>
    protected override bool OnBackButtonPressed()
    {
        _viewModel.CancelCommand.Execute(null);
        return true;
    }
}
