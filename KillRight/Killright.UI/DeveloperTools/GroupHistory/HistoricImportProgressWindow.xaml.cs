using System.Windows;
using Killright.Storage.GroupHistory.Models;

namespace Killright.UI.DeveloperTools.GroupHistory;

public partial class HistoricImportProgressWindow : Window
{
    public HistoricImportProgressWindow()
    {
        InitializeComponent();
    }

    public void ShowMessage(string message)
    {
        MessageTextBlock.Text = message;
        ResultTextBox.Text = message;
    }

    public void UpdateProgress(GroupHistoryImportProgress progress)
    {
        ProgressBar.IsIndeterminate = progress.TotalDays <= 0;

        if (progress.TotalDays > 0)
        {
            ProgressBar.Maximum = progress.TotalDays;
            ProgressBar.Value = progress.CompletedDays;
        }

        ResultTextBox.Text =
            $"{progress.Message}\r\n" +
            $"Completed: {progress.CompletedDays}/{progress.TotalDays}\r\n" +
            $"Succeeded: {progress.SuccessfulDays}\r\n" +
            $"Failed: {progress.FailedDays}";
    }
}