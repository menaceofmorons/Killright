using System.ComponentModel;
using System.Windows;
using Killright.Shared.Sde;
using Killright.Storage.Sde;

namespace Killright.UI.Sde;

public partial class SdeFirstRunLoadWindow : Window
{
    private readonly SdeIngestionService _ingestionService;
    private bool _loadSucceeded;

    public SdeFirstRunLoadWindow(SdeIngestionService ingestionService)
    {
        InitializeComponent();
        _ingestionService = ingestionService;
        Loaded += SdeFirstRunLoadWindow_Loaded;
    }

    private async void SdeFirstRunLoadWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RunLoadAsync();
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        await RunLoadAsync();
    }

    private async Task RunLoadAsync()
    {
        RetryButton.Visibility = Visibility.Collapsed;
        ErrorTextBlock.Visibility = Visibility.Collapsed;
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        StageTextBlock.Text = "Checking manifest...";

        var progress = new Progress<SdeCheckProgress>(OnProgress);
        var outcome = await _ingestionService.RunCheckAsync(progress);

        if (outcome is SdeCheckOutcome.ManifestFailure or SdeCheckOutcome.DownloadFailure or SdeCheckOutcome.UnexpectedFailure)
        {
            StageTextBlock.Text = "Reference data load failed.";
            ErrorTextBlock.Text = DescribeFailure(outcome);
            ErrorTextBlock.Visibility = Visibility.Visible;
            RetryButton.Visibility = Visibility.Visible;
            return;
        }

        _loadSucceeded = true;
        Close();
    }

    private void OnProgress(SdeCheckProgress progress)
    {
        switch (progress.Stage)
        {
            case SdeCheckStage.CheckingManifest:
                StageTextBlock.Text = "Checking manifest...";
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                break;
            case SdeCheckStage.Downloading:
                StageTextBlock.Text = "Downloading reference data...";
                DownloadProgressBar.Visibility = Visibility.Visible;

                if (progress.DownloadFraction is { } fraction)
                    DownloadProgressBar.Value = fraction;

                break;
            case SdeCheckStage.Importing:
                StageTextBlock.Text = "Importing reference data...";
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private static string DescribeFailure(SdeCheckOutcome outcome)
    {
        return outcome switch
        {
            SdeCheckOutcome.ManifestFailure => "Could not reach the CCP static-data manifest.",
            SdeCheckOutcome.DownloadFailure => "The reference-data download failed.",
            _ => "An unexpected error occurred."
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!_loadSucceeded)
            e.Cancel = true;
    }
}
