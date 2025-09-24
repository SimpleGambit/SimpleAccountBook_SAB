using System;

namespace SimpleAccountBook.Models;

public class DetailSearchResult
{
    public required TransactionRecord Transaction { get; init; }
    public DateTime TransactionTime => Transaction.TransactionTime;
    public string TransactionType => Transaction.TransactionType;
    public decimal TransactionAmount => Transaction.Amount;
    public string Category => Transaction.Category;
    public string Description => Transaction.Description;
    public string ItemName { get; init; } = string.Empty;
    public decimal DetailAmount { get; init; }
    public string Note { get; init; } = string.Empty;
    public bool HasMemoDetail { get; init; }
    public string? SourceFileName { get; init; }
    public string? SourceFilePath { get; init; }
}