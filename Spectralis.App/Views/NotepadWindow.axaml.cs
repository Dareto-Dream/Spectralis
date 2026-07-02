using Avalonia.Controls;

namespace Spectralis.App.Views;

/// <summary>Pop-out for a single notepad. Shares the same <c>NotepadViewModel</c> instance as the
/// docked panel, so edits made here and there stay in sync automatically.</summary>
public partial class NotepadWindow : Window
{
    public NotepadWindow()
    {
        InitializeComponent();
    }
}
