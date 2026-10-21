using Android.App;
using Android.Content;
using Android.Content.PM;

namespace SodiumStudio;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize |
                           ConfigChanges.Density | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
public class MainActivity : MauiAppCompatActivity
{
    private const int PickerRequestCode = 0x5D01;

    private TaskCompletionSource<Intent?>? _pendingPicker;

    /// <summary>
    /// Starts a system picker (folder, open document, create document) and completes with the
    /// result intent, or null if the user cancelled. Only one picker is active at a time.
    /// ConfigurationChanges above keeps the activity alive across rotation/resizing; if Android
    /// kills the process while the picker is open, the pending result is lost and the user
    /// simply starts the action again.
    /// </summary>
    public Task<Intent?> LaunchPickerAsync(Intent intent)
    {
        _pendingPicker?.TrySetResult(null);

        var completion = new TaskCompletionSource<Intent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingPicker = completion;
        StartActivityForResult(intent, PickerRequestCode);
        return completion.Task;
    }

    public Task<Intent?> PickFolderAsync(Intent intent) => LaunchPickerAsync(intent);

#pragma warning disable CA1422, CS0672 // OnActivityResult is deprecated in newer API levels but still delivered.
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == PickerRequestCode)
        {
            var completion = _pendingPicker;
            _pendingPicker = null;
            completion?.TrySetResult(resultCode == Result.Ok ? data : null);
        }
    }
#pragma warning restore CA1422, CS0672
}
