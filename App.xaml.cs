using SodiumStudio.Views;

namespace SodiumStudio;

public partial class App : Application
{
    private readonly MainPage _mainPage;

    public App(MainPage mainPage)
    {
        InitializeComponent();
        _mainPage = mainPage;

        // Visual Studio-style dark theme only; no light variant in Phase 1.
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new Window(_mainPage) { Title = "Sodium Studio" };
}
