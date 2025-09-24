using Microsoft.Win32;
using SimpleAccountBook.Controls.Calendar;
using SimpleAccountBook.Models;
using SimpleAccountBook.Services;
using SimpleAccountBook.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SimpleAccountBook;

public partial class MainWindow : Window
{
    private readonly AccountBookViewModel _viewModel;
    private readonly MemoService _memoService;
    private readonly AppStateService _appStateService;
    private ScrollViewer? _loadedFileListScrollViewer;
    private bool _isRestoringState;
    private string? _lastStateFilePath;
    private readonly Dictionary<GridViewColumn, ColumnSortState> _loadedFileSortStates;
    private readonly Dictionary<GridViewColumn, ColumnSortState> _detailSearchSortStates;
    private GridViewColumn? _activeLoadedFileSortColumn;
    private GridViewColumn? _activeDetailSearchSortColumn;
    private bool? _loadedFileVerticalScrollVisible;
    private bool _isAdjustingLoadedFileColumns;
    private (double FileName, double FilePath, double TransactionCount, double LoadedAt, double Remove)? _previousLoadedFileColumnWidths;
    private const double FileNameColumnMinWidth = 80d;
    private const double FilePathColumnMinWidth = 200d;
    private const double TransactionCountColumnMinWidth = 70d;
    private const double LoadedAtColumnMinWidth = 150d;
    private const double RemoveColumnWidth = 50d;
    private const double ColumnPaddingCompensation = 5d;
    private const double ColumnWidthAdjustmentTolerance = 0.5d;
    private const string MemoDetailClipboardFormat = "SimpleAccountBook.MemoDetail";

    public static readonly RoutedUICommand SaveCommand = new("저장", "SaveCommand", typeof(MainWindow));
    public static readonly RoutedUICommand SaveAsCommand = new("다른 이름으로 저장", "SaveAsCommand", typeof(MainWindow));
    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new AccountBookViewModel(new ExcelImportService());
        _viewModel.PasswordProvider = RequestPasswordAsync;
        _memoService = new MemoService();
        _appStateService = new AppStateService();
        DataContext = _viewModel;
        _viewModel.StatusLog.CollectionChanged += StatusLog_CollectionChanged;
        InitializeStatusLogContextMenu();

        // ViewModel 속성 변경 이벤트 핸들러
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.BudgetChanged += (_, _) => _ = SaveCurrentStateAsync();

        _memoService.MemosChanged += MemoService_MemosChanged;
        Loaded += MainWindow_Loaded;

        // 커스텀 달력 이벤트 핸들러
        AccountCalendar.SelectedDateChanged += AccountCalendar_SelectedDateChanged;
        AccountCalendar.DisplayDateChanged += AccountCalendar_DisplayDateChanged;
        _detailSearchSortStates = CreateDetailSearchSortStates();
        _loadedFileSortStates = CreateLoadedFileSortStates();
        LoadedFileList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(LoadedFileColumnHeader_Click));
        DetailSearchResultsList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(DetailSearchColumnHeader_Click));
    }

    private void InitializeStatusLogContextMenu()
    {
        if (StatusLogList is null)
        {
            return;
        }
        StatusLogList.ContextMenu = CreateStatusLogContextMenu();
        StatusLogList.Loaded += StatusLogList_Loaded;
        StatusLogList.LayoutUpdated += StatusLogList_LayoutUpdated;
    }

    private void StatusLogList_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.InvokeAsync(AttachStatusLogContextMenuToScrollComponents, DispatcherPriority.Background);
    }

    private void StatusLogList_LayoutUpdated(object? sender, EventArgs e)
    {
        AttachStatusLogContextMenuToScrollComponents();
    }

    private void AttachStatusLogContextMenuToScrollComponents()
    {
        if (StatusLogList is null)
        {
            return;
        }

        if (FindVisualChild<ScrollViewer>(StatusLogList) is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.ApplyTemplate();
        if (scrollViewer.Template.FindName("PART_VerticalScrollBar", scrollViewer) is not ScrollBar verticalScrollBar)
        {
            return;
        }

        verticalScrollBar.ApplyTemplate();
        EnsureStatusLogContextMenu(verticalScrollBar);

        if (verticalScrollBar.Template.FindName("PART_LineUpButton", verticalScrollBar) is FrameworkElement lineUpButton)
        {
            EnsureStatusLogContextMenu(lineUpButton);
        }

        if (verticalScrollBar.Template.FindName("PART_LineDownButton", verticalScrollBar) is FrameworkElement lineDownButton)
        {
            EnsureStatusLogContextMenu(lineDownButton);
        }

        if (verticalScrollBar.Template.FindName("PART_Track", verticalScrollBar) is FrameworkElement track)
        {
            EnsureStatusLogContextMenu(track);
        }

    }

    private void EnsureStatusLogContextMenu(FrameworkElement target)
    {
        if (target.ContextMenu?.Tag is StatusLogContextMenuState)
        {
            return;
        }

        target.ContextMenu = CreateStatusLogContextMenu();
    }

    private sealed class StatusLogContextMenuState
    {
        public StatusLogContextMenuState(MenuItem copyMenuItem, MenuItem copyAllMenuItem)
        {
            CopyMenuItem = copyMenuItem;
            CopyAllMenuItem = copyAllMenuItem;
        }

        public MenuItem CopyMenuItem { get; }
        public MenuItem CopyAllMenuItem { get; }
    }

    private ContextMenu CreateStatusLogContextMenu()
    {
        var contextMenu = new ContextMenu();

        if (TryFindResource("StatusLogContextMenuStyle") is Style contextMenuStyle)
        {
            contextMenu.Style = contextMenuStyle;
        }

        if (TryFindResource("StatusLogContextMenuItemStyle") is Style menuItemStyle)
        {
            contextMenu.ItemContainerStyle = menuItemStyle;
        }

        var copyMenuItem = new MenuItem
        {
            Header = "이 로그 복사"
        };
        copyMenuItem.Click += CopyThisLog_Click;

        var copyAllMenuItem = new MenuItem
        {
            Header = "모든 로그 복사"
        };
        copyAllMenuItem.Click += CopyAllLogs_Click;

        contextMenu.Items.Add(copyMenuItem);
        contextMenu.Items.Add(copyAllMenuItem);
        contextMenu.Tag = new StatusLogContextMenuState(copyMenuItem, copyAllMenuItem);
        contextMenu.Opened += StatusLogContextMenu_Opened;

        return contextMenu;
    }

    private void StatusLogContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (StatusLogList is null || sender is not ContextMenu contextMenu)
        {
            return;
        }

        if (contextMenu.Tag is not StatusLogContextMenuState state)
        {
            return;
        }

        var selectedLog = StatusLogList.SelectedItem as string;
        state.CopyMenuItem.Tag = selectedLog;
        state.CopyMenuItem.IsEnabled = !string.IsNullOrWhiteSpace(selectedLog);
        state.CopyAllMenuItem.IsEnabled = _viewModel.StatusLog.Count > 0;
    }
    private void SaveCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = true;
    }

    private async void SaveCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        await SaveCurrentStateAsync();
    }
    private async void SaveAsCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        await SaveCurrentStateAsync(true, true);
    }

    /// <summary>
    /// 환경설정 메뉴 클릭 이벤트 - 테마 설정 창을 엽니다
    /// </summary>
    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void BudgetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy || _isRestoringState)
        {
            MessageBox.Show("파일 처리가 끝난 뒤 다시 열어 주세요.", "예상 지출 및 통계", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new BudgetWindow(_viewModel) { Owner = this }.ShowDialog();
    }

    /// <summary>
    /// 테마 변경 시 달력 색상을 업데이트합니다 (외부에서 호출 가능)
    /// </summary>
    public void UpdateCalendarTheme()
    {
        // 달력 업데이트
        AccountCalendar?.UpdateCalendar();
        
        // 전체 UI 시각적 업데이트
        Dispatcher.InvokeAsync(() =>
        {
            // 전체 윈도우 다시 그리기
            this.InvalidateVisual();
            
            // ListView 다시 그리기
            LoadedFileList?.InvalidateVisual();
            DetailSearchResultsList?.InvalidateVisual();
            
            // 레이아웃 업데이트
            this.UpdateLayout();
        }, DispatcherPriority.Render);
    }

    private void AccountCalendar_SelectedDateChanged(object? sender, DateTime? selectedDate)
    {
        if (selectedDate.HasValue && _viewModel != null)
        {
            _viewModel.SelectedDate = selectedDate.Value;
            UpdateSelectedDateTransactionsWithMemo();
        }
    }

    private void AccountCalendar_DisplayDateChanged(object? sender, DateTime displayDate)
    {
        if (_viewModel != null)
        {
            _viewModel.CurrentMonth = displayDate;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountBookViewModel.DailySummaries) ||
            e.PropertyName == nameof(AccountBookViewModel.DailySummaryVersion))
        {
            System.Diagnostics.Debug.WriteLine("[MainWindow] DailySummaries 업데이트됨");
            UpdateCalendarData();
        }
        else if (e.PropertyName == nameof(AccountBookViewModel.SelectedDate))
        {
            if (_viewModel.SelectedDate != AccountCalendar.SelectedDate)
            {
                AccountCalendar.SelectedDate = _viewModel.SelectedDate;
                UpdateSelectedDateTransactionsWithMemo();
            }
        }
        else if (e.PropertyName == nameof(AccountBookViewModel.CurrentMonth))
        {
            if (_viewModel.CurrentMonth != AccountCalendar.DisplayDate)
            {
                AccountCalendar.DisplayDate = _viewModel.CurrentMonth;
            }
        }
        else if (e.PropertyName == nameof(AccountBookViewModel.SelectedDateTransactions))
        {
            UpdateSelectedDateTransactionsWithMemo();
        }
    }
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadStateFromDiskAsync(false);
    }

    private void MemoService_MemosChanged(object? sender, EventArgs e)
    {
        if (_isRestoringState)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_viewModel.DetailSearchQuery))
        {
            PerformDetailSearch();
        }

        UpdateCalendarDetailMismatchIndicators();

        _ = SaveCurrentStateAsync();
    }

    private void StatusLog_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add &&
            e.Action != NotifyCollectionChangedAction.Reset)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (StatusLogList.Items.Count == 0)
            {
                return;
            }

            var lastItem = StatusLogList.Items[StatusLogList.Items.Count - 1];
            StatusLogList.ScrollIntoView(lastItem);

            if (FindVisualChild<ScrollViewer>(StatusLogList) is { } listScrollViewer)
            {
                listScrollViewer.ScrollToEnd();
            }
        }, DispatcherPriority.Background);
    }

    /// 선택된 날짜의 거래 목록을 메모 기능이 포함된 모델로 변환
    private void UpdateSelectedDateTransactionsWithMemo()
    {
        var transactionsWithMemo = new ObservableCollection<TransactionWithMemo>();
        
        foreach (var transaction in _viewModel.SelectedDateTransactions)
        {
            var withMemo = new TransactionWithMemo(transaction);
            
            // 저장된 메모가 있으면 로드
            var savedMemo = _memoService.GetMemo(transaction);
            if (savedMemo != null)
            {
                withMemo.Memo = savedMemo;
            }
            
            // 메모 변경 이벤트 구독
            withMemo.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TransactionWithMemo.HasDetailAmountMismatch))
                {
                    UpdateCalendarDetailMismatchIndicators();
                }

                if (e.PropertyName == nameof(TransactionWithMemo.Memo) ||
                    e.PropertyName == nameof(TransactionWithMemo.IsExpanded))
                {
                    // 메모가 변경되면 자동 저장
                    if (withMemo.HasMemo)
                    {
                        _memoService.SaveMemo(withMemo.Transaction, withMemo.Memo);
                    }

                    UpdateCalendarDetailMismatchIndicators();
                }
            };
            
            transactionsWithMemo.Add(withMemo);
        }
        
        // ViewModel의 SelectedDateTransactionsWithMemo 속성 업데이트
        _viewModel.SetSelectedDateTransactionsWithMemo(transactionsWithMemo);
        UpdateCalendarDetailMismatchIndicators();
    }
    private void AddManualTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDate is not DateTime selectedDate)
        {
            MessageBox.Show("거래를 추가할 날짜를 먼저 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newTransaction = _viewModel.AddManualTransaction(selectedDate);

        UpdateSelectedDateTransactionsWithMemo();

        var added = _viewModel.SelectedDateTransactionsWithMemo
            .FirstOrDefault(t => ReferenceEquals(t.Transaction, newTransaction));

        if (added is not null)
        {
            added.IsExpanded = true;
        }
    }

    private void DeleteTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.Tag is not TransactionWithMemo withMemo)
        {
            return;
        }

        var result = MessageBox.Show(
            "선택한 거래를 삭제하시겠습니까?",
            "거래 삭제",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (_viewModel.RemoveTransaction(withMemo.Transaction))
        {
            _memoService.DeleteMemo(withMemo.Transaction);
            UpdateSelectedDateTransactionsWithMemo();
        }
        else
        {
            MessageBox.Show("거래를 삭제하지 못했습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleExcludeTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.Tag is not TransactionWithMemo withMemo)
        {
            return;
        }

        withMemo.IsExcludedFromTotal = !withMemo.IsExcludedFromTotal;
        _viewModel.RecalculateTotals();
    }

    private void UpdateCalendarData()
    {
        // 커스텀 달력에 데이터 전달
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            System.Diagnostics.Debug.WriteLine("[MainWindow] UpdateCalendarData 시작");
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 일별 요약 수: {_viewModel.DailySummaries.Count}");
            
            // 커스텀 달력에 데이터 설정
            AccountCalendar.SetDailySummaries(_viewModel.DailySummaries);
            AccountCalendar.SetDetailMismatchDates(CalculateDetailMismatchDates());
            System.Diagnostics.Debug.WriteLine("[MainWindow] 달력 데이터 업데이트 완료");
        }));
    }
    private IReadOnlyCollection<DateOnly> CalculateDetailMismatchDates()
    {
        var mismatchDates = new HashSet<DateOnly>();

        foreach (var transaction in _viewModel.Transactions)
        {
            var memo = _memoService.GetMemo(transaction);
            if (memo is null || memo.Details.Count == 0)
            {
                continue;
            }

            if (memo.TotalDetailAmount != transaction.Amount)
            {
                mismatchDates.Add(DateOnly.FromDateTime(transaction.TransactionTime));
            }
        }

        foreach (var withMemo in _viewModel.SelectedDateTransactionsWithMemo)
        {
            if (!withMemo.HasMemo || !withMemo.HasDetailAmountMismatch)
            {
                continue;
            }

            mismatchDates.Add(DateOnly.FromDateTime(withMemo.Transaction.TransactionTime));
        }

        return mismatchDates;
    }

    private void UpdateCalendarDetailMismatchIndicators()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            AccountCalendar.SetDetailMismatchDates(CalculateDetailMismatchDates());
        }));
    }

    /// 메모 상세 항목 추가
    private void AddMemoDetail_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var transaction = button?.Tag as TransactionWithMemo;
        
        if (transaction != null)
        {
            var newDetail = new MemoDetail
            {
                ItemName = string.Empty,
                Amount = 0,
                Note = string.Empty
            };

            var transactionAmount = Math.Abs(transaction.Transaction.Amount);
            if (transaction.Memo.Details.Count == 0 && transactionAmount > 0)
            {
                newDetail.Amount = transactionAmount;
            }

            transaction.Memo.Details.Add(newDetail);
            
            // 메모 저장
            _memoService.SaveMemo(transaction.Transaction, transaction.Memo);
        }
    }
    private void CopyMemoDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MemoDetail detail })
        {
            return;
        }

        var clipboardData = new MemoDetailClipboardData
        {
            ItemName = detail.ItemName ?? string.Empty,
            Amount = detail.Amount,
            Note = detail.Note ?? string.Empty
        };

        try
        {
            var serialized = JsonSerializer.Serialize(clipboardData);
            var dataObject = new DataObject();
            dataObject.SetData(MemoDetailClipboardFormat, serialized);

            static string SanitizeForClipboardText(string value) =>
                value.Replace('\t', ' ')
                     .Replace('\r', ' ')
                     .Replace('\n', ' ');

            var fallbackText = string.Join('\t',
                SanitizeForClipboardText(clipboardData.ItemName),
                clipboardData.Amount.ToString(CultureInfo.InvariantCulture),
                SanitizeForClipboardText(clipboardData.Note));

            dataObject.SetData(DataFormats.UnicodeText, fallbackText);
            Clipboard.SetDataObject(dataObject, true);
        }
        catch (COMException ex)
        {
            MessageBox.Show(
                $"상세 내역을 클립보드에 복사하지 못했습니다.{Environment.NewLine}{ex.Message}",
                "복사 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PasteMemoDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TransactionWithMemo transaction })
        {
            return;
        }

        if (!TryGetMemoDetailFromClipboard(out var clipboardDetail, out var failureMessage))
        {
            var hasErrorMessage = !string.IsNullOrWhiteSpace(failureMessage);
            var message = hasErrorMessage
                ? failureMessage!
                : "붙여넣을 수 있는 상세 항목이 클립보드에 없습니다.";

            MessageBox.Show(
                message,
                "붙여넣기 실패",
                MessageBoxButton.OK,
                hasErrorMessage ? MessageBoxImage.Error : MessageBoxImage.Information);
            return;
        }

        var newDetail = new MemoDetail
        {
            ItemName = clipboardDetail.ItemName ?? string.Empty,
            Amount = Math.Abs(clipboardDetail.Amount),
            Note = clipboardDetail.Note ?? string.Empty
        };

        transaction.Memo.Details.Add(newDetail);
        _memoService.SaveMemo(transaction.Transaction, transaction.Memo);
    }

    private bool TryGetMemoDetailFromClipboard([NotNullWhen(true)] out MemoDetailClipboardData? detail, out string? failureMessage)
    {
        detail = null;
        failureMessage = null;

        try
        {
            if (Clipboard.ContainsData(MemoDetailClipboardFormat))
            {
                if (Clipboard.GetData(MemoDetailClipboardFormat) is string raw && !string.IsNullOrWhiteSpace(raw))
                {
                    var parsed = JsonSerializer.Deserialize<MemoDetailClipboardData>(raw);
                    if (parsed != null)
                    {
                        detail = parsed;
                        return true;
                    }
                }
            }
            else if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var segments = text.Split('\t');
                    if (segments.Length >= 2)
                    {
                        var amountText = segments.Length > 1 ? segments[1] : string.Empty;
                        if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) &&
                            !decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
                        {
                            amount = 0m;
                        }

                        detail = new MemoDetailClipboardData
                        {
                            ItemName = segments[0].Trim(),
                            Amount = amount,
                            Note = segments.Length > 2 ? segments[2].Trim() : string.Empty
                        };
                        return true;
                    }
                }
            }
        }
        catch (COMException ex)
        {
            failureMessage = $"클립보드에 접근하지 못했습니다.{Environment.NewLine}{ex.Message}";
            return false;
        }
        catch (JsonException)
        {
            // 잘못된 JSON 형식은 무시하고 텍스트 기반 처리로 넘어갑니다.
        }

        return false;
    }
    /// 메모 상세 항목 삭제
    private void RemoveMemoDetail_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.Tag is not MemoDetail detail)
        {
            return;
        }

        // 상위 TransactionWithMemo 찾기
        var itemsControl = FindParent<ItemsControl>(button);
        if (itemsControl?.DataContext is TransactionWithMemo transaction)
        {
            transaction.Memo.Details.Remove(detail);

            // 메모 저장
            _memoService.SaveMemo(transaction.Transaction, transaction.Memo);
        }
    }

    private void AmountTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!IsDigitsOnly(e.Text))
        {
            e.Handled = true;
        }
    }

    private void AmountTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            e.Handled = true;
            return;
        }

        if (textBox.SelectionLength > 0)
        {
            return;
        }

        if (e.Key is Key.Back or Key.Delete)
        {
            HandleCommaDeletion(textBox, e.Key == Key.Back, e);
        }
    }

    private void AmountTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        var sanitized = pastedText.Replace(",", string.Empty).Trim();

        if (!IsDigitsOnly(sanitized))
        {
            e.CancelCommand();
        }
    }

    private static void HandleCommaDeletion(TextBox textBox, bool isBackspace, KeyEventArgs keyEventArgs)
    {
        var text = textBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var caretIndex = textBox.CaretIndex;
        var commaIndex = GetCommaIndex(text, caretIndex, isBackspace);

        if (commaIndex is null)
        {
            return;
        }

        var builder = new StringBuilder(text);
        var targetIndex = Math.Min(commaIndex.Value, builder.Length - 1);

        if (isBackspace)
        {
            if (targetIndex >= 0 && targetIndex < builder.Length)
            {
                builder.Remove(targetIndex, 1);
            }

            var precedingIndex = targetIndex - 1;
            if (precedingIndex >= 0 && precedingIndex < builder.Length && char.IsDigit(builder[precedingIndex]))
            {
                builder.Remove(precedingIndex, 1);
                caretIndex = precedingIndex;
            }
            else
            {
                caretIndex = Math.Max(precedingIndex + 1, 0);
            }
        }
        else
        {
            if (targetIndex >= 0 && targetIndex < builder.Length)
            {
                builder.Remove(targetIndex, 1);
            }

            if (targetIndex >= 0 && targetIndex < builder.Length && char.IsDigit(builder[targetIndex]))
            {
                builder.Remove(targetIndex, 1);
            }

            caretIndex = Math.Min(targetIndex, builder.Length);
        }

        var newText = builder.ToString();
        if (text.Equals(newText, StringComparison.Ordinal))
        {
            builder.Remove(commaIndex.Value, 1);
        }

        textBox.Text = newText;
        textBox.CaretIndex = Math.Clamp(caretIndex, 0, newText.Length);
        keyEventArgs.Handled = true;
    }

    private static int? GetCommaIndex(string text, int caretIndex, bool isBackspace)
    {
        if (isBackspace)
        {
            if (caretIndex <= 0 || caretIndex > text.Length)
            {
                return null;
            }

            return text[caretIndex - 1] == ',' ? caretIndex - 1 : null;
        }

        if (caretIndex < 0 || caretIndex >= text.Length)
        {
            return null;
        }

        return text[caretIndex] == ',' ? caretIndex : null;
    }

    private static bool IsDigitsOnly(string text)
    {
        foreach (var character in text)
        {
            if (!char.IsDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// Visual Tree에서 부모 요소 찾기
    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        if (child is null) { return null; }

        var parentObject = VisualTreeHelper.GetParent(child);

        if (parentObject == null) { return null; }
        if (parentObject is T parent) { return parent; }

        return FindParent<T>(parentObject);
    }
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var supportedFiles = files.Where(IsSupportedFile).ToArray();

        if (supportedFiles.Length == 0)
        {
            MessageBox.Show("지원되지 않는 형식입니다. Excel(.xlsx/.xlsm/.xls) 또는 PDF 파일을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await LoadFilesAsync(supportedFiles);
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            e.Effects = files.Any(IsSupportedFile) ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Supported Files (*.xlsx;*.xlsm;*.xls;*.pdf)|*.xlsx;*.xlsm;*.xls;*.pdf",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadFilesAsync(dialog.FileNames);
        }
    }

    private async Task LoadFilesAsync(IEnumerable<string> filePaths)
    {
        bool hasChanges = false;

        foreach (var path in filePaths.Select(Path.GetFullPath))
        {
            if (!File.Exists(path))
            {
                MessageBox.Show($"파일을 찾을 수 없습니다: {path}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                continue;
            }

            try
            {
                await _viewModel.LoadFromFileAsync(path);
                hasChanges = true;
            }
            catch (DocumentPasswordException ex)
            {
                var fileName = Path.GetFileName(path);
                if (!ex.HadPassword && !ex.PromptAttempted)
                {
                    var providedPassword = await RequestPasswordAsync(fileName, ex.IsInvalidPassword);
                    if (!string.IsNullOrEmpty(providedPassword))
                    {
                        try
                        {
                            await _viewModel.LoadFromFileAsync(path, providedPassword);
                            hasChanges = true;
                            continue;
                        }
                        catch (DocumentPasswordException retryEx)
                        {
                            MessageBox.Show($"{fileName} 파일을 불러오지 못했습니다.\n{retryEx.Message}", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"{fileName} 비밀번호 입력이 취소되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show($"{fileName} 파일을 불러오지 못했습니다.\n{ex.Message}", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        if (hasChanges)
        {
            RefreshDataViews();
            await SaveCurrentStateAsync();
        }
    }
    private Task<string?> RequestPasswordAsync(string fileName, bool isRetry)
    {
        if (Dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowPasswordPrompt(fileName, isRetry));
        }

        return Dispatcher.InvokeAsync(() => ShowPasswordPrompt(fileName, isRetry)).Task;
    }

    private string? ShowPasswordPrompt(string fileName, bool isRetry)
    {
        var prompt = new PasswordPromptWindow(fileName, isRetry)
        {
            Owner = this
        };

        var dialogResult = prompt.ShowDialog();
        return dialogResult == true ? prompt.Password : null;
    }

    private void RefreshDataViews()
    {
        System.Diagnostics.Debug.WriteLine($"전체 거래 수: {_viewModel.Transactions.Count}");
        System.Diagnostics.Debug.WriteLine($"일별 요약 수: {_viewModel.DailySummaries.Count}");
        
        // 처음 5개의 일별 요약 출력
        foreach (var summary in _viewModel.DailySummaries.Take(5))
        {
            System.Diagnostics.Debug.WriteLine($"  {summary.Key}: 입금={summary.Value.IncomeAmount:#,0}, 출금={summary.Value.ExpenseAmount:#,0}");
            System.Diagnostics.Debug.WriteLine($"    IncomeText='{summary.Value.IncomeText}', ExpenseText='{summary.Value.ExpenseText}'");
        }
        
        // 달력 데이터 업데이트
        UpdateCalendarData();
        
        // 선택된 날짜의 거래 목록 업데이트
        UpdateSelectedDateTransactionsWithMemo();

        if (!string.IsNullOrWhiteSpace(_viewModel.DetailSearchQuery))
        {
            PerformDetailSearch();
        }

        AdjustLoadedFileColumns();
    }

    private void LoadedFileList_Loaded(object sender, RoutedEventArgs e)
    {
        AdjustLoadedFileColumns();
    }

    private void LoadedFileList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
        {
            return;
        }
        AdjustLoadedFileColumns();
    }

    private Dictionary<GridViewColumn, ColumnSortState> CreateLoadedFileSortStates()
    {
        var states = new Dictionary<GridViewColumn, ColumnSortState>();

        if (FileNameColumn != null)
        {
            states[FileNameColumn] = new ColumnSortState(nameof(LoadedFileInfo.FileName), ListSortDirection.Ascending, ListSortDirection.Descending, null);
        }

        if (TransactionCountColumn != null)
        {
            states[TransactionCountColumn] = new ColumnSortState(nameof(LoadedFileInfo.TransactionCount), ListSortDirection.Descending, ListSortDirection.Ascending, null);
        }

        if (LoadedAtColumn != null)
        {
            states[LoadedAtColumn] = new ColumnSortState(nameof(LoadedFileInfo.LoadedAt), ListSortDirection.Descending, ListSortDirection.Ascending, null);
        }

        return states;
    }
    private Dictionary<GridViewColumn, ColumnSortState> CreateDetailSearchSortStates()
    {
        var states = new Dictionary<GridViewColumn, ColumnSortState>();

        AddState(DetailSearchDateColumn, nameof(DetailSearchResult.TransactionTime), descendingFirst: true);
        AddState(DetailSearchTypeColumn, nameof(DetailSearchResult.TransactionType));
        AddState(DetailSearchTransactionAmountColumn, nameof(DetailSearchResult.TransactionAmount), descendingFirst: true);
        AddState(DetailSearchDescriptionColumn, nameof(DetailSearchResult.Description));
        AddState(DetailSearchCategoryColumn, nameof(DetailSearchResult.Category));
        AddState(DetailSearchItemNameColumn, nameof(DetailSearchResult.ItemName));
        AddState(DetailSearchDetailAmountColumn, nameof(DetailSearchResult.DetailAmount), descendingFirst: true);
        AddState(DetailSearchNoteColumn, nameof(DetailSearchResult.Note));
        AddState(DetailSearchFileColumn, nameof(DetailSearchResult.SourceFileName));

        return states;

        void AddState(GridViewColumn? column, string propertyName, bool descendingFirst = false)
        {
            if (column is null)
            {
                return;
            }

            states[column] = descendingFirst
                ? new ColumnSortState(propertyName, ListSortDirection.Descending, ListSortDirection.Ascending, null)
                : new ColumnSortState(propertyName, ListSortDirection.Ascending, ListSortDirection.Descending, null);
        }
    }
    private void LoadedFileColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header ||
            header.Column is null ||
            !_loadedFileSortStates.TryGetValue(header.Column, out var sortState))
        {
            return;
        }

        if (_viewModel.LoadedFilesView is null)
        {
            return;
        }

        if (_activeLoadedFileSortColumn != header.Column)
        {
            if (_activeLoadedFileSortColumn != null &&
                _loadedFileSortStates.TryGetValue(_activeLoadedFileSortColumn, out var previousState))
            {
                previousState.Reset();
            }

            sortState.Reset();
            _activeLoadedFileSortColumn = header.Column;
        }

        var direction = sortState.MoveNext();

        var sortDescriptions = _viewModel.LoadedFilesView.SortDescriptions;
        sortDescriptions.Clear();

        if (direction.HasValue)
        {
            sortDescriptions.Add(new SortDescription(sortState.PropertyName, direction.Value));
        }
        else
        {
            _activeLoadedFileSortColumn = null;
            sortState.Reset();
        }

        _viewModel.LoadedFilesView.Refresh();
        e.Handled = true;
    }
    private void DetailSearchColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header ||
            header.Column is null ||
            !_detailSearchSortStates.TryGetValue(header.Column, out var sortState))
        {
            return;
        }

        var collectionView = CollectionViewSource.GetDefaultView(DetailSearchResultsList.ItemsSource);
        if (collectionView is null)
        {
            return;
        }

        if (_activeDetailSearchSortColumn != header.Column)
        {
            if (_activeDetailSearchSortColumn != null &&
                _detailSearchSortStates.TryGetValue(_activeDetailSearchSortColumn, out var previousState))
            {
                previousState.Reset();
            }

            sortState.Reset();
            _activeDetailSearchSortColumn = header.Column;
        }

        var direction = sortState.MoveNext();

        var sortDescriptions = collectionView.SortDescriptions;
        sortDescriptions.Clear();

        if (direction.HasValue)
        {
            sortDescriptions.Add(new SortDescription(sortState.PropertyName, direction.Value));

            if (!string.Equals(sortState.PropertyName, nameof(DetailSearchResult.TransactionTime), StringComparison.Ordinal))
            {
                sortDescriptions.Add(new SortDescription(nameof(DetailSearchResult.TransactionTime), ListSortDirection.Descending));
            }
        }
        else
        {
            _activeDetailSearchSortColumn = null;
            sortState.Reset();
            sortDescriptions.Add(new SortDescription(nameof(DetailSearchResult.TransactionTime), ListSortDirection.Descending));
        }

        collectionView.Refresh();
        e.Handled = true;
    }
    private void LoadedFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView listView)
        {
            listView.SelectedItem = null;
        }
    }
    private void AdjustLoadedFileColumns()
    {
        if (_isAdjustingLoadedFileColumns)
        {
            return;
        }
        if (FileNameColumn == null ||
            FilePathColumn == null ||
            TransactionCountColumn == null ||
            LoadedAtColumn == null ||
            RemoveColumn == null)
        {
            return;
        }
        _isAdjustingLoadedFileColumns = true;

        try
        {
            if (_loadedFileListScrollViewer is null)
            {
                _loadedFileListScrollViewer = FindVisualChild<ScrollViewer>(LoadedFileList);

                if (_loadedFileListScrollViewer != null)
                {
                    _loadedFileListScrollViewer.ScrollChanged += LoadedFileListScrollViewer_ScrollChanged;
                }
            }

            var scrollViewer = _loadedFileListScrollViewer;

            double availableWidth;
            bool usedViewportWidth = false;

            if (scrollViewer is not null)
            {
                availableWidth = scrollViewer.ViewportWidth;

                if (!double.IsNaN(availableWidth) && availableWidth > 0)
                {
                    usedViewportWidth = true;
                }
                else
                {
                    availableWidth = LoadedFileList.ActualWidth;
                }
            }
            else
            {
                availableWidth = LoadedFileList.ActualWidth;
            }

            if (!usedViewportWidth && scrollViewer?.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            {
                availableWidth -= SystemParameters.VerticalScrollBarWidth;
            }

            if (double.IsNaN(availableWidth) || availableWidth <= 0)
            {
                return;
            }

            availableWidth = Math.Max(0, availableWidth - ColumnPaddingCompensation);

            var minimumTotalWidth = FileNameColumnMinWidth +
                                    FilePathColumnMinWidth +
                                    TransactionCountColumnMinWidth +
                                    LoadedAtColumnMinWidth +
                                    RemoveColumnWidth;

            if (availableWidth <= 0)
            {
                SetLoadedFileColumnWidths(0, 0, 0, 0, 0);
                return;
            }

            if (availableWidth < minimumTotalWidth)
            {
                var scale = availableWidth / minimumTotalWidth;

                SetLoadedFileColumnWidths(
                   FileNameColumnMinWidth * scale,
                   FilePathColumnMinWidth * scale,
                   TransactionCountColumnMinWidth * scale,
                   LoadedAtColumnMinWidth * scale,
                   RemoveColumnWidth * scale);

                return;
            }

            var extraWidth = availableWidth - minimumTotalWidth;
            var fileNameWidth = FileNameColumnMinWidth + extraWidth * 0.4;
            var filePathWidth = FilePathColumnMinWidth + extraWidth * 0.6;
            var assignedWidth = fileNameWidth +
                                filePathWidth +
                                TransactionCountColumnMinWidth +
                                LoadedAtColumnMinWidth +
                                RemoveColumnWidth;

            var remainder = availableWidth - assignedWidth;
            if (Math.Abs(remainder) > ColumnWidthAdjustmentTolerance)
            {
                filePathWidth += remainder;
            }

            SetLoadedFileColumnWidths(
                fileNameWidth,
                filePathWidth,
                TransactionCountColumnMinWidth,
                LoadedAtColumnMinWidth,
                RemoveColumnWidth
            );

            if (scrollViewer != null)
            {
                _loadedFileVerticalScrollVisible = scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible;
            }
        }
        finally
        {
            _isAdjustingLoadedFileColumns = false;
        }
    }

    private void SetLoadedFileColumnWidths(double fileName, double filePath, double transactionCount, double loadedAt, double remove)
    {
        var sanitized = (
            FileName: Math.Max(0, fileName),
            FilePath: Math.Max(0, filePath),
            TransactionCount: Math.Max(0, transactionCount),
            LoadedAt: Math.Max(0, loadedAt),
            Remove: Math.Max(0, remove));

        if (_previousLoadedFileColumnWidths is { } previous &&
            Math.Abs(previous.FileName - sanitized.FileName) <= ColumnWidthAdjustmentTolerance &&
            Math.Abs(previous.FilePath - sanitized.FilePath) <= ColumnWidthAdjustmentTolerance &&
            Math.Abs(previous.TransactionCount - sanitized.TransactionCount) <= ColumnWidthAdjustmentTolerance &&
            Math.Abs(previous.LoadedAt - sanitized.LoadedAt) <= ColumnWidthAdjustmentTolerance &&
            Math.Abs(previous.Remove - sanitized.Remove) <= ColumnWidthAdjustmentTolerance)
        {
            return;
        }

        FileNameColumn.Width = sanitized.FileName;
        FilePathColumn.Width = sanitized.FilePath;
        TransactionCountColumn.Width = sanitized.TransactionCount;
        LoadedAtColumn.Width = sanitized.LoadedAt;
        RemoveColumn.Width = sanitized.Remove;

        _previousLoadedFileColumnWidths = sanitized;
    }

    private void LoadedFileListScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
        {
            return;
        }

        var isVisible = viewer.ComputedVerticalScrollBarVisibility == Visibility.Visible;

        if (_loadedFileVerticalScrollVisible == isVisible)
        {
            return;
        }

        _loadedFileVerticalScrollVisible = isVisible;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(AdjustLoadedFileColumns));
    }
    private void GridViewColumnHeader_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader header)
        {
            return;
        }

        if (header.Template.FindName("PART_HeaderGripper", header) is Thumb gripper)
        {
            gripper.IsHitTestVisible = false;
            gripper.Width = 0;
        }
    }

    // 불러온 데이터 제거
    private void RemoveFileButton_Click(object sender, RoutedEventArgs e) =>
    RemoveLoadedFile_Click(sender, e);
    private async void RemoveLoadedFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string filePath)
        {
            return;
        }

        var fileName = Path.GetFileName(filePath);
        var result = MessageBox.Show(
            $"\"{fileName}\" 파일을 목록에서 제거하시겠습니까?\n\n제거하면 이 파일의 모든 거래 내역이 삭제됩니다.",
            "파일 제거",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (_viewModel.RemoveFile(filePath))
        {
            RefreshDataViews();
            await SaveCurrentStateAsync();
        }
        else
        {
            MessageBox.Show("파일을 제거하지 못했습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsSupportedFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".xlsx" or ".xlsm" or ".xls" or ".pdf";
    }
    private async void SaveStateButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveCurrentStateAsync(true, true);
    }

    private async void LoadStateButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadStateFromDiskAsync(true, true);
    }

    private async Task SaveCurrentStateAsync(bool showMessage = false, bool promptForPath = false)
    {
        if (_isRestoringState)
        {
            return;
        }

        try
        {
            var filePath = GetSaveFilePath(promptForPath);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                if (promptForPath)
                {
                    _viewModel.LogStatus("상태 저장이 취소되었습니다.");
                }
                return;
            }

            var state = _viewModel.CreateAppState(_memoService.CreateSnapshot());
            await _appStateService.SaveAsync(state, filePath);
            _lastStateFilePath = filePath;
            _viewModel.LogStatus($"상태 저장 완료: {Path.GetFileName(filePath)}");

            if (showMessage)
            {
                MessageBox.Show("현재 상태를 저장했습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _viewModel.LogStatus($"상태 저장 실패: {ex.Message}");
            MessageBox.Show($"상태를 저장하지 못했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadStateFromDiskAsync(bool showMessage, bool promptForPath = false)
    {
        var filePath = GetLoadFilePath(promptForPath);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            if (promptForPath)
            {
                _viewModel.LogStatus("상태 불러오기가 취소되었습니다.");
            }
            return;
        }

        AppState? state;
        try
        {
            state = await _appStateService.LoadAsync(filePath);
        }
        catch (Exception ex)
        {
            _viewModel.LogStatus($"상태 불러오기 실패: {ex.Message}");
            MessageBox.Show($"상태를 불러오지 못했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (state is null)
        {
            _viewModel.LogStatus("선택한 상태 파일을 불러올 수 없습니다.");
            if (showMessage)
            {
                MessageBox.Show("선택한 상태 파일을 불러올 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        try
        {
            _isRestoringState = true;
            _memoService.LoadFromSnapshot(state.Memos);
            await _viewModel.RestoreStateAsync(state);
            RefreshDataViews();
            _lastStateFilePath = filePath;
            _viewModel.LogStatus($"상태 불러오기 완료: {Path.GetFileName(filePath)}");

            if (showMessage)
            {
                MessageBox.Show("저장된 상태를 불러왔습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            _isRestoringState = false;
        }
    }
    private string? GetSaveFilePath(bool promptForPath)
    {
        if (promptForPath)
        {
            var dialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = ".sab",
                Filter = "Simple Account Book (*.sab)|*.sab",
                InitialDirectory = _appStateService.DefaultSaveDirectory,
                FileName = string.IsNullOrWhiteSpace(_lastStateFilePath)
                    ? "AccountBook.sab"
                    : Path.GetFileName(_lastStateFilePath)
            };

            if (dialog.ShowDialog() == true)
            {
                _lastStateFilePath = dialog.FileName;
            }
            else
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(_lastStateFilePath))
        {
            _lastStateFilePath = _appStateService.DefaultStateFilePath;
        }

        return _lastStateFilePath;
    }

    private string? GetLoadFilePath(bool promptForPath)
    {
        if (promptForPath)
        {
            var dialog = new OpenFileDialog
            {
                DefaultExt = ".sab",
                Filter = "Simple Account Book (*.sab)|*.sab",
                InitialDirectory = _appStateService.DefaultSaveDirectory,
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                _lastStateFilePath = dialog.FileName;
            }
            else
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(_lastStateFilePath))
        {
            _lastStateFilePath = _appStateService.DefaultStateFilePath;
        }

        return _lastStateFilePath;
    }
    private void ToggleRightPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
        {
            return;
        }

        if (string.Equals(tag, "Files", StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.IsDetailSearchMode = false;
            return;
        }

        if (string.Equals(tag, "Details", StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.IsDetailSearchMode = true;
            if (!string.IsNullOrWhiteSpace(_viewModel.DetailSearchQuery))
            {
                PerformDetailSearch();
            }
        }
    }
    private void DetailSearchQuery_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_viewModel.IsDetailSearchMode)
        {
            return;
        }

        PerformDetailSearch();
    }

    private void DetailSearchButton_Click(object sender, RoutedEventArgs e)
    {
        PerformDetailSearch();
    }

    private void DetailSearchQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        PerformDetailSearch();
        e.Handled = true;
    }

    private void DetailSearchResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListView listView ||
            listView.SelectedItem is not DetailSearchResult result)
        {
            return;
        }

        _viewModel.SelectedDate = result.TransactionTime.Date;
        UpdateSelectedDateTransactionsWithMemo();
    }

    private void PerformDetailSearch()
    {
        var query = (_viewModel.DetailSearchQuery ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(query))
        {
            _viewModel.SetDetailSearchResults(Array.Empty<DetailSearchResult>());
            return;
        }

        var keywords = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (keywords.Length == 0)
        {
            keywords = new[] { query };
        }

        var results = new List<DetailSearchResult>();

        foreach (var transaction in _viewModel.Transactions)
        {
            var fileInfo = _viewModel.FindLoadedFileInfo(transaction);
            var memo = _memoService.GetMemo(transaction);
            if (memo is not null && memo.Details.Count > 0)
            {
                foreach (var detail in memo.Details)
                {
                    if (!MatchesDetail(detail.ItemName, detail.Note, detail.Amount, transaction, fileInfo, keywords))
                    {
                        continue;
                    }

                    results.Add(new DetailSearchResult
                    {
                        Transaction = transaction,
                        ItemName = detail.ItemName,
                        DetailAmount = detail.Amount,
                        Note = detail.Note,
                        HasMemoDetail = true,
                        SourceFileName = fileInfo?.FileName,
                        SourceFilePath = fileInfo?.FilePath
                    });
                }
                continue;
            }

            var fallbackItemName = GetFallbackItemName(transaction);
            var fallbackNote = string.Empty;
            var fallbackAmount = transaction.Amount;

            if (!MatchesDetail(fallbackItemName, fallbackNote, fallbackAmount, transaction, fileInfo, keywords))
            {
                continue;
            }
            results.Add(new DetailSearchResult
            {
                Transaction = transaction,
                ItemName = fallbackItemName,
                DetailAmount = fallbackAmount,
                Note = fallbackNote,
                HasMemoDetail = false,
                SourceFileName = fileInfo?.FileName,
                SourceFilePath = fileInfo?.FilePath
            });
        }

        var orderedResults = results
            .OrderByDescending(r => r.TransactionTime)
            .ThenByDescending(r => r.DetailAmount)
            .ToList();

        _viewModel.SetDetailSearchResults(orderedResults);
    }

    private static bool MatchesDetail(string? itemName, string? note, decimal detailAmount, TransactionRecord transaction, LoadedFileInfo? fileInfo, IReadOnlyList<string> keywords)
    {
        if (keywords.Count == 0)
        {
            return false;
        }

        var sources = new List<string?>
        {
            itemName,
            note,
            transaction.Description,
            transaction.Category,
            transaction.TransactionType,
            transaction.TransactionTime.ToString("yyyy-MM-dd HH:mm"),
            transaction.Amount.ToString("0"),
             detailAmount.ToString("0"),
            fileInfo?.FileName,
            fileInfo?.FilePath
        };

        return keywords.All(keyword =>
            sources.Any(source =>
                !string.IsNullOrWhiteSpace(source) &&
                source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0));
    }
    private static string GetFallbackItemName(TransactionRecord transaction)
    {
        if (!string.IsNullOrWhiteSpace(transaction.Description))
        {
            return transaction.Description;
        }

        if (!string.IsNullOrWhiteSpace(transaction.Category))
        {
            return transaction.Category;
        }

        if (!string.IsNullOrWhiteSpace(transaction.TransactionType))
        {
            return transaction.TransactionType;
        }

        return string.Empty;
    }
    private class MemoDetailClipboardData
    {
        public string ItemName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Note { get; set; } = string.Empty;
    }
    private sealed class ColumnSortState
    {
        private readonly ListSortDirection?[] _cycle;
        private int _index = -1;

        public ColumnSortState(string propertyName, params ListSortDirection?[] cycle)
        {
            if (cycle == null || cycle.Length == 0)
            {
                throw new ArgumentException("정렬 순환 목록은 비어 있을 수 없습니다.", nameof(cycle));
            }

            PropertyName = propertyName;
            _cycle = cycle;
        }

        public string PropertyName { get; }

        public void Reset() => _index = -1;

        public ListSortDirection? MoveNext()
        {
            _index = (_index + 1) % _cycle.Length;
            return _cycle[_index];
        }
    }
}