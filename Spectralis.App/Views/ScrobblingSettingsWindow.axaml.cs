using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.App.Services;
using Spectralis.Core.Scrobbling;

namespace Spectralis.App.Views;

/// <summary>Last.fm browser-auth linking and ListenBrainz token settings.</summary>
public partial class ScrobblingSettingsWindow : Window
{
    private AppSettings? _settings;
    private string? _lfmPendingToken;

    public ScrobblingSettingsWindow()
    {
        InitializeComponent();
    }

    public static async Task ShowAsync(Window owner, AppSettings settings)
    {
        var window = new ScrobblingSettingsWindow { _settings = settings };
        window.Populate();
        await window.ShowDialog(owner);
    }

    private void Populate()
    {
        if (_settings is null)
        {
            return;
        }

        LfmEnabledCheck.IsChecked = _settings.LastFmEnabled;
        LfmApiKeyBox.Text = _settings.LastFmApiKey;
        LfmApiSecretBox.Text = _settings.LastFmApiSecret;
        LfmStatus.Text = string.IsNullOrWhiteSpace(_settings.LastFmSessionKey)
            ? "Not linked."
            : $"Linked as {_settings.LastFmUsername}.";

        LbzEnabledCheck.IsChecked = _settings.ListenBrainzEnabled;
        LbzTokenBox.Text = _settings.ListenBrainzToken;
        LbzStatus.Text = string.IsNullOrWhiteSpace(_settings.ListenBrainzUsername)
            ? ""
            : $"Validated as {_settings.ListenBrainzUsername}.";
    }

    private async void OnLfmConnect(object? sender, RoutedEventArgs e)
    {
        var apiKey = LfmApiKeyBox.Text?.Trim() ?? "";
        var apiSecret = LfmApiSecretBox.Text?.Trim() ?? "";
        if (apiKey.Length == 0 || apiSecret.Length == 0)
        {
            LfmStatus.Text = "Enter your Last.fm API key and secret first (last.fm/api/account/create).";
            return;
        }

        LfmStatus.Text = "Requesting token...";
        try
        {
            var token = await LastFmClient.GetTokenAsync(apiKey, apiSecret);
            if (token is null)
            {
                LfmStatus.Text = "Failed to get token.";
                return;
            }

            _lfmPendingToken = token;
            Process.Start(new ProcessStartInfo
            {
                FileName = LastFmClient.BuildAuthorizeUrl(apiKey, token),
                UseShellExecute = true,
            });
            LfmCompleteButton.IsEnabled = true;
            LfmStatus.Text = "Authorize Spectralis in your browser, then click Complete Link.";
        }
        catch (Exception ex)
        {
            LfmStatus.Text = $"Token request failed: {ex.Message}";
        }
    }

    private async void OnLfmComplete(object? sender, RoutedEventArgs e)
    {
        if (_settings is null || _lfmPendingToken is null)
        {
            return;
        }

        var apiKey = LfmApiKeyBox.Text?.Trim() ?? "";
        var apiSecret = LfmApiSecretBox.Text?.Trim() ?? "";
        LfmStatus.Text = "Completing authorization...";
        try
        {
            var (sessionKey, username) = await LastFmClient.GetSessionAsync(apiKey, apiSecret, _lfmPendingToken);
            if (sessionKey is null)
            {
                LfmStatus.Text = "Authorization not granted yet. Try again after authorizing.";
                return;
            }

            _settings.LastFmSessionKey = sessionKey;
            _settings.LastFmUsername = username ?? "";
            _lfmPendingToken = null;
            LfmCompleteButton.IsEnabled = false;
            LfmStatus.Text = $"Linked as {_settings.LastFmUsername}.";
        }
        catch (Exception ex)
        {
            LfmStatus.Text = $"Authorization failed: {ex.Message}";
        }
    }

    private void OnLfmUnlink(object? sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.LastFmSessionKey = "";
        _settings.LastFmUsername = "";
        LfmStatus.Text = "Not linked.";
    }

    private async void OnLbzValidate(object? sender, RoutedEventArgs e)
    {
        var token = LbzTokenBox.Text?.Trim() ?? "";
        if (token.Length == 0)
        {
            LbzStatus.Text = "Enter your ListenBrainz user token first.";
            return;
        }

        LbzStatus.Text = "Validating...";
        var client = new ListenBrainzClient(token);
        if (await client.ValidateTokenAsync())
        {
            var username = await client.GetUsernameAsync() ?? "";
            if (_settings is not null)
            {
                _settings.ListenBrainzUsername = username;
            }

            LbzStatus.Text = $"Validated as {username}.";
        }
        else
        {
            LbzStatus.Text = "Token is not valid.";
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            Close();
            return;
        }

        _settings.LastFmEnabled = LfmEnabledCheck.IsChecked == true;
        _settings.LastFmApiKey = LfmApiKeyBox.Text?.Trim() ?? "";
        _settings.LastFmApiSecret = LfmApiSecretBox.Text?.Trim() ?? "";
        _settings.ListenBrainzEnabled = LbzEnabledCheck.IsChecked == true;
        _settings.ListenBrainzToken = LbzTokenBox.Text?.Trim() ?? "";
        AppSettingsStore.Save(_settings);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
