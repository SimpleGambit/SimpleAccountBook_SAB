using System;
using System.Windows.Media;

namespace SimpleAccountBook.Models;

public class DailySummary
{
    private readonly bool _showZeroAmounts;

    private DailySummary(
        string amountText,
        string tooltip,
        Brush amountBrush,
        decimal incomeAmount,
        decimal expenseAmount,
        bool showZeroAmounts)
    {
        AmountText = amountText;
        Tooltip = tooltip;
        AmountBrush = amountBrush;
        IncomeAmount = incomeAmount;
        ExpenseAmount = expenseAmount;
        _showZeroAmounts = showZeroAmounts;
    }

    public string AmountText { get; }
    public string Tooltip { get; }
    public Brush AmountBrush { get; }

    public decimal IncomeAmount { get; }
    public decimal ExpenseAmount { get; }

    public string IncomeText => IncomeAmount > 0 ? $"{IncomeAmount:#,0}" : string.Empty;
    public string ExpenseText => ExpenseAmount > 0 ? $"{ExpenseAmount:#,0}" : string.Empty;

    public static DailySummary Blank { get; } = new DailySummary(string.Empty, string.Empty, Brushes.Transparent, 0, 0, true);

    public static DailySummary FromTotals(DateOnly date, decimal incomeAmount, decimal expenseAmount)
    {
        var net = incomeAmount - expenseAmount;
        var amountText = net == 0 ? string.Empty : net.ToString("+#,0;-#,0");
        var brush = net switch
        {
            > 0 => Brushes.DarkGreen,
            < 0 => Brushes.Firebrick,
            _ => Brushes.DimGray
        };

        var dateLabel = new DateTime(date.Year, date.Month, date.Day);
        var tooltip = $"{dateLabel:yyyy-MM-dd}\n수입: {incomeAmount:#,0}원\n지출: {expenseAmount:#,0}원\n합계: {net:#,0}원";

        // 디버깅 로그 추가
        System.Diagnostics.Debug.WriteLine($"[DailySummary] {date}: Income={incomeAmount:#,0}, Expense={expenseAmount:#,0}");

        return new DailySummary(amountText, tooltip, brush, incomeAmount, expenseAmount, true);
    }

    public static DailySummary CreateEmpty(DateTime date)
    {
        return new DailySummary(string.Empty, $"{date:yyyy-MM-dd}\n거래 없음", Brushes.Transparent, 0, 0, true);
    }


}