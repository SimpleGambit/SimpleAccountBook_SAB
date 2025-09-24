using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SimpleAccountBook;

public partial class MainWindow
{
    private void ListBoxItem_RightClickSelect(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item)
        {
            return;
        }

        if (!item.IsSelected)
        {
            item.IsSelected = true;
        }

        item.Focus();
    }

    private void CopyThisLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        var logEntry = menuItem.Tag as string ?? StatusLogList.SelectedItem as string;

        if (string.IsNullOrWhiteSpace(logEntry))
        {
            return;
        }

        try
        {
            Clipboard.SetText(logEntry);
        }
        catch (Exception ex)
        {
            _viewModel.StatusLog.Add($"클립보드 복사 실패: {ex.Message}");
        }
    }

    private void CopyAllLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.StatusLog.Count == 0)
        {
            return;
        }

        try
        {
            var combinedLogs = string.Join(Environment.NewLine, _viewModel.StatusLog);
            Clipboard.SetText(combinedLogs);
        }
        catch (Exception ex)
        {
            _viewModel.StatusLog.Add($"클립보드 복사 실패: {ex.Message}");
        }
    }
}