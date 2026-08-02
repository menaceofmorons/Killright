using System.Net;
using System.Net.Http;
using System.Windows;
using Killright.Integration.zKill.History;

namespace Killright.UI.DeveloperTools.GroupDetectionHistoryPilot;

public partial class GroupDetectionHistoryPilotWindow : Window
{
    public GroupDetectionHistoryPilotWindow()
    {
        InitializeComponent();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        ResultTextBlock.Text = "Running history volume pilot...";

        try
        {
            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var httpClient = new HttpClient(handler);
            var client = new ZkillHistoryClient(httpClient);
            var result = await client.CountPreviousCompleteMonthAsync();

            ResultTextBlock.Text =
                $"Month: {result.MonthLabel}\n" +
                $"Successful days: {result.SuccessfulDays}\n" +
                $"Failed days: {result.FailedDays}\n" +
                $"Rows returned: {result.TotalRows:N0}";
        }
        catch (Exception ex)
        {
            ResultTextBlock.Text = $"History volume pilot failed: {ex.Message}";
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }
}