using System;
using System.Collections.Generic;
using System.Linq;
using SimpleAccountBook.Models;

namespace SimpleAccountBook.Services;

public static class BudgetService
{
    public static Dictionary<string, TransactionTotals> GetAdjustedAmounts(
        IEnumerable<TransactionRecord> transactions, BudgetState state, DateTime? asOfDate = null)
    {
        var records = transactions.GroupBy(t => t.Id).Select(g => g.First()).ToList();
        var byId = records.ToDictionary(t => t.Id);
        var totals = new Dictionary<string, TransactionTotals>();
        foreach (var transaction in records)
        {
            var reason = transaction.IsExcludedFromTotal ? "사용자 제외"
                : IsOwnTransfer(transaction, state) ? "본인 계좌 간 이체" : string.Empty;
            totals[transaction.Id] = new TransactionTotals
            {
                Income = reason.Length == 0 && transaction.IsIncome ? transaction.Amount : 0,
                Expense = reason.Length == 0 && !transaction.IsIncome ? transaction.Amount : 0,
                ExclusionReason = reason
            };
        }

        var refunded = new Dictionary<string, decimal>();
        var refundCutoff = (asOfDate ?? DateTime.Today).Date;
        foreach (var refund in records.Where(t => t.IsIncome && t.TransactionTime.Date <= refundCutoff && !string.IsNullOrWhiteSpace(t.RefundOfTransactionId))
                     .OrderBy(t => t.TransactionTime).ThenBy(t => t.Id, StringComparer.Ordinal))
        {
            if (!byId.TryGetValue(refund.RefundOfTransactionId, out var original)
                || original.IsIncome || refund.TransactionTime < original.TransactionTime
                || IsOwnTransfer(original, state) || IsOwnTransfer(refund, state))
                continue;

            var alreadyRefunded = refunded.GetValueOrDefault(original.Id);
            var applied = Math.Min(refund.Amount, Math.Max(0, original.Amount - alreadyRefunded));
            if (applied == 0)
                continue;

            refunded[original.Id] = alreadyRefunded + applied;
            var originalTotal = totals[original.Id];
            var refundTotal = totals[refund.Id];
            originalTotal.Expense = Math.Max(0, originalTotal.Expense - applied);
            refundTotal.Income = Math.Max(0, refundTotal.Income - applied);
            if (originalTotal.ExclusionReason.Length == 0 || originalTotal.ExclusionReason == "부분 환불")
                originalTotal.ExclusionReason = alreadyRefunded + applied >= original.Amount ? "전액 환불" : "부분 환불";
            if (refundTotal.ExclusionReason.Length == 0)
                refundTotal.ExclusionReason = refundTotal.Income == 0 ? "환불 입금" : "환불 금액 제외 (초과 입금 포함)";
        }
        return totals;
    }

    public static List<ExpensePlanSummary> GetPlanSummaries(
        IEnumerable<TransactionRecord> transactions, BudgetState state, DateTime month)
    {
        month = MonthStart(month);
        var nextMonth = month.AddMonths(1);
        var records = transactions.ToList();
        var totals = GetAdjustedAmounts(records, state);
        var actualByPlan = records.Where(t => t.TransactionTime >= month && t.TransactionTime < nextMonth
                                             && !string.IsNullOrWhiteSpace(t.ExpensePlanId))
            .GroupBy(t => t.ExpensePlanId)
            .ToDictionary(g => g.Key, g => g.Sum(t => totals[t.Id].Expense));
        return GetActivePlans(state, month).Select(plan => new ExpensePlanSummary
        {
            Plan = plan,
            Expected = plan.Amount,
            Actual = actualByPlan.GetValueOrDefault(plan.Id),
            DueDate = new DateTime(month.Year, month.Month, Math.Min(plan.DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)))
        }).ToList();
    }

    public static List<MonthlyBudgetSummary> GetMonthlySummaries(
        IEnumerable<TransactionRecord> transactions, BudgetState state, DateTime asOfDate)
    {
        var end = MonthStart(asOfDate);
        var start = end.AddMonths(-12);
        var records = transactions.ToList();
        var totals = GetAdjustedAmounts(records, state, asOfDate);
        var summaries = Enumerable.Range(0, 12).Select(i => new MonthlyBudgetSummary { Month = start.AddMonths(i) }).ToList();
        foreach (var transaction in records.Where(t => t.TransactionTime >= start && t.TransactionTime < end))
        {
            var index = MonthDistance(start, transaction.TransactionTime);
            summaries[index].Income += totals[transaction.Id].Income;
            summaries[index].Expense += totals[transaction.Id].Expense;
        }
        return summaries;
    }

    public static List<CategoryBudgetSummary> GetCategorySummaries(
        IEnumerable<TransactionRecord> transactions, BudgetState state, DateTime start, DateTime endExclusive)
    {
        var records = transactions.ToList();
        var totals = GetAdjustedAmounts(records, state);
        return records.Where(t => t.TransactionTime >= start && t.TransactionTime < endExclusive)
            .GroupBy(t => GetCategory(t, state))
            .Select(g => new CategoryBudgetSummary
            {
                Category = g.Key,
                Income = g.Sum(t => totals[t.Id].Income),
                Expense = g.Sum(t => totals[t.Id].Expense)
            })
            .Where(s => s.Income != 0 || s.Expense != 0)
            .OrderByDescending(s => s.Expense).ThenBy(s => s.Category).ToList();
    }

    public static List<TransactionRecord> GetOtherExpenses(
        IEnumerable<TransactionRecord> transactions, BudgetState state, DateTime month)
    {
        month = MonthStart(month);
        var nextMonth = month.AddMonths(1);
        var records = transactions.ToList();
        var totals = GetAdjustedAmounts(records, state);
        var activeIds = GetActivePlans(state, month).Select(p => p.Id).ToHashSet();
        return records.Where(t => t.TransactionTime >= month && t.TransactionTime < nextMonth
                                  && totals[t.Id].Expense > 0 && !activeIds.Contains(t.ExpensePlanId))
            .OrderByDescending(t => t.TransactionTime).ToList();
    }

    public static List<ExpensePlan> GetActivePlans(BudgetState state, DateTime month)
    {
        month = MonthStart(month);
        return state.ExpensePlans.Where(p => IsDue(p, month))
            .GroupBy(p => p.Id).Select(g => g.OrderByDescending(p => p.StartMonth).First())
            .OrderBy(p => p.Kind).ThenBy(p => p.DayOfMonth).ThenBy(p => p.Name).ToList();
    }

    public static string GetCategory(TransactionRecord transaction, BudgetState state)
    {
        if (!string.IsNullOrWhiteSpace(transaction.SpendingCategory))
            return transaction.SpendingCategory.Trim();
        if (!string.IsNullOrWhiteSpace(transaction.ExpensePlanId))
        {
            var month = MonthStart(transaction.TransactionTime);
            var plan = state.ExpensePlans.Where(p => p.Id == transaction.ExpensePlanId && IsDue(p, month))
                .OrderByDescending(p => p.StartMonth).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(plan?.Category))
                return plan.Category.Trim();
        }
        return "미분류";
    }

    public static bool IsOwnTransfer(TransactionRecord transaction, BudgetState state)
    {
        var source = state.OwnAccounts.FirstOrDefault(a => a.Id == transaction.SourceAccountId);
        if (source == null)
            return false;
        if (!string.IsNullOrWhiteSpace(transaction.TransferAccountId))
            return transaction.TransferAccountId != source.Id
                   && state.OwnAccounts.Any(a => a.Id == transaction.TransferAccountId);

        var counterparty = NormalizeAccountNumber(transaction.CounterpartyAccountNumber);
        if (counterparty.Length == 0)
            return false;
        var matches = state.OwnAccounts.Where(a => NormalizeAccountNumber(a.AccountNumber) == counterparty).ToList();
        return matches.Count == 1 && matches[0].Id != source.Id;
    }

    public static string NormalizeAccountNumber(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            return string.Empty;
        var normalized = string.Concat(accountNumber.Where(c => !char.IsWhiteSpace(c) && c != '-'));
        // Masked account numbers cannot establish an exact account match.
        return normalized.All(c => c >= '0' && c <= '9') ? normalized : string.Empty;
    }

    public static HashSet<string> GetTransferCandidateIds(IEnumerable<TransactionRecord> transactions, BudgetState state)
    {
        var records = transactions.Where(t => t.Amount > 0 && !t.IsExcludedFromTotal
            && !IsOwnTransfer(t, state) && string.IsNullOrEmpty(t.RefundOfTransactionId)).ToList();
        var ownIds = state.OwnAccounts.Select(a => a.Id).ToHashSet();
        var candidates = records.Where(t => t.Category.Contains("이체", StringComparison.Ordinal)
            || t.Category.Contains("세이프박스", StringComparison.Ordinal)).Select(t => t.Id).ToHashSet();
        foreach (var amountGroup in records.Where(t => ownIds.Contains(t.SourceAccountId)).GroupBy(t => t.Amount))
        {
            var grouped = amountGroup.ToList();
            foreach (var record in grouped)
                if (grouped.Any(other => other.IsIncome != record.IsIncome && other.SourceAccountId != record.SourceAccountId
                    && Math.Abs((other.TransactionTime - record.TransactionTime).TotalDays) <= 1))
                    candidates.Add(record.Id);
        }
        return candidates;
    }

    public static void SavePlan(BudgetState state, ExpensePlan draft, DateTime effectiveMonth)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
            throw new ArgumentException("예상 지출 이름을 입력해 주세요.");
        if (draft.Amount < 0)
            throw new ArgumentException("예상 금액은 0 이상이어야 합니다.");
        if (draft.DayOfMonth < 1 || draft.DayOfMonth > 31)
            throw new ArgumentException("결제 예정일은 1일부터 31일 사이로 입력해 주세요.");
        if (draft.IntervalMonths < 1 || draft.IntervalMonths > 12)
            throw new ArgumentException("반복 주기는 1개월부터 12개월 사이로 입력해 주세요.");
        if (!Enum.IsDefined(draft.Kind))
            throw new ArgumentException("고정 지출 또는 변동 지출을 선택해 주세요.");

        effectiveMonth = MonthStart(effectiveMonth);
        var requestedStart = MonthStart(draft.StartMonth);
        var start = requestedStart > effectiveMonth ? requestedStart : effectiveMonth;
        var end = draft.EndMonth.HasValue ? MonthStart(draft.EndMonth.Value) : (DateTime?)null;
        if (end.HasValue && end < start)
            throw new ArgumentException("종료 월은 적용 시작 월보다 빠를 수 없습니다.");

        var id = string.IsNullOrWhiteSpace(draft.Id) ? Guid.NewGuid().ToString("N") : draft.Id;
        var previous = state.ExpensePlans.Where(p => p.Id == id && MonthStart(p.StartMonth) == requestedStart)
            .OrderByDescending(p => p.StartMonth).FirstOrDefault();
        var anchor = previous?.RecurrenceAnchorMonth ?? previous?.StartMonth ?? requestedStart;
        var saved = new ExpensePlan
        {
            Id = id,
            Name = draft.Name.Trim(),
            Kind = draft.Kind,
            Amount = draft.Amount,
            DayOfMonth = draft.DayOfMonth,
            IntervalMonths = draft.IntervalMonths,
            StartMonth = start,
            EndMonth = end,
            RecurrenceAnchorMonth = MonthStart(anchor),
            Category = draft.Category?.Trim() ?? string.Empty,
            Note = draft.Note?.Trim() ?? string.Empty
        };
        EndPlan(state, id, start);
        state.ExpensePlans.Add(saved);
        if (saved.Category.Length > 0 && !state.Categories.Contains(saved.Category, StringComparer.OrdinalIgnoreCase))
            state.Categories.Add(saved.Category);
    }

    public static void EndPlan(BudgetState state, string planId, DateTime effectiveMonth)
    {
        effectiveMonth = MonthStart(effectiveMonth);
        state.ExpensePlans.RemoveAll(p => p.Id == planId && MonthStart(p.StartMonth) >= effectiveMonth);
        foreach (var plan in state.ExpensePlans.Where(p => p.Id == planId && (!p.EndMonth.HasValue || MonthStart(p.EndMonth.Value) >= effectiveMonth)))
            plan.EndMonth = effectiveMonth.AddMonths(-1);
    }

    private static bool IsDue(ExpensePlan plan, DateTime month)
    {
        if (plan.IntervalMonths < 1 || plan.DayOfMonth < 1 || plan.DayOfMonth > 31
            || month < MonthStart(plan.StartMonth)
            || (plan.EndMonth.HasValue && month > MonthStart(plan.EndMonth.Value)))
            return false;
        var distance = MonthDistance(plan.RecurrenceAnchorMonth ?? plan.StartMonth, month);
        return distance >= 0 && distance % plan.IntervalMonths == 0;
    }

    private static DateTime MonthStart(DateTime date) => new(date.Year, date.Month, 1);
    private static int MonthDistance(DateTime start, DateTime end) => (end.Year - start.Year) * 12 + end.Month - start.Month;
}
