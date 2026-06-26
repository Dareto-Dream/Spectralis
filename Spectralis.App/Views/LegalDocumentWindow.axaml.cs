using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.App.Services;

namespace Spectralis.App.Views;

public enum LegalDocumentKind
{
    TermsOfService,
    PrivacyPolicy,
}

public partial class LegalDocumentWindow : Window
{
    public LegalDocumentWindow()
        : this(LegalDocumentKind.TermsOfService)
    {
    }

    public LegalDocumentWindow(LegalDocumentKind kind)
    {
        InitializeComponent();
        Title = DocumentTitle(kind);
        TitleText.Text = DocumentTitle(kind);
        DocumentText.Text = LoadDocumentText(kind);
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not null)
        {
            await Clipboard.SetTextAsync(DocumentText.Text ?? string.Empty);
        }
    }

    private void OnOpenPage(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = ResolveLegalPagePath();
            Process.Start(new ProcessStartInfo
            {
                FileName = path ?? "https://deltavdevs.com/legal.html",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            SpectralisLog.Error("Failed to open legal page.", ex);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private static string DocumentTitle(LegalDocumentKind kind) =>
        kind == LegalDocumentKind.TermsOfService
            ? "Spectralis Terms of Service"
            : "Spectralis Privacy Policy";

    private static string MarkdownFileName(LegalDocumentKind kind) =>
        kind == LegalDocumentKind.TermsOfService
            ? "terms-of-service.md"
            : "privacy-policy.md";

    private static string LoadDocumentText(LegalDocumentKind kind)
    {
        var path = ResolveLegalMarkdownPath(kind);
        return path is not null && File.Exists(path)
            ? MarkdownToPlainText(File.ReadAllText(path))
            : DocumentTitle(kind);
    }

    private static string? ResolveLegalMarkdownPath(LegalDocumentKind kind)
    {
        var relativePath = Path.Combine("docs", "legal", MarkdownFileName(kind));
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, relativePath),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", relativePath)),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", relativePath)),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ResolveLegalPagePath()
    {
        const string legalPageFileName = "legal.html";
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, legalPageFileName),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", legalPageFileName)),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", legalPageFileName)),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string MarkdownToPlainText(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder(markdown.Length);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                line = line.TrimStart('#').TrimStart();
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                line = "  * " + line[2..];
            }

            line = Regex.Replace(line, @"`([^`]+)`", "$1");
            line = Regex.Replace(line, @"\[(?<text>[^\]]+)\]\([^)]+\)", "${text}");
            builder.AppendLine(line);
        }

        return builder.ToString().Trim();
    }
}
