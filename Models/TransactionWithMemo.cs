using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace SimpleAccountBook.Models
{
    /// <summary>
    /// UI에서 사용할 메모 기능이 포함된 거래 모델
    /// </summary>
    public class TransactionWithMemo : INotifyPropertyChanged
    {
        private readonly TransactionRecord _transaction;
        private bool _isExpanded;
        private TransactionMemo? _memo;
        public TransactionRecord Transaction => _transaction;
        public TransactionMemo Memo
        {
            get
            {
                if (_memo is null)
                {
                    _memo = new TransactionMemo();
                    AttachMemoHandlers(_memo);
                }

                return _memo;
            }
            set
            {
                var newValue = value ?? new TransactionMemo();
                if (ReferenceEquals(_memo, newValue))
                {
                    return;
                }
                if (_memo is not null)
                {
                    _memo.PropertyChanged -= Memo_PropertyChanged;
                }

                _memo = newValue;
                AttachMemoHandlers(_memo);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMemo));
                OnPropertyChanged(nameof(HasDetailAmountMismatch));
            }
        }
        
        // UI에서 펼침/접기 상태
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        // 상세 내역이 있는지 여부
        public bool HasMemo => Memo.Details.Count > 0;
        public bool HasDetailAmountMismatch => HasMemo && Memo.TotalDetailAmount != Amount;
        // 거래 정보를 직접 노출
        public DateTime TransactionTime
        {
            get => _transaction.TransactionTime;
            set
            {
                if (_transaction.TransactionTime != value)
                {
                    _transaction.TransactionTime = value;
                }
            }
        }

        public DateTime? TransactionDate
        {
            get => _transaction.TransactionTime.Date;
            set
            {
                if (value is not DateTime newValue)
                {
                    OnPropertyChanged();
                    return;
                }

                var normalized = newValue.Date;
                if (normalized == _transaction.TransactionTime.Date)
                {
                    return;
                }

                TransactionTime = normalized.Add(_transaction.TransactionTime.TimeOfDay);
            }
        }

        public string TransactionTimeText
        {
            get => _transaction.TransactionTime.ToString("HH:mm");
            set
            {
                var current = _transaction.TransactionTime;
                if (string.IsNullOrWhiteSpace(value))
                {
                    OnPropertyChanged();
                    return;
                }

                var trimmed = value.Trim();
                if (TryParseTime(trimmed, out var timeOfDay))
                {
                    var newValue = current.Date.Add(timeOfDay);
                    if (newValue != current)
                    {
                        TransactionTime = newValue;
                        return;
                    }
                }

                // 입력이 유효하지 않으면 UI를 현재 값으로 되돌린다.
                OnPropertyChanged();
            }
        }
        public string TransactionType
        {
            get => _transaction.TransactionType;
            set => _transaction.TransactionType = value ?? string.Empty;
        }
        public decimal Amount
        {
            get => _transaction.Amount;
            set => _transaction.Amount = value;
        }
        public decimal ExpenseAmount
        {
            get => _transaction.IsIncome ? 0m : _transaction.Amount;
            set
            {
                var normalized = Math.Abs(value);
                if (normalized > 0)
                {
                    _transaction.TransactionType = "출금";
                    _transaction.Amount = normalized;
                }
                else if (!_transaction.IsIncome)
                {
                    _transaction.Amount = 0m;
                    _transaction.TransactionType = string.Empty;
                }
            }
        }

        public decimal IncomeAmount
        {
            get => _transaction.IsIncome ? _transaction.Amount : 0m;
            set
            {
                var normalized = Math.Abs(value);
                if (normalized > 0)
                {
                    _transaction.TransactionType = "입금";
                    _transaction.Amount = normalized;
                }
                else if (_transaction.IsIncome)
                {
                    _transaction.Amount = 0m;
                    _transaction.TransactionType = string.Empty;
                }
            }
        }

        public string Category
        {
            get => _transaction.Category;
            set => _transaction.Category = value ?? string.Empty;
        }

        public string Description
        {
            get => _transaction.Description;
            set => _transaction.Description = value ?? string.Empty;
        }

        public bool IsExcludedFromTotal
        {
            get => _transaction.IsExcludedFromTotal;
            set => _transaction.IsExcludedFromTotal = value;
        }

        public TransactionWithMemo(TransactionRecord transaction)
        {
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            PropertyChangedEventManager.AddHandler(_transaction, Transaction_PropertyChanged, string.Empty);
        }
        private void AttachMemoHandlers(TransactionMemo memo)
        {
            if (memo is null)
            {
                return;
            }

            memo.PropertyChanged -= Memo_PropertyChanged;
            memo.PropertyChanged += Memo_PropertyChanged;
        }
        private void Transaction_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(TransactionRecord.Amount):
                    OnPropertyChanged(nameof(Amount));
                    OnPropertyChanged(nameof(ExpenseAmount));
                    OnPropertyChanged(nameof(IncomeAmount));
                    OnPropertyChanged(nameof(HasDetailAmountMismatch));
                    break;
                case nameof(TransactionRecord.TransactionType):
                    OnPropertyChanged(nameof(TransactionType));
                    OnPropertyChanged(nameof(ExpenseAmount));
                    OnPropertyChanged(nameof(IncomeAmount));
                    break;
                case nameof(TransactionRecord.TransactionTime):
                    OnPropertyChanged(nameof(TransactionTime));
                    OnPropertyChanged(nameof(TransactionDate));
                    OnPropertyChanged(nameof(TransactionTimeText));
                    break;
                case nameof(TransactionRecord.Category):
                    OnPropertyChanged(nameof(Category));
                    break;
                case nameof(TransactionRecord.Description):
                    OnPropertyChanged(nameof(Description));
                    break;
                case nameof(TransactionRecord.IsExcludedFromTotal):
                    OnPropertyChanged(nameof(IsExcludedFromTotal));
                    break;
            }
        }
        private void Memo_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) ||
                e.PropertyName == nameof(TransactionMemo.Details) ||
                e.PropertyName == nameof(TransactionMemo.TotalDetailAmount))
            {
                OnPropertyChanged(nameof(HasMemo));
                OnPropertyChanged(nameof(HasDetailAmountMismatch));
            }
        }
        private static bool TryParseTime(string input, out TimeSpan result)
        {
            var formats = new[]
            {
                "hh\\:mm",
                "h\\:mm",
                "HH\\:mm",
                "H\\:mm",
                "hhmm",
                "hmm",
                "HHmm"
            };

            if (TimeSpan.TryParseExact(input, formats, CultureInfo.InvariantCulture, out result))
            {
                return true;
            }

            if (DateTime.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var dateTime))
            {
                result = dateTime.TimeOfDay;
                return true;
            }

            result = default;
            return false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}