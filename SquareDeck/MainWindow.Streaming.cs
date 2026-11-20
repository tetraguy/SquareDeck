using System.Windows;
using SquareDeck.Screens;
using SquareDeck.Services;

namespace SquareDeck;

/// <summary>The pill at the bottom left while a streaming service is kept open: its logo and what's on.</summary>
public partial class MainWindow
{
    private string? _streamLogoFor;

    private void StartStreamPill()
    {
        StreamingScreen.StateChanged += () => Dispatcher.BeginInvoke(UpdateStreamPill);
        UpdateStreamPill();
    }

    private void UpdateStreamPill()
    {
        var open = StreamingScreen.Current;
        // Only while it's in the background: on screen it's the whole screen anyway.
        bool show = open != null && !IsTop(open);
        StreamButton.Visibility = StreamCloseButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            if (_footerIndex >= 0) SetFooterIndex(Math.Min(_footerIndex, FooterButtons().Count - 1));
            return;
        }
        if (_streamLogoFor != open!.Service.Id)
        {
            _streamLogoFor = open.Service.Id;
            StreamLogo.Child = open.Service.Face(56, 30, 7);
        }
        string what = open.NowTitle.Length > 0 ? open.NowTitle : "Open";
        StreamText.Text = (open.NowPlaying ? "Playing  ·  " : open.NowTitle.Length > 0 ? "Paused  ·  " : "") + what;
        StreamButton.ToolTip = $"{open.Service.Name}: {what}. Press to go back to it.";
    }

    private void StreamOpen_Click(object sender, RoutedEventArgs e)
    {
        if (StreamingScreen.Current is { } open) _ = StreamingScreen.OpenAsync(this, open.Service);
    }

    private void StreamClose_Click(object sender, RoutedEventArgs e)
    {
        if (StreamingScreen.Current is { } open)
        {
            UiSound.Back();
            ShowToast("STREAMING", open.Service.Name + " closed", "", "", "info", remember: false);
            StreamingScreen.CloseKept();
        }
    }
}
