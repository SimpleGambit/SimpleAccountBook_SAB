using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace SimpleAccountBook.Models;

public class LoadedFileInfo : INotifyPropertyChanged
{
    private int _transactionCount;
    private DateTime _loadedAt;

    public LoadedFileInfo(string filePath, int transactionCount, DateTime loadedAt)
    {
        FilePath = filePath;
        _transactionCount = transactionCount;
        _loadedAt = loadedAt;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    public int TransactionCount
    {
        get => _transactionCount;
        private set
        {
            if (_transactionCount != value)
            {
                _transactionCount = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime LoadedAt
    {
        get => _loadedAt;
        private set
        {
            if (_loadedAt != value)
            {
                _loadedAt = value;
                OnPropertyChanged();
            }
        }
    }

    public void Update(int transactionCount, DateTime loadedAt)
    {
        TransactionCount = transactionCount;
        LoadedAt = loadedAt;
        OnPropertyChanged(nameof(DisplayText));
    }

    public string DisplayText => $"{FileName} ({TransactionCount}건)";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}