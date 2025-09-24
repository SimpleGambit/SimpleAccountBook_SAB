using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SimpleAccountBook.Models
{
    /// 거래 내역의 상세 내역
    public class TransactionMemo : INotifyPropertyChanged
    {
        private ObservableCollection<MemoDetail> _details = new ObservableCollection<MemoDetail>();

        public TransactionMemo()
        {
            Details = new ObservableCollection<MemoDetail>();
        }

        public ObservableCollection<MemoDetail> Details
        {
            get => _details;
            set
            {
                if (!ReferenceEquals(_details, value))
                {
                    if (_details != null)
                    {
                        _details.CollectionChanged -= Details_CollectionChanged;
                        foreach (var detail in _details)
                        {
                            detail.PropertyChanged -= MemoDetail_PropertyChanged;
                        }
                    }

                    _details = value ?? new ObservableCollection<MemoDetail>();
                    _details.CollectionChanged += Details_CollectionChanged;
                    foreach (var detail in _details)
                    {
                        detail.PropertyChanged += MemoDetail_PropertyChanged;
                    }

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalDetailAmount));
                }
            }
        }

        public DateTime LastModified { get; set; }
        
        // 상세 항목들의 총 금액
        public decimal TotalDetailAmount 
        { 
            get 
            {
                decimal total = 0;
                foreach (var detail in Details)
                {
                    total += detail.Amount;
                }
                return total;
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        private void Details_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (MemoDetail oldItem in e.OldItems)
                {
                    oldItem.PropertyChanged -= MemoDetail_PropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (MemoDetail newItem in e.NewItems)
                {
                    newItem.PropertyChanged += MemoDetail_PropertyChanged;
                }
            }

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var detail in _details)
                {
                    detail.PropertyChanged -= MemoDetail_PropertyChanged;
                    detail.PropertyChanged += MemoDetail_PropertyChanged;
                }
            }

            OnPropertyChanged(nameof(Details));
            OnPropertyChanged(nameof(TotalDetailAmount));
        }

        private void MemoDetail_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MemoDetail.Amount) || string.IsNullOrEmpty(e.PropertyName))
            {
                OnPropertyChanged(nameof(TotalDetailAmount));
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// 상세 항목 (개별 지출 내역)
    public class MemoDetail : INotifyPropertyChanged
    {
        private string _itemName = string.Empty;
        private decimal _amount;
        private string _note = string.Empty;

        public string ItemName
        {
            get => _itemName;
            set
            {
                if (_itemName != value)
                {
                    _itemName = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal Amount
        {
            get => _amount;
            set
            {
                if (_amount != value)
                {
                    _amount = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Note
        {
            get => _note;
            set
            {
                if (_note != value)
                {
                    _note = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}