using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Spectralis.Core.Capsule;

namespace Spectralis.App.Views;

public partial class CapsuleTrustWindow : Window
{
    // Friendly descriptions and risk tier for each known capability ID.
    private static readonly Dictionary<string, (string Label, bool IsElevated)> CapabilityInfo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["app.theme.deepControl"]    = ("Override app theme (colors and fonts)", false),
        ["app.layout.deepControl"]   = ("Override app layout and panels", false),
        ["app.chrome.effects"]       = ("Apply window chrome visual effects", false),
        ["visualizer.multiLayer"]    = ("Use multi-layer visualizers", false),
        ["visualizer.wasm"]          = ("Run WebAssembly visualizer code", true),
        ["visualizer.shaderPack"]    = ("Load custom GPU shader packs", false),
        ["webview.localContent"]     = ("Access bundled local web content", false),
        ["webview.networkAccess"]    = ("Access the internet from web content", true),
        ["sharedPlay.hostCapsule"]   = ("Host shared listening sessions", false),
        ["sharedPlay.packageUpload"] = ("Upload capsule packages to listeners", true),
        ["timeline.appControl"]      = ("Control app playback via reactive timeline", false),
        ["album.world"]              = ("Open as an interactive album world", false),
    };

    // Amber from Color.Warning token; muted from Color.Ink.Muted token.
    private static readonly Color ElevatedColor = Color.Parse("#D9A40A");
    private static readonly Color NormalColor = Color.Parse("#5A616C");

    private bool _trusted;

    public CapsuleTrustWindow()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner, CapsuleTrustContext context)
    {
        var window = new CapsuleTrustWindow();
        window.Populate(context);
        await window.ShowDialog(owner);
        return window._trusted;
    }

    private void Populate(CapsuleTrustContext context)
    {
        var creator = context.Creator;
        var displayName = string.IsNullOrWhiteSpace(creator.DisplayName)
            ? "Unknown creator"
            : creator.DisplayName;
        CreatorLabel.Text = $"{displayName}  ·  {creator.Fingerprint}";

        if (context.RequestedCapabilities.Count > 0)
        {
            foreach (var cap in context.RequestedCapabilities)
            {
                if (!CapabilityInfo.TryGetValue(cap, out var info))
                    info = (cap, false);

                var dot = new Border
                {
                    Width = 7,
                    Height = 7,
                    CornerRadius = new CornerRadius(3.5),
                    Background = new SolidColorBrush(info.IsElevated ? ElevatedColor : NormalColor),
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var label = new TextBlock
                {
                    Text = info.Label,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 9,
                };
                row.Children.Add(dot);
                row.Children.Add(label);
                CapabilityRows.Children.Add(row);
            }

            CapabilitiesSection.IsVisible = true;
        }

        if (context.ContentTags.Count > 0)
        {
            ContentTagsPanel.ItemsSource = context.ContentTags;
            ContentTagsSection.IsVisible = true;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnTrust(object? sender, RoutedEventArgs e)
    {
        _trusted = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _trusted = false;
    }
}
