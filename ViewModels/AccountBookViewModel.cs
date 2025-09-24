using SimpleAccountBook.Models;
using SimpleAccountBook.Services;
using SimpleAccountBook;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows;

namespace SimpleAccountBook.ViewModels;

public class AccountBookViewModel : INotifyPropertyChanged
{
    private readonly IExcelImportService _importService;
    private readonly Dictionary<string, List<TransactionRecord>> _fileTransactions = new Dictionary<string, List<TransactionRecord>>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]?> _fileContents = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _filePasswords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private Dictionary<DateOnly, (decimal income, decimal expense)> _dailyTotals = new Dictionary<DateOnly, (decimal income, decimal expense)>();
    private Dictionary<DateOnly, DailySummary> _dailySummaries = new Dictionary<DateOnly, DailySummary>();
    private ObservableCollection<TransactionRecord> _transactions = new ObservableCollection<TransactionRecord>();
    private readonly ObservableCollection<TransactionRecord> _selectedDateTransactions = new ObservableCollection<TransactionRecord>();
    private ObservableCollection<TransactionWithMemo> _selectedDateTransactionsWithMemo = new ObservableCollection<TransactionWithMemo>();
    private readonly HashSet<TransactionRecord> _trackedTransactions = new HashSet<TransactionRecord>();
    private readonly List<TransactionRecord> _manualTransactions = new List<TransactionRecord>();
    private DateTime _currentMonth;
    private DateTime? _selectedDate;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private decimal _monthlyIncome;
    private decimal _monthlyExpense;
    private decimal _assetBalance;
    private int _dailySummaryVersion;
    private readonly ICollectionView _loadedFilesView;
    private string _loadedFileSearchText = string.Empty;
    private readonly ObservableCollection<DetailSearchResult> _detailSearchResults = new ObservableCollection<DetailSearchResult>();
    private bool _isDetailSearchMode;
    private string _detailSearchQuery = string.Empty;
    private Func<string, bool, Task<string?>> _passwordProvider = DefaultPasswordProvider;
    private string _startupYearMonthText = string.Empty;
    private Dictionary<string, TransactionTotals> _adjustedTotals = new();

    public BudgetState Budget { get; private set; } = new();
    public event EventHandler? BudgetChanged;

    public void NotifyBudgetChanged()
    {
        RecalculateSummaries();
        BudgetChanged?.Invoke(this, EventArgs.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AccountBookViewModel(IExcelImportService importService)
    {
        _importService = importService;
        var today = DateTime.Today;
        _currentMonth = new DateTime(today.Year, today.Month, 1);
        StartupYearMonthText = today.ToString("yyyy년 MM월");
        _loadedFilesView = CollectionViewSource.GetDefaultView(LoadedFiles);
        _loadedFilesView.Filter = FilterLoadedFiles;
        LoadedFiles.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(LoadedFileCount));
            OnPropertyChanged(nameof(HasLoadedFiles));
        };
        _detailSearchResults.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(DetailSearchResults));
            OnPropertyChanged(nameof(DetailSearchResultCount));
            OnPropertyChanged(nameof(HasDetailSearchResults));
        };
        AttachTransactionHandlers(_transactions);
    }
    public ObservableCollection<LoadedFileInfo> LoadedFiles { get; } = new ObservableCollection<LoadedFileInfo>();
    public ICollectionView LoadedFilesView => _loadedFilesView;
    public int LoadedFileCount => LoadedFiles.Count;
    public bool HasLoadedFiles => LoadedFiles.Count > 0;
    public bool IsDetailSearchMode
    {
        get => _isDetailSearchMode;
        set
        {
            if (_isDetailSearchMode != value)
            {
                _isDetailSearchMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLoadedFilesMode));
            }
        }
    }
    public bool IsLoadedFilesMode => !IsDetailSearchMode;
    public string LoadedFileSearchText
    {
        get => _loadedFileSearchText;
        set
        {
            if (_loadedFileSearchText != value)
            {
                _loadedFileSearchText = value;
                OnPropertyChanged();
                _loadedFilesView.Refresh();
            }
        }
    }
    public string DetailSearchQuery
    {
        get => _detailSearchQuery;
        set
        {
            if (!string.Equals(_detailSearchQuery, value, StringComparison.Ordinal))
            {
                _detailSearchQuery = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }
    public ObservableCollection<string> StatusLog { get; } = new ObservableCollection<string>();
    public Func<string, bool, Task<string?>> PasswordProvider
    {
        get => _passwordProvider;
        set => _passwordProvider = value ?? DefaultPasswordProvider;
    }
    public ObservableCollection<TransactionRecord> Transactions
    {
        get => _transactions;
        private set
        {
            if (!ReferenceEquals(_transactions, value))
            {
                DetachTransactionHandlers(_transactions);
                _transactions = value;
                AttachTransactionHandlers(_transactions);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasTransactions));
            }
        }
    }

    public ObservableCollection<TransactionRecord> SelectedDateTransactions => _selectedDateTransactions;
    public ObservableCollection<DetailSearchResult> DetailSearchResults => _detailSearchResults;

    public int DetailSearchResultCount => _detailSearchResults.Count;

    public bool HasDetailSearchResults => _detailSearchResults.Count > 0;
    // 선택된 날짜의 거래 목록 (메모 기능 포함)
    public ObservableCollection<TransactionWithMemo> SelectedDateTransactionsWithMemo
    {
        get => _selectedDateTransactionsWithMemo;
        set
        {
            _selectedDateTransactionsWithMemo = value;
            OnPropertyChanged();
        }
    }

    // 메모가 포함된 거래 목록 설정
    public void SetSelectedDateTransactionsWithMemo(ObservableCollection<TransactionWithMemo> transactions)
    {
        SelectedDateTransactionsWithMemo = transactions;
    }
    public void SetDetailSearchResults(IEnumerable<DetailSearchResult> results)
    {
        _detailSearchResults.Clear();

        if (results is null)
        {
            return;
        }

        foreach (var result in results)
        {
            _detailSearchResults.Add(result);
        }
    }

    public LoadedFileInfo? FindLoadedFileInfo(TransactionRecord record)
    {
        if (record is null)
        {
            return null;
        }

        foreach (var entry in _fileTransactions)
        {
            if (!entry.Value.Contains(record))
            {
                continue;
            }

            var normalizedPath = Path.GetFullPath(entry.Key);
            return LoadedFiles.FirstOrDefault(f =>
                string.Equals(f.FilePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }
    public string StartupYearMonthText
    {
        get => _startupYearMonthText;
        private set
        {
            if (!string.Equals(_startupYearMonthText, value, StringComparison.Ordinal))
            {
                _startupYearMonthText = value;
                OnPropertyChanged();
            }
        }
    }
    public DateTime CurrentMonth
    {
        get => _currentMonth;
        set
        {
            var normalized = new DateTime(value.Year, value.Month, 1);
            if (_currentMonth != normalized)
            {
                _currentMonth = normalized;
                OnPropertyChanged();
                UpdateMonthlyTotals();
            }
        }
    }

    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (_selectedDate != value)
            {
                _selectedDate = value;
                OnPropertyChanged();
                UpdateSelectedDateTransactions();
                if (value.HasValue)
                {
                    CurrentMonth = value.Value;
                }
            }
        }
    }

    public decimal MonthlyIncome
    {
        get => _monthlyIncome;
        private set
        {
            if (_monthlyIncome != value)
            {
                _monthlyIncome = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MonthlyNet));
            }
        }
    }

    public decimal MonthlyExpense
    {
        get => _monthlyExpense;
        private set
        {
            if (_monthlyExpense != value)
            {
                _monthlyExpense = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MonthlyNet));
            }
        }
    }

    public decimal MonthlyNet => MonthlyIncome - MonthlyExpense;

    public decimal AssetBalance
    {
        get => _assetBalance;
        private set
        {
            if (_assetBalance != value)
            {
                _assetBalance = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => UpdateStatus(message: value);
    }

    public void LogStatus(string message) => StatusMessage = message;

    private const int MaxStatusLogEntries = 200;

    private void UpdateStatus(string message)
    {
        if (_statusMessage != message)
        {
            _statusMessage = message;
            OnPropertyChanged();
        }

        AppendStatusLogEntry(message);
    }

    private void AppendStatusLogEntry(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        StatusLog.Add($"[{timestamp}] {message}");

        if (StatusLog.Count > MaxStatusLogEntries)
        {
            StatusLog.RemoveAt(0);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasTransactions => Transactions.Count > 0;
    private bool FilterLoadedFiles(object item)
    {
        if (item is not LoadedFileInfo file)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(LoadedFileSearchText))
        {
            return true;
        }

        var keyword = LoadedFileSearchText.Trim();
        return file.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || file.FilePath.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
    public IReadOnlyDictionary<DateOnly, DailySummary> DailySummaries => _dailySummaries;

    public int DailySummaryVersion
    {
        get => _dailySummaryVersion;
        private set
        {
            if (_dailySummaryVersion != value)
            {
                _dailySummaryVersion = value;
                OnPropertyChanged();
            }
        }
    }

    public async Task LoadFromFileAsync(string filePath, string? passwordOverride = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(filePath);
        var fileName = Path.GetFileName(normalizedPath);
        string? passwordToUse = passwordOverride;

        if (passwordToUse is null && _filePasswords.TryGetValue(normalizedPath, out var storedPassword))
        {
            passwordToUse = storedPassword;
        }

        IsBusy = true;

        try
        {
            StatusMessage = "파일을 불러오는 중입니다...";
            var records = (await LoadWithPasswordRetryAsync(
                    password => _importService.LoadAsync(normalizedPath, password),
                    normalizedPath,
                    fileName,
                    passwordToUse,
                    useBusyIndicator: true))
                .OrderBy(r => r.TransactionTime)
                .ToList();
            var content = TryReadFileContent(normalizedPath);
            if (content is not null)
            {
                _fileContents[normalizedPath] = content;
            }
            else
            {
                _fileContents.Remove(normalizedPath);
            }
            bool isUpdate = _fileTransactions.ContainsKey(normalizedPath);
            if (_fileTransactions.TryGetValue(normalizedPath, out var previousRecords))
            {
                PreserveBudgetAssignments(previousRecords, records);
            }
            _fileTransactions[normalizedPath] = records;
            UpdateLoadedFile(normalizedPath, records.Count);

            RebuildTransactionsFromFiles();

            StatusMessage = isUpdate
              ? $"{fileName} 새로고침 완료 - {records.Count}건 (총 {Transactions.Count}건)"
                : $"{fileName} 불러오기 완료 - {records.Count}건 (총 {Transactions.Count}건)";
        }
        catch (DocumentPasswordException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"파일을 불러오지 못했습니다: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
    private static void PreserveBudgetAssignments(IEnumerable<TransactionRecord> previous, IEnumerable<TransactionRecord> imported)
    {
        // Match only unchanged rows in the same source file, preserving duplicate rows individually.
        var previousRows = previous.GroupBy(r => (r.TransactionTime, r.TransactionType, r.Amount, r.Category, r.Description))
            .ToDictionary(g => g.Key, g => new Queue<TransactionRecord>(g));
        foreach (var record in imported)
        {
            var key = (record.TransactionTime, record.TransactionType, record.Amount, record.Category, record.Description);
            if (!previousRows.TryGetValue(key, out var matches) || matches.Count == 0)
            {
                continue;
            }

            var previousRecord = matches.Dequeue();
            record.Id = previousRecord.Id;
            record.ExpensePlanId = previousRecord.ExpensePlanId;
            record.SpendingCategory = previousRecord.SpendingCategory;
            record.Tags = previousRecord.Tags;
            record.SourceAccountId = previousRecord.SourceAccountId;
            record.CounterpartyAccountNumber = previousRecord.CounterpartyAccountNumber;
            record.TransferAccountId = previousRecord.TransferAccountId;
            record.RefundOfTransactionId = previousRecord.RefundOfTransactionId;
            record.IsExcludedFromTotal = previousRecord.IsExcludedFromTotal;
        }
    }

    public bool RemoveFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(filePath);
        if (!_fileTransactions.Remove(normalizedPath))
        {
            return false;
        }

        _fileContents.Remove(normalizedPath);
        _filePasswords.Remove(normalizedPath);

        var existing = LoadedFiles.FirstOrDefault(f =>
            string.Equals(f.FilePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            LoadedFiles.Remove(existing);
        }

        RebuildTransactionsFromFiles();

        StatusMessage = LoadedFiles.Count > 0
            ? $"{Path.GetFileName(normalizedPath)} 제거 완료 (총 {Transactions.Count}건)"
            : "모든 파일이 제거되었습니다. 새로운 파일을 불러오세요.";

        return true;
    }

    public DailySummary GetDailySummary(DateTime date)
    {
        var key = DateOnly.FromDateTime(date);
        if (_dailySummaries.TryGetValue(key, out var summary))
        {
            return summary;
        }

        return DailySummary.CreateEmpty(date);
    }

    public AppState CreateAppState(Dictionary<string, MemoSnapshot>? memoSnapshots)
    {
        var state = new AppState
        {
            CurrentMonth = CurrentMonth,
            SelectedDate = SelectedDate,
            Budget = Budget.CreateSnapshot()
        };

        foreach (var (filePath, records) in _fileTransactions)
        {
            var normalizedPath = Path.GetFullPath(filePath);
            var loadedInfo = LoadedFiles.FirstOrDefault(f =>
                string.Equals(f.FilePath, normalizedPath, StringComparison.OrdinalIgnoreCase));

            var clonedRecords = records.Select(r => r.CreateSnapshot()).ToList();

            byte[]? excelContent = null;
            if (_fileContents.TryGetValue(normalizedPath, out var cachedContent) && cachedContent is not null)
            {
                excelContent = cachedContent;
            }
            else
            {
                excelContent = TryReadFileContent(normalizedPath);
                if (excelContent is not null)
                {
                    _fileContents[normalizedPath] = excelContent;
                }
            }

            state.Files.Add(new AppStateFile
            {
                FilePath = normalizedPath,
                LoadedAt = loadedInfo?.LoadedAt ?? DateTime.Now,
                Transactions = clonedRecords,
                ExcelContent = excelContent
            });
        }

        state.ManualTransactions = _manualTransactions
        .Select(r => r.CreateSnapshot())
        .ToList();

        if (memoSnapshots is null)
        {
            state.Memos = new Dictionary<string, MemoSnapshot>(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            state.Memos = memoSnapshots.ToDictionary(
                kvp => kvp.Key,
                kvp => new MemoSnapshot
                {
                    LastModified = kvp.Value.LastModified,
                    Details = kvp.Value.Details?
                        .Select(detail => new MemoDetailSnapshot
                        {
                            ItemName = detail.ItemName,
                            Amount = detail.Amount,
                            Note = detail.Note
                        })
                        .ToList() ?? new List<MemoDetailSnapshot>()
                },
                StringComparer.OrdinalIgnoreCase);
        }
        return state;
    }

    public async Task RestoreStateAsync(AppState? state)
    {
        if (state is null)
        {
            return;
        }

        _fileTransactions.Clear();
        LoadedFiles.Clear();
        _fileContents.Clear();
        _filePasswords.Clear();
        _manualTransactions.Clear();
        Budget = (state.Budget ?? new BudgetState()).CreateSnapshot();

        if (state.Files is not null)
        {
            foreach (var file in state.Files)
            {
                if (string.IsNullOrWhiteSpace(file.FilePath))
                {
                    continue;
                }

                var normalizedPath = Path.GetFullPath(file.FilePath);
                if (file.ExcelContent is { Length: > 0 })
                {
                    _fileContents[normalizedPath] = file.ExcelContent;
                }

                IEnumerable<TransactionRecord> sourceRecords = Array.Empty<TransactionRecord>();

                if (file.Transactions is { Count: > 0 })
                {
                    sourceRecords = file.Transactions;
                }
                else if (file.ExcelContent is { Length: > 0 })
                {
                    try
                    {
                        var imported = await LoadWithPasswordRetryAsync(
                             password => _importService.LoadAsync(file.ExcelContent, file.FilePath, password),
                             normalizedPath,
                             Path.GetFileName(normalizedPath),
                             TryGetStoredPassword(normalizedPath),
                             useBusyIndicator: false);
                        sourceRecords = imported;
                    }
                    catch (DocumentPasswordException ex)
                    {
                        Debug.WriteLine($"[AccountBookViewModel] 임베디드 파일 데이터 복원 비밀번호 필요: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AccountBookViewModel] 임베디드 파일 데이터 복원 실패: {ex}");
                    }
                }
                else if (File.Exists(normalizedPath))
                {
                    try
                    {
                        var imported = await LoadWithPasswordRetryAsync(
                             password => _importService.LoadAsync(normalizedPath, password),
                             normalizedPath,
                             Path.GetFileName(normalizedPath),
                             TryGetStoredPassword(normalizedPath),
                             useBusyIndicator: false);
                        sourceRecords = imported;
                        var diskContent = TryReadFileContent(normalizedPath);
                        if (diskContent is not null)
                        {
                            _fileContents[normalizedPath] = diskContent;
                        }
                    }
                    catch (DocumentPasswordException ex)
                    {
                        Debug.WriteLine($"[AccountBookViewModel] 파일 재로딩 비밀번호 필요: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AccountBookViewModel] 파일 재로딩 실패: {ex}");
                    }
                }

                var orderedRecords = sourceRecords
                    .Select(r => r.CreateSnapshot())
                    .OrderBy(r => r.TransactionTime)
                    .ToList();

                _fileTransactions[normalizedPath] = orderedRecords;
                var loadedAt = file.LoadedAt == default ? DateTime.Now : file.LoadedAt;
                LoadedFiles.Add(new LoadedFileInfo(normalizedPath, orderedRecords.Count, loadedAt));
            }
        }

        if (state.ManualTransactions is { Count: > 0 })
        {
            foreach (var manual in state.ManualTransactions)
            {
                var restored = manual.CreateSnapshot();

                _manualTransactions.Add(restored);
            }
        }

        RebuildTransactionsFromFiles();

        if (state.CurrentMonth != default)
        {
            CurrentMonth = state.CurrentMonth;
        }

        SelectedDate = state.SelectedDate;

        if (!SelectedDate.HasValue)
        {
            var today = DateTime.Today;
            var normalizedToday = new DateTime(today.Year, today.Month, 1);

            if (CurrentMonth < normalizedToday)
            {
                CurrentMonth = normalizedToday;
            }
        }

        StatusMessage = state.Files?.Count > 0
            ? "저장된 상태를 불러왔습니다."
            : "저장된 상태가 비어 있습니다.";
    }

    private string? TryGetStoredPassword(string normalizedPath)
    {
        return _filePasswords.TryGetValue(normalizedPath, out var storedPassword)
            ? storedPassword
            : null;
    }

    private async Task<IList<TransactionRecord>> LoadWithPasswordRetryAsync(
        Func<string?, Task<IList<TransactionRecord>>> loader,
        string normalizedPath,
        string fileName,
        string? initialPassword,
        bool useBusyIndicator)
    {
        string? passwordToUse = initialPassword;

        while (true)
        {
            try
            {
                if (useBusyIndicator)
                {
                    StatusMessage = "파일을 불러오는 중입니다...";
                }

                var records = await loader(passwordToUse);

                if (!string.IsNullOrEmpty(passwordToUse))
                {
                    _filePasswords[normalizedPath] = passwordToUse;
                }
                else
                {
                    _filePasswords.Remove(normalizedPath);
                }

                return records;
            }
            catch (DocumentPasswordException ex)
            {

                if (PasswordProvider is null)
                {
                    throw;
                }

                StatusMessage = ex.IsInvalidPassword
                    ? $"{fileName} 비밀번호가 올바르지 않습니다."
                    : $"{fileName} 파일의 비밀번호가 필요합니다.";

                if (useBusyIndicator)
                {
                    IsBusy = false;
                }

                var providedPassword = await _passwordProvider(fileName, ex.IsInvalidPassword);
                if (string.IsNullOrEmpty(providedPassword))
                {
                    StatusMessage = $"{fileName} 비밀번호 입력이 취소되었습니다.";

                    if (useBusyIndicator)
                    {
                        IsBusy = true;
                    }

                    throw;
                }

                passwordToUse = providedPassword;

                if (useBusyIndicator)
                {
                    IsBusy = true;
                }
            }
        }
    }

    private static Task<string?> DefaultPasswordProvider(string fileName, bool isRetry)
    {
        var app = Application.Current;
        if (app is null)
        {
            return Task.FromResult<string?>(null);
        }

        if (app.Dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowPasswordPrompt(app, fileName, isRetry));
        }

        return app.Dispatcher.InvokeAsync(() => ShowPasswordPrompt(app, fileName, isRetry)).Task;
    }

    private static string? ShowPasswordPrompt(Application app, string fileName, bool isRetry)
    {
        Window? owner = null;
        try
        {
            owner = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
        }
        catch
        {
            owner = app.MainWindow;
        }

        var prompt = new PasswordPromptWindow(fileName, isRetry);
        if (owner is not null && !ReferenceEquals(owner, prompt))
        {
            prompt.Owner = owner;
        }

        return prompt.ShowDialog() == true ? prompt.Password : null;
    }
    private void RebuildDailyTotals()
    {
        _adjustedTotals = BudgetService.GetAdjustedAmounts(Transactions, Budget);
        var newDailyTotals = new Dictionary<DateOnly, (decimal income, decimal expense)>();
        var newDailySummaries = new Dictionary<DateOnly, DailySummary>();
        foreach (var record in Transactions)
        {
            var adjusted = _adjustedTotals[record.Id];
            if (adjusted.Income == 0 && adjusted.Expense == 0)
            {
                continue;
            }

            var date = DateOnly.FromDateTime(record.TransactionTime);
            var totals = newDailyTotals.TryGetValue(date, out var value)
                ? value
                : (income: 0m, expense: 0m);
            totals.income += adjusted.Income;
            totals.expense += adjusted.Expense;

            newDailyTotals[date] = totals;
        }

        foreach (var (date, totals) in newDailyTotals)
        {
            newDailySummaries[date] = DailySummary.FromTotals(date, totals.income, totals.expense);
        }

        _dailyTotals = newDailyTotals;
        _dailySummaries = newDailySummaries;
        OnPropertyChanged(nameof(DailySummaries));
        DailySummaryVersion++;

        Debug.WriteLine($"[RebuildDailyTotals] days={newDailySummaries.Count}");
        foreach (var kv in newDailyTotals.Take(5))
        {
            Debug.WriteLine($"  {kv.Key}: income={kv.Value.income}, expense={kv.Value.expense}");
        }
    }

    private void UpdateMonthlyTotals()
    {
        var start = new DateTime(CurrentMonth.Year, CurrentMonth.Month, 1);
        var end = start.AddMonths(1);

        MonthlyIncome = _dailyTotals.Where(t => t.Key >= DateOnly.FromDateTime(start) && t.Key < DateOnly.FromDateTime(end))
            .Sum(t => t.Value.income);
        MonthlyExpense = _dailyTotals.Where(t => t.Key >= DateOnly.FromDateTime(start) && t.Key < DateOnly.FromDateTime(end))
            .Sum(t => t.Value.expense);
    }

    private void UpdateAssetBalance()
    {
        AssetBalance = _adjustedTotals.Values.Sum(t => t.Income - t.Expense);
    }

    private void UpdateSelectedDateTransactions()
    {
        _selectedDateTransactions.Clear();
        if (SelectedDate is not DateTime date)
        {
            return;
        }

        var key = DateOnly.FromDateTime(date);
        foreach (var record in Transactions
                     .Where(t => DateOnly.FromDateTime(t.TransactionTime) == key)
                     .OrderBy(t => t.TransactionTime))
        {
            _selectedDateTransactions.Add(record);
        }

        OnPropertyChanged(nameof(SelectedDateTransactions));
    }
    public TransactionRecord AddManualTransaction(DateTime date)
    {
        var normalizedDate = date.Date;
        var transactionTime = normalizedDate.Add(DateTime.Now.TimeOfDay);

        if (transactionTime.Date != normalizedDate)
        {
            transactionTime = normalizedDate;
        }

        var newRecord = new TransactionRecord
        {
            TransactionTime = transactionTime,
            TransactionType = string.Empty,
            Amount = 0m,
            Category = string.Empty,
            Description = string.Empty
        };

        _manualTransactions.Add(newRecord);
        InsertTransactionSorted(newRecord);

        if (SelectedDate is DateTime current && current.Date == normalizedDate)
        {
            UpdateSelectedDateTransactions();
        }
        else
        {
            SelectedDate = normalizedDate;
        }

        LogStatus($"{transactionTime:yyyy-MM-dd HH:mm} 수동 거래를 추가했습니다.");

        return newRecord;
    }

    public bool RemoveTransaction(TransactionRecord? record)
    {
        if (record is null)
        {
            return false;
        }

        var removed = Transactions.Remove(record);

        if (!removed)
        {
            return false;
        }

        RemoveTransactionFromSourceCollections(record);
        UpdateSelectedDateTransactions();
        OnPropertyChanged(nameof(HasTransactions));

        LogStatus($"{record.TransactionTime:yyyy-MM-dd HH:mm} 거래를 삭제했습니다.");

        return true;
    }

    public void RecalculateTotals()
    {
        RecalculateSummaries();
    }

    private void InsertTransactionSorted(TransactionRecord record)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        var index = 0;

        while (index < Transactions.Count && Transactions[index].TransactionTime <= record.TransactionTime)
        {
            index++;
        }

        Transactions.Insert(index, record);
    }

    private bool ResortTransaction(TransactionRecord record)
    {
        if (record is null)
        {
            return false;
        }

        var oldIndex = Transactions.IndexOf(record);
        if (oldIndex < 0)
        {
            return false;
        }

        var recordTime = record.TransactionTime;
        var newIndex = 0;

        for (var i = 0; i < Transactions.Count; i++)
        {
            var current = Transactions[i];
            if (ReferenceEquals(current, record))
            {
                continue;
            }

            if (current.TransactionTime <= recordTime)
            {
                newIndex++;
            }
            else
            {
                break;
            }
        }

        if (newIndex >= Transactions.Count)
        {
            newIndex = Math.Max(Transactions.Count - 1, 0);
        }

        if (newIndex == oldIndex)
        {
            return false;
        }

        Transactions.Move(oldIndex, newIndex);
        return true;
    }
    private void RemoveTransactionFromSourceCollections(TransactionRecord record)
    {
        if (_manualTransactions.Remove(record))
        {
            return;
        }

        foreach (var entry in _fileTransactions.ToList())
        {
            if (!entry.Value.Remove(record))
            {
                continue;
            }

            UpdateLoadedFile(entry.Key, entry.Value.Count);
            break;
        }
    }
    private void RebuildTransactionsFromFiles()
    {
        var combined = _manualTransactions
            .Concat(_fileTransactions.Values.SelectMany(list => list))
            .OrderBy(t => t.TransactionTime)
            .ToList();

        Transactions = new ObservableCollection<TransactionRecord>(combined);

        RebuildDailyTotals();
        UpdateMonthlyTotals();
        UpdateAssetBalance();

        if (Transactions.Count == 0)
        {
            SelectedDate = null;
            UpdateSelectedDateTransactions();
            return;
        }

        if (SelectedDate is not DateTime selectedDate ||
            !Transactions.Any(t => DateOnly.FromDateTime(t.TransactionTime) == DateOnly.FromDateTime(selectedDate)))
        {
            SelectedDate = Transactions.Last().TransactionTime.Date;
        }
        else
        {
            UpdateSelectedDateTransactions();
        }
    }
    private void TrackTransaction(TransactionRecord record)
    {
        if (_trackedTransactions.Add(record))
        {
            record.PropertyChanged += TransactionRecord_PropertyChanged;
        }
    }

    private void UntrackTransaction(TransactionRecord record)
    {
        if (_trackedTransactions.Remove(record))
        {
            record.PropertyChanged -= TransactionRecord_PropertyChanged;
        }
    }

    private void AttachTransactionHandlers(ObservableCollection<TransactionRecord>? transactions)
    {
        if (transactions is null)
        {
            return;
        }

        transactions.CollectionChanged += Transactions_CollectionChanged;
        foreach (var record in transactions)
        {
            TrackTransaction(record);
        }
    }

    private void DetachTransactionHandlers(ObservableCollection<TransactionRecord>? transactions)
    {
        if (transactions is null)
        {
            return;
        }

        transactions.CollectionChanged -= Transactions_CollectionChanged;
        foreach (var record in transactions.ToList())
        {
            UntrackTransaction(record);
        }
    }

    private void Transactions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is { } oldItems)
        {
            foreach (TransactionRecord record in oldItems)
            {
                UntrackTransaction(record);
            }
        }

        if (e.NewItems is { } newItems)
        {
            foreach (TransactionRecord record in newItems)
            {
                TrackTransaction(record);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset && sender is ObservableCollection<TransactionRecord> collection)
        {
            foreach (var tracked in _trackedTransactions.ToList())
            {
                UntrackTransaction(tracked);
            }

            foreach (var record in collection)
            {
                TrackTransaction(record);
            }
        }

        RecalculateSummaries();
    }

    private void TransactionRecord_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TransactionRecord record)
        {
            return;
        }

        if (e.PropertyName == nameof(TransactionRecord.TransactionTime))
        {
            var moved = ResortTransaction(record);

            if (!moved)
            {
                RecalculateSummaries();
            }

            if (SelectedDate is DateTime selectedDate)
            {
                var selectedDateOnly = DateOnly.FromDateTime(selectedDate);
                var recordDate = DateOnly.FromDateTime(record.TransactionTime);

                if (recordDate == selectedDateOnly)
                {
                    UpdateSelectedDateTransactions();
                }
                else
                {
                    UpdateSelectedDateTransactions();

                    if (!Transactions.Any(t => DateOnly.FromDateTime(t.TransactionTime) == selectedDateOnly))
                    {
                        SelectedDate = record.TransactionTime.Date;
                    }
                }
            }
            else
            {
                SelectedDate = record.TransactionTime.Date;
            }

            return;
        }


        if (e.PropertyName == nameof(TransactionRecord.Amount) ||
            e.PropertyName == nameof(TransactionRecord.TransactionType) ||
            e.PropertyName == nameof(TransactionRecord.IsExcludedFromTotal))
        {
            RecalculateSummaries();
        }
    }

    private void RecalculateSummaries()
    {
        RebuildDailyTotals();
        UpdateMonthlyTotals();
        UpdateAssetBalance();
    }
    private void UpdateLoadedFile(string filePath, int recordCount)
    {
        var existing = LoadedFiles.FirstOrDefault(f =>
            string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            LoadedFiles.Add(new LoadedFileInfo(filePath, recordCount, DateTime.Now));
        }
        else
        {
            existing.Update(recordCount, DateTime.Now);
        }
    }
    private static byte[]? TryReadFileContent(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountBookViewModel] 파일 내용 읽기 실패 ({path}): {ex}");
            return null;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}