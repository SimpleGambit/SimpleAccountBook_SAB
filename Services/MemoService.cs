using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SimpleAccountBook.Models;

namespace SimpleAccountBook.Services;

public class MemoService
{
    private readonly Dictionary<string, TransactionMemo> _memos = new Dictionary<string, TransactionMemo>();

    public event EventHandler? MemosChanged;

    public TransactionMemo? GetMemo(TransactionRecord transaction)
    {
        var key = GetMemoKey(transaction);
        return _memos.TryGetValue(key, out var memo) ? memo : null;
    }

    public void SaveMemo(TransactionRecord transaction, TransactionMemo memo)
    {
        var key = GetMemoKey(transaction);
        memo.LastModified = DateTime.Now;
        _memos[key] = memo;
        OnMemosChanged();
    }

    public void DeleteMemo(TransactionRecord transaction)
    {
        var key = GetMemoKey(transaction);
        if (_memos.Remove(key))
        {
            OnMemosChanged();
        }

    }

    public void ClearAllMemos()
    {
        if (_memos.Count == 0)
        {
            return;
        }

        _memos.Clear();
        OnMemosChanged();
    }

    public Dictionary<string, MemoSnapshot> CreateSnapshot()
    {
        return _memos.ToDictionary(
            kvp => kvp.Key,
            kvp => new MemoSnapshot
            {
                LastModified = kvp.Value.LastModified,
                Details = kvp.Value.Details
                    .Select(detail => new MemoDetailSnapshot
                    {
                        ItemName = detail.ItemName,
                        Amount = detail.Amount,
                        Note = detail.Note
                    })
                    .ToList()
            });
    }

    public void LoadFromSnapshot(Dictionary<string, MemoSnapshot>? snapshot)
    {
        _memos.Clear();

        if (snapshot is null)
        {
            OnMemosChanged();
            return;
        }

        foreach (var (key, memoSnapshot) in snapshot)
        {
            var memo = new TransactionMemo
            {
                LastModified = memoSnapshot.LastModified,
                Details = new ObservableCollection<MemoDetail>(
                    memoSnapshot.Details?.Select(detail => new MemoDetail
                    {
                        ItemName = detail.ItemName,
                        Amount = detail.Amount,
                        Note = detail.Note
                    }) ?? Array.Empty<MemoDetail>())
            };

            _memos[key] = memo;
        }

        OnMemosChanged();
    }
    private static string GetMemoKey(TransactionRecord transaction)
    {
        return $"{transaction.TransactionTime:yyyy-MM-dd_HH:mm:ss}_{transaction.Amount}_{transaction.TransactionType}";
    }

    private void OnMemosChanged()
    {
        MemosChanged?.Invoke(this, EventArgs.Empty);
    }
}