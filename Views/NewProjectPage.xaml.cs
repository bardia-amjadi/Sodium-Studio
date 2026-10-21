using SodiumStudio.ViewModels;

namespace SodiumStudio.Views;

public partial class NewProjectPage : ContentPage
{
    private readonly NewProjectViewModel _viewModel;

    public NewProjectPage(NewProjectViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>The Android back button cancels the wizard.</summary>
    protected override bool OnBackButtonPressed()
    {
        _viewModel.CancelCommand.Execute(null);
        return true;
    }
}
