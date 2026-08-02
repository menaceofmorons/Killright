using System.Net;
using System.Net.Http;
using System.Windows;
using Killright.Integration.zKill.History;

namespace Killright.UI.DeveloperTools.GroupDetectionHistoryPilot;

public partial class GroupDetectionHistoryPilotWindow : Window
{
    private string? _lastResults;

    public GroupDetectionHistoryPilotWindow()
    {
        InitializeComponent();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        CopyResultsButton.IsEnabled = false;
        _lastResults = null;
        ResultTextBox.Text = "Running Winter Nexus quarter history pilot...";

        try
        {
            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var httpClient = new HttpClient(handler);
            var client = new ZkillHistoryClient(httpClient);
            var result = await client.CountWinterNexusQuarterAsync();

            _lastResults = result.ToShareableReport();
            ResultTextBox.Text = _lastResults;
            CopyResultsButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _lastResults = $"History quarter pilot failed: {ex.Message}";
            ResultTextBox.Text = _lastResults;
            CopyResultsButton.IsEnabled = true;
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }

    private void CopyResultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastResults))
            return;

        Clipboard.SetText(_lastResults);
    }
}