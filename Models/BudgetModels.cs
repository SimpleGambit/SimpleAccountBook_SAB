using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleAccountBook.Models;

public enum ExpenseKind
{
    Fixed,
    Variable
}

public class BudgetState
{
    public List<ExpensePlan> ExpensePlans { get; set; } = new();
    public List<OwnAccount> OwnAccounts { get; set; } = new();
    public List<string> Categories { get; set; } = new()
    {
        "주거비", "대출", "공과금", "식비", "교통비", "구독료", "생활비", "의료비", "급여", "기타"
    };

    public BudgetState CreateSnapshot() => new()
    {
        ExpensePlans = ExpensePlans.Select(p => p.CreateSnapshot()).ToList(),
        OwnAccounts = OwnAccounts.Select(a => a.CreateSnapshot()).ToList(),
        Categories = Categories.ToList()
    };
}

public class ExpensePlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public ExpenseKind Kind { get; set; }
    public decimal Amount { get; set; }
    public int DayOfMonth { get; set; } = 1;
    public int IntervalMonths { get; set; } = 1;
    public DateTime StartMonth { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime? EndMonth { get; set; }
    // A later effective version retains the original payment cycle.
    public DateTime? RecurrenceAnchorMonth { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    public ExpensePlan CreateSnapshot() => (ExpensePlan)MemberwiseClone();
}

public class OwnAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;

    public OwnAccount CreateSnapshot() => (OwnAccount)MemberwiseClone();
}

public class TransactionTotals
{
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public string ExclusionReason { get; set; } = string.Empty;
}

public class ExpensePlanSummary
{
    public ExpensePlan Plan { get; set; } = new();
    public decimal Expected { get; set; }
    public decimal Actual { get; set; }
    public decimal Remaining => Math.Max(0, Expected - Actual);
    public decimal Overrun => Math.Max(0, Actual - Expected);
    public DateTime DueDate { get; set; }
}

public class MonthlyBudgetSummary
{
    public DateTime Month { get; set; }
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Net => Income - Expense;
}

public class CategoryBudgetSummary
{
    public string Category { get; set; } = string.Empty;
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Net => Income - Expense;
}
