using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClosedXML.Excel;
using SimpleAccountBook;
using SimpleAccountBook.Models;
using SimpleAccountBook.Services;
using SimpleAccountBook.ViewModels;

internal static class Program
{
    private static int _passed;
    private static readonly DateTime September = new(2026, 9, 1);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Test("previous twelve complete months and empty months", TestPeriod);
            Test("expected, actual, remaining, overrun and other expenses", TestPlans);
            Test("payment dates, recurrence, history and end month", TestHistory);
            Test("own-account confirmation, exact accounts and candidates", TestTransfers);
            Test("full, partial, multiple and excess refunds", TestRefunds);
            Test("invalid, future and cross-month refunds", TestRefundBoundaries);
            Test("old state compatibility, metadata and independent snapshots", TestPersistence);
            Test("view-model edits and removed refunds recalculate totals", TestViewModel);
            Test("refreshing the same file preserves distinct annotations", TestReimport);
            var sampleIndex = Array.IndexOf(args, "--sample");
            if (sampleIndex >= 0) Test("sample workbook importer", () => TestSample(args[sampleIndex + 1]));
            var renderIndex = Array.IndexOf(args, "--render");
            var stateIndex = Array.IndexOf(args, "--state");
            if (stateIndex >= 0) Test("existing save schema round-trip (read-only)", () => TestExistingState(args[stateIndex + 1]));
            if (renderIndex >= 0) Test("WPF layout and editing workflows", () => TestWindow(args[renderIndex + 1]));
            Console.WriteLine($"PASS: {_passed} regression scenarios.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Test(string name, Action action) { action(); _passed++; Console.WriteLine($"PASS {name}"); }
    private static void Equal<T>(T expected, T actual, string message = "")
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message}: expected {expected}, got {actual}"); }
    private static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static TransactionRecord Tx(decimal amount, bool income = false, DateTime? date = null) => new()
    { Amount = amount, TransactionType = income ? "입금" : "출금", TransactionTime = date ?? September.AddDays(5), Description = "테스트 거래" };
    private static ExpensePlan Plan(decimal amount = 100) => new() { Name = "식비", Kind = ExpenseKind.Variable, Amount = amount, StartMonth = September, Category = "식비" };

    private static void TestPeriod()
    {
        var state = new BudgetState();
        var records = new[] { Tx(10, true, new DateTime(2025, 10, 1)), Tx(20, false, new DateTime(2026, 9, 30, 23, 59, 59)),
            Tx(40, true, new DateTime(2026, 10, 1)), Tx(80, false, new DateTime(2025, 9, 30)) };
        var months = BudgetService.GetMonthlySummaries(records, state, new DateTime(2026, 10, 20));
        Equal(12, months.Count);
        Equal(new DateTime(2025, 10, 1), months.First().Month);
        Equal(September, months.Last().Month);
        Equal(10m, months.Sum(m => m.Income)); Equal(20m, months.Sum(m => m.Expense));
        Equal(10, months.Count(m => m.Income == 0 && m.Expense == 0));
    }

    private static void TestPlans()
    {
        var state = new BudgetState(); var plan = Plan(); state.ExpensePlans.Add(plan);
        var one = Tx(60); var two = Tx(50); var other = Tx(15);
        one.ExpensePlanId = two.ExpensePlanId = plan.Id;
        var records = new[] { one, two, other };
        var result = BudgetService.GetPlanSummaries(records, state, September).Single();
        Equal(100m, result.Expected); Equal(110m, result.Actual); Equal(0m, result.Remaining); Equal(10m, result.Overrun);
        Equal(other.Id, BudgetService.GetOtherExpenses(records, state, September).Single().Id);
        two.Amount = 10;
        result = BudgetService.GetPlanSummaries(records, state, September).Single();
        Equal(30m, result.Remaining); Equal(0m, result.Overrun);
        Equal(115m, records.Sum(t => t.Amount) + result.Remaining, "forecast must not double-count settled amounts");
        Equal("식비", BudgetService.GetCategory(one, state));
        one.SpendingCategory = "외식";
        Equal("외식", BudgetService.GetCategory(one, state));
        plan.Kind = ExpenseKind.Fixed;
        Equal(70m, BudgetService.GetPlanSummaries(records, state, September).Single().Actual);
    }

    private static void TestHistory()
    {
        var state = new BudgetState(); var plan = Plan(); plan.StartMonth = new DateTime(2024, 1, 1); plan.DayOfMonth = 31;
        BudgetService.SavePlan(state, plan, plan.StartMonth);
        Equal(new DateTime(2024, 2, 29), BudgetService.GetPlanSummaries(Array.Empty<TransactionRecord>(), state, new DateTime(2024, 2, 1)).Single().DueDate);
        var edit = state.ExpensePlans.Single().CreateSnapshot(); edit.Amount = 200;
        BudgetService.SavePlan(state, edit, new DateTime(2024, 6, 1));
        Equal(100m, BudgetService.GetActivePlans(state, new DateTime(2024, 5, 1)).Single().Amount);
        Equal(200m, BudgetService.GetActivePlans(state, new DateTime(2024, 6, 1)).Single().Amount);
        BudgetService.EndPlan(state, plan.Id, new DateTime(2024, 8, 1));
        Equal(0, BudgetService.GetActivePlans(state, new DateTime(2024, 8, 1)).Count);
        Equal(1, BudgetService.GetActivePlans(state, new DateTime(2024, 7, 1)).Count);
        var annual = Plan(); annual.IntervalMonths = 12; annual.StartMonth = new DateTime(2024, 1, 1);
        var annualState = new BudgetState(); BudgetService.SavePlan(annualState, annual, annual.StartMonth);
        edit = annualState.ExpensePlans.Single().CreateSnapshot(); edit.Amount = 300;
        BudgetService.SavePlan(annualState, edit, new DateTime(2024, 6, 1));
        Equal(0, BudgetService.GetActivePlans(annualState, new DateTime(2024, 6, 1)).Count, "annual cycle must not shift on editing");
        Equal(300m, BudgetService.GetActivePlans(annualState, new DateTime(2025, 1, 1)).Single().Amount);
        try { var invalid = Plan(); invalid.Amount = -1; BudgetService.SavePlan(state, invalid, September); throw new Exception("negative plan accepted"); }
        catch (ArgumentException) { }
    }

    private static void TestTransfers()
    {
        var a = new OwnAccount { Name = "본인 A", AccountNumber = "123-456" };
        var b = new OwnAccount { Name = "본인 B", AccountNumber = "987-654" };
        var state = new BudgetState { OwnAccounts = new() { a, b } };
        var debit = Tx(100); debit.SourceAccountId = a.Id;
        var credit = Tx(100, true); credit.SourceAccountId = b.Id;
        var records = new[] { debit, credit };
        var totals = BudgetService.GetAdjustedAmounts(records, state);
        Equal(100m, totals[debit.Id].Expense); Equal(100m, totals[credit.Id].Income);
        Equal(2, BudgetService.GetTransferCandidateIds(records, state).Count, "candidates remain counted");
        debit.TransferAccountId = b.Id; credit.TransferAccountId = a.Id;
        totals = BudgetService.GetAdjustedAmounts(records, state);
        Equal(0m, totals[debit.Id].Expense); Equal(0m, totals[credit.Id].Income);
        debit.TransferAccountId = a.Id;
        True(!BudgetService.IsOwnTransfer(debit, state), "same account is invalid");
        debit.TransferAccountId = string.Empty; debit.CounterpartyAccountNumber = "987654";
        True(BudgetService.IsOwnTransfer(debit, state), "exact counterpart account");
        debit.CounterpartyAccountNumber = "987***";
        True(!BudgetService.IsOwnTransfer(debit, state), "masked account is insufficient");
        debit.CounterpartyAccountNumber = "987654"; debit.SourceAccountId = "unknown";
        True(!BudgetService.IsOwnTransfer(debit, state), "source ownership must be known");
        debit.SourceAccountId = a.Id; state.OwnAccounts.Add(new OwnAccount { AccountNumber = "987654" });
        True(!BudgetService.IsOwnTransfer(debit, state), "ambiguous account number must not exclude");
        var fee = Tx(1); fee.SourceAccountId = a.Id;
        Equal(1m, BudgetService.GetAdjustedAmounts(new[] { fee }, state)[fee.Id].Expense);
    }

    private static void TestRefunds()
    {
        var state = new BudgetState(); var expense = Tx(100); var refund = Tx(40, true, September.AddDays(6));
        refund.RefundOfTransactionId = expense.Id;
        var totals = BudgetService.GetAdjustedAmounts(new[] { expense, refund }, state);
        Equal(60m, totals[expense.Id].Expense); Equal(0m, totals[refund.Id].Income);
        var refund2 = Tx(80, true, September.AddDays(7)); refund2.RefundOfTransactionId = expense.Id;
        totals = BudgetService.GetAdjustedAmounts(new[] { expense, refund, refund2 }, state);
        Equal(0m, totals[expense.Id].Expense); Equal(20m, totals[refund2.Id].Income); Equal("전액 환불", totals[expense.Id].ExclusionReason);
        expense.IsExcludedFromTotal = true;
        totals = BudgetService.GetAdjustedAmounts(new[] { expense, refund }, state);
        Equal(0m, totals[expense.Id].Expense); Equal(0m, totals[refund.Id].Income);
        expense.IsExcludedFromTotal = false; refund.Amount = 100;
        totals = BudgetService.GetAdjustedAmounts(new[] { expense, refund }, state);
        Equal(0m, totals[expense.Id].Expense); Equal(0m, totals[refund.Id].Income);
    }

    private static void TestRefundBoundaries()
    {
        var state = new BudgetState(); var expense = Tx(100, false, new DateTime(2026, 1, 5));
        var refund = Tx(40, true, new DateTime(2026, 2, 5)); refund.RefundOfTransactionId = expense.Id;
        var records = new[] { expense, refund };
        var months = BudgetService.GetMonthlySummaries(records, state, new DateTime(2026, 3, 1));
        Equal(60m, months.Single(m => m.Month == new DateTime(2026, 1, 1)).Expense);
        Equal(0m, months.Single(m => m.Month == new DateTime(2026, 2, 1)).Income);
        refund.TransactionTime = new DateTime(2026, 4, 1);
        Equal(100m, BudgetService.GetAdjustedAmounts(records, state, new DateTime(2026, 3, 1))[expense.Id].Expense);
        refund.TransactionTime = expense.TransactionTime.AddDays(-1);
        Equal(100m, BudgetService.GetAdjustedAmounts(records, state)[expense.Id].Expense);
        refund.TransactionTime = new DateTime(2026, 2, 5); refund.RefundOfTransactionId = "missing";
        Equal(40m, BudgetService.GetAdjustedAmounts(records, state)[refund.Id].Income);
    }

    private static void TestPersistence()
    {
        var legacy = JsonSerializer.Deserialize<AppState>("{\"CurrentMonth\":\"2026-09-01T00:00:00\",\"ManualTransactions\":[{\"Amount\":25,\"TransactionType\":\"출금\",\"TransactionTime\":\"2026-09-05T00:00:00\"}]}")!;
        True(legacy.Budget != null, "old state must receive budget defaults");
        var vm = new AccountBookViewModel(new ExcelImportService()); vm.RestoreStateAsync(legacy).GetAwaiter().GetResult();
        vm.CurrentMonth = September;
        Equal(25m, vm.MonthlyExpense); True(vm.Transactions[0].Id.Length > 0, "migrated stable ID");
        var record = vm.Transactions[0]; var plan = Plan(); vm.Budget.ExpensePlans.Add(plan);
        record.ExpensePlanId = plan.Id; record.SpendingCategory = "식비"; record.Tags = "가족, 점심";
        record.SourceAccountId = "source"; record.TransferAccountId = "target"; record.CounterpartyAccountNumber = "123"; record.RefundOfTransactionId = "refund-original";
        var snapshot = vm.CreateAppState(null); var json = JsonSerializer.Serialize(snapshot);
        record.Tags = "changed"; plan.Amount = 999;
        Equal("가족, 점심", snapshot.ManualTransactions.Single().Tags);
        Equal(100m, snapshot.Budget.ExpensePlans.Single().Amount);
        var restored = new AccountBookViewModel(new ExcelImportService());
        restored.RestoreStateAsync(JsonSerializer.Deserialize<AppState>(json)).GetAwaiter().GetResult();
        var result = restored.Transactions.Single();
        Equal(record.Id, result.Id); Equal("source", result.SourceAccountId); Equal("target", result.TransferAccountId);
        Equal("123", result.CounterpartyAccountNumber); Equal("refund-original", result.RefundOfTransactionId); Equal(plan.Id, result.ExpensePlanId);
    }

    private static void TestViewModel()
    {
        var expense = Tx(100); var refund = Tx(40, true, September.AddDays(6)); refund.RefundOfTransactionId = expense.Id;
        var vm = new AccountBookViewModel(new ExcelImportService());
        vm.RestoreStateAsync(new AppState { CurrentMonth = September, SelectedDate = September.AddDays(5), ManualTransactions = new() { expense, refund } }).GetAwaiter().GetResult();
        Equal(60m, vm.MonthlyExpense); Equal(0m, vm.MonthlyIncome);
        vm.Transactions.First(t => !t.IsIncome).Amount = 120;
        Equal(80m, vm.MonthlyExpense);
        vm.RemoveTransaction(vm.Transactions.Single(t => t.IsIncome));
        Equal(120m, vm.MonthlyExpense);
        var day = vm.DailySummaries[DateOnly.FromDateTime(expense.TransactionTime)]; Equal(120m, day.ExpenseAmount);
    }

    private static void TestReimport()
    {
        var importer = new FakeImporter(); var vm = new AccountBookViewModel(importer);
        var path = Path.Combine(Path.GetTempPath(), "budget-regression-missing.xlsx");
        vm.LoadFromFileAsync(path).GetAwaiter().GetResult();
        Equal(2, vm.Transactions.Count);
        var ids = vm.Transactions.Select(t => t.Id).ToArray();
        vm.Transactions[0].Tags = "first"; vm.Transactions[1].Tags = "second"; vm.Transactions[0].ExpensePlanId = "food";
        vm.LoadFromFileAsync(path).GetAwaiter().GetResult();
        Equal(ids[0], vm.Transactions[0].Id); Equal(ids[1], vm.Transactions[1].Id);
        Equal("first", vm.Transactions[0].Tags); Equal("second", vm.Transactions[1].Tags); Equal("food", vm.Transactions[0].ExpensePlanId);
    }
    private sealed class FakeImporter : IExcelImportService
    {
        public Task<IList<TransactionRecord>> LoadAsync(string filePath, string? password = null) => Task.FromResult<IList<TransactionRecord>>(new List<TransactionRecord> { Tx(20), Tx(20) });
        public Task<IList<TransactionRecord>> LoadAsync(ReadOnlyMemory<byte> data, string? fileNameHint = null, string? password = null) => LoadAsync("");
    }

    private static void TestSample(string path)
    {
        var records = new ExcelImportService().LoadAsync(path).GetAwaiter().GetResult();
        using var workbook = new XLWorkbook(path);
        var source = workbook.Worksheets.First().RowsUsed().Where(r => r.RowNumber() > 11)
            .Where(r => r.Cell(3).GetString() is "입금" or "출금")
            .Select(r => new { Income = r.Cell(3).GetString() == "입금", Amount = decimal.Parse(r.Cell(4).GetString(), System.Globalization.NumberStyles.Number) }).ToList();
        Equal(141, source.Count); Equal(1, source.Count(r => r.Amount == 0));
        // The existing importer deliberately skips zero-amount rows; retain that behavior.
        Equal(140, records.Count);
        Equal(source.Count(r => r.Income && r.Amount != 0), records.Count(t => t.IsIncome));
        Equal(source.Count(r => !r.Income && r.Amount != 0), records.Count(t => !t.IsIncome));
        True(records.All(t => t.TransactionTime >= September && t.TransactionTime < September.AddMonths(1)), "sample dates");
        Equal(records.Count, records.Select(t => t.Id).Distinct().Count());
        var totals = BudgetService.GetAdjustedAmounts(records, new BudgetState());
        Equal(source.Where(t => t.Income).Sum(t => Math.Abs(t.Amount)), totals.Values.Sum(t => t.Income));
        Equal(source.Where(t => !t.Income).Sum(t => Math.Abs(t.Amount)), totals.Values.Sum(t => t.Expense));
    }

    private static void TestExistingState(string path)
    {
        var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var roundTrip = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state))!;
        Equal(state.Files.Count, roundTrip.Files.Count);
        Equal(state.Files.Sum(f => f.Transactions.Count), roundTrip.Files.Sum(f => f.Transactions.Count));
        Equal(state.ManualTransactions.Count, roundTrip.ManualTransactions.Count);
        Equal(state.Memos.Count, roundTrip.Memos.Count);
        Equal(state.Files.SelectMany(f => f.Transactions).Sum(t => t.Amount), roundTrip.Files.SelectMany(f => f.Transactions).Sum(t => t.Amount));
        True(roundTrip.Files.SelectMany(f => f.Transactions).All(t => !string.IsNullOrWhiteSpace(t.Id)), "legacy saved rows receive stable identities");
    }

    private static void TestWindow(string output)
    {
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var vm = new AccountBookViewModel(new ExcelImportService());
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        var budget = new BudgetState(); var food = Plan(500000); food.StartMonth = month.AddMonths(-12); budget.ExpensePlans.Add(food);
        var rent = Plan(650000); rent.Name = "월세"; rent.Kind = ExpenseKind.Fixed; rent.Category = "주거비"; rent.StartMonth = food.StartMonth; budget.ExpensePlans.Add(rent);
        var subscription = Plan(15000); subscription.Name = "구독료"; subscription.Kind = ExpenseKind.Fixed; subscription.StartMonth = food.StartMonth; budget.ExpensePlans.Add(subscription);
        var account = new OwnAccount { Name = "생활비 계좌", AccountNumber = "111-222-333" }; budget.OwnAccounts.Add(account);
        budget.OwnAccounts.Add(new OwnAccount { Name = "저축 계좌" });
        var records = new List<TransactionRecord>();
        for (var i = 0; i < 12; i++)
        {
            var date = month.AddMonths(-i).AddDays(5);
            records.Add(Tx(2800000 + i * 10000, true, date));
            var expense = Tx(530000 - i * 7000, false, date); expense.ExpensePlanId = food.Id; expense.Description = "식료품 · 가족 식사"; expense.Tags = "가족"; expense.SourceAccountId = account.Id; records.Add(expense);
            var housing = Tx(650000, false, date); housing.ExpensePlanId = rent.Id; housing.Description = "월세 납부"; records.Add(housing);
        }
        var other = Tx(80000, false, month.AddDays(12)); other.Description = "생활용품 구입"; records.Add(other);
        vm.RestoreStateAsync(new AppState { Budget = budget, CurrentMonth = month, SelectedDate = month.AddDays(5), ManualTransactions = records }).GetAwaiter().GetResult();
        var window = new BudgetWindow(vm);
        var tabs = (TabControl)window.FindName("MainTabs");
        var content = (FrameworkElement)window.Content;
        foreach (var theme in new[] { "Light", "Dark" })
        {
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/SimpleAccountBook;component/Themes/{theme}Theme.xaml", UriKind.Relative) });
            for (var tab = 0; tab < tabs.Items.Count; tab++)
            {
                tabs.SelectedIndex = tab;
                content.Measure(new Size(1230, 795)); content.Arrange(new Rect(0, 0, 1230, 795)); content.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var image = new RenderTargetBitmap(1230, 795, 96, 96, PixelFormats.Pbgra32);
                var backdrop = new DrawingVisual();
                using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle((Brush)app.Resources["WindowBackgroundBrush"], null, new Rect(0, 0, 1230, 795));
                image.Render(backdrop); image.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = File.Create(Path.Combine(output, $"{theme}-{tab}.png")); encoder.Save(stream);
            }
        }
        tabs.SelectedIndex = 1;
        var grid = (DataGrid)window.FindName("TransactionsGrid"); grid.SelectedItems.Clear(); grid.SelectedItems.Add(grid.Items[0]);
        ((TextBox)window.FindName("BulkCategory")).Text = "검증 분류";
        InvokeClick(window, "SetCategory_Click");
        True(vm.Transactions.Any(t => t.SpendingCategory == "검증 분류"), "bulk category workflow");
        tabs.SelectedIndex = 0;
        ((TextBox)window.FindName("PlanName")).Text = "검증 예정 지출";
        ((TextBox)window.FindName("PlanAmount")).Text = "12000";
        InvokeClick(window, "SavePlan_Click");
        True(vm.Budget.ExpensePlans.Any(p => p.Name == "검증 예정 지출"), "plan save workflow");
        tabs.SelectedIndex = 1;
        grid.SelectedItems.Clear();
        grid.SelectedItems.Add(grid.Items.Cast<BudgetWindow.TransactionRow>().First(r => r.Record.Id == other.Id));
        ((ComboBox)window.FindName("SourceAccount")).SelectedItem = vm.Budget.OwnAccounts[0];
        InvokeClick(window, "SetSourceAccount_Click");
        ((ComboBox)window.FindName("TransferAccount")).SelectedItem = vm.Budget.OwnAccounts[1];
        InvokeClick(window, "ConfirmTransfer_Click");
        True(BudgetService.IsOwnTransfer(vm.Transactions.Single(t => t.Id == other.Id), vm.Budget), "manual transfer confirmation workflow");
        InvokeClick(window, "UnlinkTransfer_Click");
        True(!BudgetService.IsOwnTransfer(vm.Transactions.Single(t => t.Id == other.Id), vm.Budget), "manual transfer reversal");
        var refundRecord = vm.Transactions.First(t => t.IsIncome && t.TransactionTime.Month == month.Month && t.TransactionTime.Year == month.Year);
        var expenseRecord = vm.Transactions.First(t => !t.IsIncome && t.ExpensePlanId == food.Id && t.TransactionTime.Month == month.Month && t.TransactionTime.Year == month.Year);
        grid.SelectedItems.Clear();
        grid.SelectedItems.Add(grid.Items.Cast<BudgetWindow.TransactionRow>().Single(r => r.Record.Id == refundRecord.Id));
        grid.SelectedItems.Add(grid.Items.Cast<BudgetWindow.TransactionRow>().Single(r => r.Record.Id == expenseRecord.Id));
        InvokeClick(window, "LinkRefund_Click");
        Equal(expenseRecord.Id, refundRecord.RefundOfTransactionId);
        InvokeClick(window, "UnlinkRefund_Click");
        Equal(string.Empty, refundRecord.RefundOfTransactionId);
        var filePath = Path.Combine(output, "roundtrip.sab");
        var service = new AppStateService();
        service.SaveAsync(vm.CreateAppState(null), filePath).GetAwaiter().GetResult();
        var saved = service.LoadAsync(filePath).GetAwaiter().GetResult()!;
        Equal(vm.Budget.OwnAccounts.Count, saved.Budget.OwnAccounts.Count);
        Equal(vm.Budget.ExpensePlans.Count, saved.Budget.ExpensePlans.Count);
        window.Close();
    }
    private static void InvokeClick(BudgetWindow window, string name) => typeof(BudgetWindow)
        .GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
}
