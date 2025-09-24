using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SimpleAccountBook.Models;
using SimpleAccountBook.Services;
using SimpleAccountBook.ViewModels;

namespace SimpleAccountBook;

public partial class BudgetWindow : Window
{
    private readonly AccountBookViewModel _viewModel;
    private DateTime _month;
    private DateTime? _statsMonth;
    private ExpensePlan? _editingPlan;
    private bool _refreshing = true;
    private List<TransactionRow> _rows = new();
    private List<TransactionRow> _statsRows = new();

    public BudgetWindow(AccountBookViewModel viewModel)
    {
        _viewModel = viewModel;
        _month = MonthStart(viewModel.CurrentMonth);
        InitializeComponent();
        Refresh();
        ResetPlanEditor();
        StatusText.Text = "적용한 변경사항은 기존 상태 파일에 자동 저장됩니다. 이체 후보는 확인하기 전까지 통계에 포함됩니다.";
    }

    private BudgetState State => _viewModel.Budget;
    private static DateTime MonthStart(DateTime value) => new(value.Year, value.Month, 1);

    private void Refresh()
    {
        _refreshing = true;
        try
        {
            var selectedIds = TransactionsGrid.SelectedItems.Cast<TransactionRow>().Select(r => r.Record.Id).ToHashSet();
            var category = CategoryFilter.SelectedItem as string;
            var planId = (LinkPlan.SelectedItem as ExpensePlan)?.Id;
            var sourceId = (SourceAccount.SelectedItem as OwnAccount)?.Id;
            var transferId = (TransferAccount.SelectedItem as OwnAccount)?.Id;
            var totals = BudgetService.GetAdjustedAmounts(_viewModel.Transactions, State);
            var candidateIds = BudgetService.GetTransferCandidateIds(_viewModel.Transactions, State);
            _rows = _viewModel.Transactions.OrderByDescending(t => t.TransactionTime).Select(t => new TransactionRow
            {
                Record = t,
                Income = totals[t.Id].Income,
                Expense = totals[t.Id].Expense,
                Category = BudgetService.GetCategory(t, State),
                PlanName = State.ExpensePlans.Where(p => p.Id == t.ExpensePlanId && p.StartMonth <= t.TransactionTime)
                    .OrderByDescending(p => p.StartMonth).FirstOrDefault()?.Name ?? string.Empty,
                AccountName = State.OwnAccounts.FirstOrDefault(a => a.Id == t.SourceAccountId)?.Name ?? "미지정",
                IsOwnTransfer = BudgetService.IsOwnTransfer(t, State),
                IsTransferCandidate = candidateIds.Contains(t.Id),
                Status = totals[t.Id].ExclusionReason.Length > 0 ? totals[t.Id].ExclusionReason
                    : candidateIds.Contains(t.Id) ? "이체 후보 · 확인 필요" : "포함"
            }).ToList();

            var summaries = BudgetService.GetPlanSummaries(_viewModel.Transactions, State, _month);
            PlansGrid.ItemsSource = summaries.Select(s => new PlanRow(s)).ToList();
            MonthText.Text = _month.ToString("yyyy년 MM월");
            var monthlyActual = _rows.Where(r => MonthStart(r.Record.TransactionTime) == _month).Sum(r => r.Expense);
            var remaining = summaries.Sum(s => s.Remaining);
            ForecastText.Text = $"실제 지출 {monthlyActual:N0}원  +  남은 예상액 {remaining:N0}원  =  예상 총지출 {monthlyActual + remaining:N0}원\n등록 예상액 {summaries.Sum(s => s.Expected):N0}원  ·  항목 초과액 {summaries.Sum(s => s.Overrun):N0}원";
            var otherIds = BudgetService.GetOtherExpenses(_viewModel.Transactions, State, _month).Select(t => t.Id).ToHashSet();
            OtherGrid.ItemsSource = _rows.Where(r => otherIds.Contains(r.Record.Id)).ToList();
            var categories = State.Categories.Concat(_rows.Select(r => r.Category)).Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();
            CategoriesGrid.ItemsSource = State.Categories.OrderBy(c => c).ToList();
            CategoryFilter.ItemsSource = new[] { "모든 분류" }.Concat(categories).ToList();
            CategoryFilter.SelectedItem = category != null && categories.Contains(category) ? category : "모든 분류";
            // Include plans outside the selected forecast month; validate each transaction's own month when linking.
            var plans = State.ExpensePlans.GroupBy(p => p.Id).Select(g => g.OrderByDescending(p => p.StartMonth).First()).OrderBy(p => p.Name).ToList();
            LinkPlan.ItemsSource = plans;
            LinkPlan.SelectedItem = plans.FirstOrDefault(p => p.Id == planId);
            PlanEditorSelection.ItemsSource = State.ExpensePlans.GroupBy(p => p.Id)
                .Select(g => g.Where(p => p.StartMonth <= _month).OrderByDescending(p => p.StartMonth).FirstOrDefault()
                    ?? g.OrderBy(p => p.StartMonth).First()).OrderBy(p => p.Name).ToList();
            SourceAccount.ItemsSource = State.OwnAccounts.ToList();
            TransferAccount.ItemsSource = State.OwnAccounts.ToList();
            SourceAccount.SelectedItem = State.OwnAccounts.FirstOrDefault(a => a.Id == sourceId);
            TransferAccount.SelectedItem = State.OwnAccounts.FirstOrDefault(a => a.Id == transferId);
            var monthly = BudgetService.GetMonthlySummaries(_viewModel.Transactions, State, DateTime.Today);
            MonthlyGrid.ItemsSource = monthly;
            StatsPeriodText.Text = $"{monthly.First().Month:yyyy-MM} ~ {monthly.Last().Month:yyyy-MM} · 이번 달 제외";
            StatsTotalText.Text = $"총수입 {monthly.Sum(m => m.Income):N0}원     총지출 {monthly.Sum(m => m.Expense):N0}원     차액 {monthly.Sum(m => m.Net):N0}원";
            var maximum = Math.Max(1m, monthly.Max(m => Math.Max(m.Income, m.Expense)));
            ChartItems.ItemsSource = monthly.Select(m => new { m.Month, m.Income, m.Expense, Maximum = maximum }).ToList();
            FilterTransactions();
            foreach (var row in TransactionsGrid.Items.Cast<TransactionRow>().Where(r => selectedIds.Contains(r.Record.Id)))
                TransactionsGrid.SelectedItems.Add(row);
            FilterStatistics();
        }
        finally { _refreshing = false; }
    }

    private void Changed(string message)
    {
        _viewModel.NotifyBudgetChanged();
        Refresh();
        StatusText.Text = message;
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "입력 확인", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private List<TransactionRecord> SelectedTransactions()
    {
        var records = TransactionsGrid.SelectedItems.Cast<TransactionRow>().Select(r => r.Record).ToList();
        if (records.Count == 0) throw new ArgumentException("먼저 거래를 선택해 주세요.");
        return records;
    }

    private static DateTime ReadMonth(string text, string label)
    {
        if (!DateTime.TryParseExact(text.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
            || month.Year < 1900 || month.Year > 9998)
            throw new ArgumentException($"{label}을 yyyy-MM 형식으로 입력해 주세요. (1900~9998년)");
        return month;
    }

    private void ChangeMonth(DateTime month)
    {
        _month = MonthStart(month);
        Refresh();
        ResetPlanEditor();
    }
    private void PreviousMonth_Click(object sender, RoutedEventArgs e) { if (_month.Year > 1900 || _month.Month > 1) ChangeMonth(_month.AddMonths(-1)); }
    private void NextMonth_Click(object sender, RoutedEventArgs e) { if (_month.Year < 9998 || _month.Month < 12) ChangeMonth(_month.AddMonths(1)); }
    private void ThisMonth_Click(object sender, RoutedEventArgs e) => ChangeMonth(DateTime.Today);
    private void NewPlan_Click(object sender, RoutedEventArgs e) => ResetPlanEditor();

    private void ResetPlanEditor()
    {
        _editingPlan = null;
        PlansGrid.SelectedItem = null;
        PlanEditorSelection.SelectedItem = null;
        PlanEditorTitle.Text = "새 예상 지출";
        PlanName.Text = PlanCategory.Text = PlanEnd.Text = PlanNote.Text = string.Empty;
        PlanKind.SelectedIndex = 0;
        PlanAmount.Text = "0";
        PlanDay.Text = PlanInterval.Text = "1";
        PlanStart.Text = _month.ToString("yyyy-MM");
    }

    private void PlansGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || PlansGrid.SelectedItem is not PlanRow row) return;
        LoadPlanEditor(row.Plan);
    }

    private void PlanEditorSelection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && PlanEditorSelection.SelectedItem is ExpensePlan plan) LoadPlanEditor(plan);
    }

    private void LoadPlanEditor(ExpensePlan plan)
    {
        _editingPlan = plan.CreateSnapshot();
        PlanEditorTitle.Text = "예상 지출 편집";
        PlanName.Text = plan.Name;
        PlanKind.SelectedIndex = plan.Kind == ExpenseKind.Fixed ? 0 : 1;
        PlanAmount.Text = plan.Amount.ToString("0.##");
        PlanCategory.Text = plan.Category;
        PlanDay.Text = plan.DayOfMonth.ToString();
        PlanInterval.Text = plan.IntervalMonths.ToString();
        PlanStart.Text = plan.StartMonth.ToString("yyyy-MM");
        PlanEnd.Text = plan.EndMonth?.ToString("yyyy-MM") ?? string.Empty;
        PlanNote.Text = plan.Note;
    }

    private void SavePlan_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (!decimal.TryParse(PlanAmount.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount))
            throw new ArgumentException("예상 금액을 숫자로 입력해 주세요.");
        if (!int.TryParse(PlanDay.Text, out var day) || !int.TryParse(PlanInterval.Text, out var interval))
            throw new ArgumentException("예정일과 반복 간격을 정수로 입력해 주세요.");
        var draft = _editingPlan?.CreateSnapshot() ?? new ExpensePlan();
        draft.Name = PlanName.Text;
        draft.Kind = PlanKind.SelectedIndex == 0 ? ExpenseKind.Fixed : ExpenseKind.Variable;
        draft.Amount = amount;
        draft.DayOfMonth = day;
        draft.IntervalMonths = interval;
        draft.StartMonth = ReadMonth(PlanStart.Text, "시작 월");
        draft.EndMonth = string.IsNullOrWhiteSpace(PlanEnd.Text) ? null : ReadMonth(PlanEnd.Text, "종료 월");
        draft.Category = PlanCategory.Text;
        draft.Note = PlanNote.Text;
        BudgetService.SavePlan(State, draft, _editingPlan == null ? draft.StartMonth : _month);
        Changed("예상 지출을 저장했습니다. 이전 월의 기록은 유지됩니다.");
        ResetPlanEditor();
    });

    private void EndPlan_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_editingPlan == null) throw new ArgumentException("종료할 예상 항목을 선택해 주세요.");
        BudgetService.EndPlan(State, _editingPlan.Id, _month);
        Changed($"{_month:yyyy-MM}부터 예상 항목을 종료했습니다.");
        ResetPlanEditor();
    });

    private void TransactionFilter_Changed(object sender, RoutedEventArgs e) { if (!_refreshing) FilterTransactions(); }
    private void FilterTransactions()
    {
        var search = TransactionSearch.Text.Trim();
        var category = CategoryFilter.SelectedItem as string;
        var records = _rows.Where(r => (OnlySelectedMonth.IsChecked != true || MonthStart(r.Record.TransactionTime) == _month)
            && (category == null || category == "모든 분류" || r.Category == category) && Matches(r, search));
        records = StatusFilter.SelectedIndex switch
        {
            1 => records.Where(r => r.Record.IsIncome),
            2 => records.Where(r => !r.Record.IsIncome),
            3 => records.Where(r => r.Expense > 0 && !BudgetService.GetActivePlans(State, r.Record.TransactionTime).Any(p => p.Id == r.Record.ExpensePlanId)),
            4 => records.Where(r => r.IsOwnTransfer),
            5 => records.Where(r => !string.IsNullOrWhiteSpace(r.Record.RefundOfTransactionId) || _viewModel.Transactions.Any(t => t.RefundOfTransactionId == r.Record.Id)),
            6 => records.Where(r => r.Income == 0 && r.Expense == 0 && r.Record.Amount > 0),
            7 => records.Where(r => r.IsTransferCandidate),
            _ => records
        };
        TransactionsGrid.ItemsSource = records.ToList();
    }

    private static bool Matches(TransactionRow row, string search) => search.Length == 0
        || row.Record.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || row.Record.Category.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || row.Category.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || row.Record.Tags.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || row.PlanName.Contains(search, StringComparison.CurrentCultureIgnoreCase);

    private void AddCategory(string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !State.Categories.Contains(name, StringComparer.OrdinalIgnoreCase)) State.Categories.Add(name);
    }
    private void SetCategory_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var records = SelectedTransactions();
        var category = BulkCategory.Text.Trim();
        foreach (var record in records) record.SpendingCategory = category;
        AddCategory(category);
        Changed($"{records.Count}건의 분류를 적용했습니다.");
    });
    private void SetTags_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var records = SelectedTransactions();
        var tags = string.Join(", ", BulkTags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct());
        foreach (var record in records) record.Tags = tags;
        Changed($"{records.Count}건의 태그를 적용했습니다.");
    });
    private void LinkPlan_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var records = SelectedTransactions();
        if (LinkPlan.SelectedItem is not ExpensePlan plan) throw new ArgumentException("연결할 예상 항목을 선택해 주세요.");
        if (records.Any(r => r.IsIncome || !BudgetService.GetActivePlans(State, r.TransactionTime).Any(p => p.Id == plan.Id)))
            throw new ArgumentException("해당 예상 항목이 발생하는 월의 지출 거래만 연결할 수 있습니다.");
        if (records.Any(r => BudgetService.IsOwnTransfer(r, State))) throw new ArgumentException("본인 계좌 간 이체는 예상 지출에 연결할 수 없습니다.");
        foreach (var record in records) record.ExpensePlanId = plan.Id;
        Changed($"{records.Count}건을 예상 항목에 연결했습니다.");
    });
    private void UnlinkPlan_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        foreach (var record in SelectedTransactions()) record.ExpensePlanId = string.Empty;
        Changed("예상 항목 연결을 해제했습니다.");
    });
    private void SetSourceAccount_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var records = SelectedTransactions();
        if (SourceAccount.SelectedItem is not OwnAccount account) throw new ArgumentException("거래가 발생한 본인 계좌를 선택해 주세요.");
        if (records.Any(r => r.TransferAccountId == account.Id)) throw new ArgumentException("상대 본인 계좌와 같은 계좌입니다. 이체 연결을 먼저 해제해 주세요.");
        foreach (var record in records) record.SourceAccountId = account.Id;
        Changed($"{records.Count}건의 거래 계좌를 지정했습니다.");
    });
    private void ConfirmTransfer_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var records = SelectedTransactions();
        if (TransferAccount.SelectedItem is not OwnAccount account) throw new ArgumentException("상대 본인 계좌를 선택해 주세요.");
        if (records.Any(r => !State.OwnAccounts.Any(a => a.Id == r.SourceAccountId) || r.SourceAccountId == account.Id))
            throw new ArgumentException("거래 계좌를 먼저 지정하세요. 거래 계좌와 상대 본인 계좌는 달라야 합니다.");
        if (records.Any(r => !string.IsNullOrWhiteSpace(r.RefundOfTransactionId) || _viewModel.Transactions.Any(t => t.RefundOfTransactionId == r.Id)))
            throw new ArgumentException("환불 연결이 있는 거래입니다. 환불 연결을 먼저 해제해 주세요.");
        foreach (var record in records) { record.TransferAccountId = account.Id; record.ExpensePlanId = string.Empty; }
        Changed("선택한 본인 계좌 간 이체를 통계에서 제외했습니다. 반대편 거래도 같은 방식으로 확인하세요.");
    });
    private void ClearSourceAccount_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        foreach (var record in SelectedTransactions())
        {
            record.SourceAccountId = record.TransferAccountId = record.CounterpartyAccountNumber = string.Empty;
        }
        Changed("거래 계좌와 본인 이체 지정을 해제했습니다.");
    });
    private void UnlinkTransfer_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        foreach (var record in SelectedTransactions()) { record.TransferAccountId = string.Empty; record.CounterpartyAccountNumber = string.Empty; }
        Changed("본인 이체 지정을 해제했습니다.");
    });
    private void LinkRefund_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var records = SelectedTransactions();
        if (records.Count != 2 || records.Count(r => r.IsIncome) != 1)
            throw new ArgumentException("실제 환불 입금 1건과 원래 지출 1건을 함께 선택해 주세요.");
        var refund = records.Single(r => r.IsIncome);
        var expense = records.Single(r => !r.IsIncome);
        if (refund.TransactionTime < expense.TransactionTime || refund.TransactionTime.Date > DateTime.Today)
            throw new ArgumentException("원래 지출 이후 실제로 입금된 거래만 환불로 연결할 수 있습니다.");
        if (records.Any(r => r.IsExcludedFromTotal || BudgetService.IsOwnTransfer(r, State)))
            throw new ArgumentException("이미 제외되거나 본인 이체인 거래는 환불에 연결할 수 없습니다.");
        if (refund.Amount <= 0 || expense.Amount <= 0) throw new ArgumentException("금액이 0인 거래는 환불로 연결할 수 없습니다.");
        refund.RefundOfTransactionId = expense.Id;
        Changed("실제 환불을 연결했습니다. 원래 지출과 환불 입금에서 환불액을 제외합니다.");
    });
    private void UnlinkRefund_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var ids = SelectedTransactions().Select(r => r.Id).ToHashSet();
        foreach (var record in _viewModel.Transactions.Where(r => ids.Contains(r.Id) || ids.Contains(r.RefundOfTransactionId)))
            record.RefundOfTransactionId = string.Empty;
        Changed("선택한 거래의 환불 연결을 해제했습니다.");
    });

    private void MonthlyGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || MonthlyGrid.SelectedItem is not MonthlyBudgetSummary row) return;
        _statsMonth = row.Month;
        FilterStatistics();
    }
    private void ClearStatsMonth_Click(object sender, RoutedEventArgs e) { _statsMonth = null; MonthlyGrid.SelectedItem = null; FilterStatistics(); }
    private void StatsFilter_Changed(object sender, RoutedEventArgs e) { if (!_refreshing) FilterStatistics(); }
    private void FilterStatistics()
    {
        var end = MonthStart(DateTime.Today);
        var start = _statsMonth ?? end.AddMonths(-12);
        if (_statsMonth.HasValue) end = start.AddMonths(1);
        var rows = _rows.Where(r => r.Record.TransactionTime >= start && r.Record.TransactionTime < end
            && (r.Income != 0 || r.Expense != 0) && Matches(r, StatsSearch.Text.Trim())).ToList();
        StatsSelectionText.Text = _statsMonth.HasValue ? $"{start:yyyy-MM} 내역" : "전체 12개월 내역";
        _statsRows = rows;
        StatsDetailsGrid.ItemsSource = rows;
        GroupsGrid.ItemsSource = rows.GroupBy(r => StatsGrouping.SelectedIndex switch
        {
            1 => string.IsNullOrWhiteSpace(r.Record.Description) ? "내용 없음" : r.Record.Description,
            2 => r.Record.TransactionTime.ToString("yyyy-MM"),
            _ => r.Category
        }).Select(g => new GroupRow { Name = g.Key, Income = g.Sum(r => r.Income), Expense = g.Sum(r => r.Expense) })
            .OrderByDescending(g => g.Expense).ThenBy(g => g.Name).ToList();
    }

    private void GroupsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupsGrid.SelectedItem is not GroupRow group) { StatsDetailsGrid.ItemsSource = _statsRows; return; }
        StatsDetailsGrid.ItemsSource = _statsRows.Where(r => (StatsGrouping.SelectedIndex switch
        {
            1 => string.IsNullOrWhiteSpace(r.Record.Description) ? "내용 없음" : r.Record.Description,
            2 => r.Record.TransactionTime.ToString("yyyy-MM"),
            _ => r.Category
        }) == group.Name).ToList();
    }

    private void CategoriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (!_refreshing && CategoriesGrid.SelectedItem is string category) CategoryName.Text = category; }
    private void AddCategory_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var name = CategoryName.Text.Trim();
        if (name.Length == 0) throw new ArgumentException("분류 이름을 입력해 주세요.");
        AddCategory(name);
        Changed("분류를 추가했습니다.");
    });
    private void RenameCategory_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (CategoriesGrid.SelectedItem is not string original) throw new ArgumentException("변경할 분류를 선택해 주세요.");
        var name = CategoryName.Text.Trim();
        if (name.Length == 0) throw new ArgumentException("분류 이름을 입력해 주세요.");
        if (name != original && State.Categories.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("이미 존재하는 분류 이름입니다.");
        State.Categories[State.Categories.IndexOf(original)] = name;
        foreach (var plan in State.ExpensePlans.Where(p => p.Category == original)) plan.Category = name;
        foreach (var record in _viewModel.Transactions.Where(t => t.SpendingCategory == original)) record.SpendingCategory = name;
        Changed("분류 이름을 변경했습니다.");
    });

    private void DeleteCategory_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (CategoriesGrid.SelectedItem is not string category) throw new ArgumentException("삭제할 분류를 선택해 주세요.");
        if (State.ExpensePlans.Any(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase))
            || _viewModel.Transactions.Any(t => string.Equals(t.SpendingCategory, category, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("거래 또는 예상 항목(이전 기록 포함)에서 사용 중인 분류는 삭제할 수 없습니다.");

        State.Categories.Remove(category);
        CategoryName.Clear();
        Changed("사용하지 않는 분류를 삭제했습니다.");
    });

    public sealed class PlanRow
    {
        private readonly ExpensePlanSummary _summary;
        public PlanRow(ExpensePlanSummary summary) => _summary = summary;
        public ExpensePlan Plan => _summary.Plan;
        public string KindText => Plan.Kind == ExpenseKind.Fixed ? "고정" : "변동";
        public decimal Expected => _summary.Expected;
        public decimal Actual => _summary.Actual;
        public decimal Remaining => _summary.Remaining;
        public decimal Overrun => _summary.Overrun;
        public DateTime DueDate => _summary.DueDate;
    }
    public sealed class TransactionRow
    {
        public TransactionRecord Record { get; init; } = new();
        public decimal Income { get; init; }
        public decimal Expense { get; init; }
        public decimal Net => Income - Expense;
        public string Category { get; init; } = string.Empty;
        public string PlanName { get; init; } = string.Empty;
        public string AccountName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public bool IsOwnTransfer { get; init; }
        public bool IsTransferCandidate { get; init; }
    }
    public sealed class GroupRow
    {
        public string Name { get; init; } = string.Empty;
        public decimal Income { get; init; }
        public decimal Expense { get; init; }
    }
}
