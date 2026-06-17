using Avalonia.Controls;
using Avalonia.Input;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views
{
    public partial class MainWindow : Window
    {
        private HotkeyService? _hotkeys;

        public MainWindow()
        {
            InitializeComponent();
        }

        public void SetupHotkeys(HotkeyService hotkeys, PlayerViewModel player)
        {
            _hotkeys = hotkeys;
            hotkeys.Register(Key.Space, KeyModifiers.None, () => {
                if (player.PlaybackState == Core.Models.PlaybackState.Playing) player.PauseCommand.Execute(null);
                else player.PlayCommand.Execute(null);
            }, "Play / Pause");
            hotkeys.Register(Key.Right, KeyModifiers.Control, () => player.NextCommand.Execute(null), "Next track");
            hotkeys.Register(Key.Left, KeyModifiers.Control, () => player.PreviousCommand.Execute(null), "Previous track");
            hotkeys.Register(Key.S, KeyModifiers.Control, () => player.ToggleShuffleCommand.Execute(null), "Toggle shuffle");
            hotkeys.Register(Key.R, KeyModifiers.Control, () => player.CycleRepeatCommand.Execute(null), "Cycle repeat");
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_hotkeys?.HandleKeyDown(e.Key, e.KeyModifiers) == true)
                e.Handled = true;
            base.OnKeyDown(e);
        }
    }
}
