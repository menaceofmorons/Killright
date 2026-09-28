using System.ComponentModel;
using System.Windows;
using Killright.Shared.Sde;
using Killright.Storage.Sde;
using Killright.UI.Resources;

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
        StageTextBlock.Text = UiText.SdeStageCheckingManifest;

        var progress = new Progress<SdeCheckProgress>(OnProgress);
        var outcome = await _ingestionService.RunCheckAsync(progress);

        if (outcome is SdeCheckOutcome.ManifestFailure or SdeCheckOutcome.DownloadFailure or SdeCheckOutcome.UnexpectedFailure)
        {
            StageTextBlock.Text = UiText.SdeLoadFailedTitle;
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
                StageTextBlock.Text = UiText.SdeStageCheckingManifest;
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                break;
            case SdeCheckStage.Downloading:
                StageTextBlock.Text = UiText.SdeStageDownloading;
                DownloadProgressBar.Visibility = Visibility.Visible;

                if (progress.DownloadFraction is { } fraction)
                    DownloadProgressBar.Value = fraction;

                break;
            case SdeCheckStage.Importing:
                StageTextBlock.Text = UiText.SdeStageImporting;
                DownloadProgressBar.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private static string DescribeFailure(SdeCheckOutcome outcome)
    {
        return outcome switch
        {
            SdeCheckOutcome.ManifestFailure => UiText.SdeFailureManifest,
            SdeCheckOutcome.DownloadFailure => UiText.SdeFailureDownload,
            _ => UiText.SdeFailureUnexpected
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!_loadSucceeded)
            e.Cancel = true;
    }
}
