using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SimpleAccountBook.Models;

namespace SimpleAccountBook.Controls.Calendar
{
    public partial class CustomCalendarControl : UserControl
    {
        // 현재 표시중인 월
        private DateTime _currentMonth;
        private bool _isUpdatingDisplayDate;
        private const int YearRange = 10;

        // 선택된 날짜
        private DateTime? _selectedDate;

        // 일별 데이터 딕셔너리
        private IReadOnlyDictionary<DateOnly, DailySummary> _dailySummaries;

        // 날짜 칸 딕셔너리 (날짜별로 Border 참조)
        private Dictionary<DateTime, Border> _dayCells;
        private HashSet<DateOnly> _detailMismatchDates;

        // Dependency Properties
        public static readonly DependencyProperty SelectedDateProperty =
            DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(CustomCalendarControl),
                new PropertyMetadata(null, OnSelectedDateChanged));

        public static readonly DependencyProperty DisplayDateProperty =
            DependencyProperty.Register(nameof(DisplayDate), typeof(DateTime), typeof(CustomCalendarControl),
                new PropertyMetadata(DateTime.Today, OnDisplayDateChanged));

        // Properties
        public DateTime? SelectedDate
        {
            get => (DateTime?)GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        public DateTime DisplayDate
        {
            get => (DateTime)GetValue(DisplayDateProperty);
            set => SetValue(DisplayDateProperty, value);
        }

        // Events
        public event EventHandler<DateTime?>? SelectedDateChanged;
        public event EventHandler<DateTime>? DisplayDateChanged;

        public CustomCalendarControl()
        {
            InitializeComponent();
            var today = DateTime.Today;
            _currentMonth = new DateTime(today.Year, today.Month, 1);
            _dayCells = new Dictionary<DateTime, Border>();
            _dailySummaries = new Dictionary<DateOnly, DailySummary>();
            _detailMismatchDates = new HashSet<DateOnly>();
            SetCurrentValue(DisplayDateProperty, _currentMonth);
            InitializeMonthYearSelectors();
            UpdateCalendar();
        }

        // 테마에서 Brush 리소스 가져오기
        private Brush GetResourceBrush(string key, Brush fallback)
        {
            return TryFindResource(key) as Brush ?? fallback;
        }

        // 달력 업데이트
        public void UpdateCalendar()
        {
            CalendarGrid.Children.Clear();
            _dayCells.Clear();

            // 현재 월 텍스트 업데이트
            CurrentMonthText.Text = $"{_currentMonth:yyyy년 MM월}";
            SyncMonthYearSelectors();

            // 해당 월의 첫날과 마지막날
            var firstDay = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            // 첫 주의 시작 위치 (일요일=0, 월요일=1, ...)
            int startOffset = (int)firstDay.DayOfWeek;

            // 전월 날짜 표시
            var prevMonthLastDay = firstDay.AddDays(-1);
            for (int i = startOffset - 1; i >= 0; i--)
            {
                var date = prevMonthLastDay.AddDays(-i);
                CreateDayCell(date, (startOffset - 1 - i) / 7, (startOffset - 1 - i) % 7, true);
            }

            // 현재 월 날짜 표시
            int currentPosition = startOffset;
            for (int day = 1; day <= lastDay.Day; day++)
            {
                var date = new DateTime(_currentMonth.Year, _currentMonth.Month, day);
                int row = currentPosition / 7;
                int col = currentPosition % 7;
                CreateDayCell(date, row, col, false);
                currentPosition++;
            }

            // 다음 월 날짜 표시 (6주를 채우기 위해)
            int nextMonthDay = 1;
            while (currentPosition < 42) // 6주 x 7일
            {
                var date = lastDay.AddDays(nextMonthDay);
                int row = currentPosition / 7;
                int col = currentPosition % 7;
                CreateDayCell(date, row, col, true);
                currentPosition++;
                nextMonthDay++;
            }

            // 주간 합계 칸 생성
            for (int week = 0; week < 6; week++)
            {
                CreateWeekSummaryCell(week);
            }

            // 데이터 업데이트
            UpdateDailySummaries();
        }

        // 날짜 칸 생성
        private void CreateDayCell(DateTime date, int row, int column, bool isInactive)
        {
            var border = new Border
            {
                Background = GetResourceBrush("CalendarCellBackgroundBrush", Brushes.White)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 날짜 숫자
            var dayNumber = new TextBlock
            {
                Text = date.Day.ToString(),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = GetResourceBrush("CalendarTextBrush", Brushes.Black)
            };

            // 일요일은 빨간색, 토요일은 파란색
            if (column == 0) dayNumber.Foreground = GetResourceBrush("SundayTextBrush", Brushes.Red);
            if (column == 6) dayNumber.Foreground = GetResourceBrush("SaturdayTextBrush", Brushes.Blue);
            if (date.Date == DateTime.Today) dayNumber.Foreground = Brushes.OrangeRed;

            Grid.SetRow(dayNumber, 0);
            grid.Children.Add(dayNumber);

            // 금액 표시 영역
            var amountPanel = new StackPanel
            {
                Name = $"AmountPanel_{date:yyyyMMdd}",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(2)
            };
            Grid.SetRow(amountPanel, 1);
            grid.Children.Add(amountPanel);

            border.Child = grid;
            
            ApplyDayCellAppearance(date, border, isInactive);

            // 클릭 이벤트 (비활성 날짜가 아닌 경우만)
            if (!isInactive)
            {
                border.MouseLeftButtonUp += (s, e) => OnDayClick(date);
                border.Cursor = Cursors.Hand;
                _dayCells[date] = border;
            }

            Grid.SetRow(border, row);
            Grid.SetColumn(border, column);
            CalendarGrid.Children.Add(border);
        }
        
        private void ApplyDayCellAppearance(DateTime date, Border border, bool isInactive)
        {
            border.BorderBrush = GetResourceBrush("CalendarCellBorderBrush", new SolidColorBrush(Color.FromRgb(224, 224, 224)));
            border.BorderThickness = new Thickness(0.5);
            border.Opacity = isInactive ? 0.5 : 1d;

            var hasMismatch = _detailMismatchDates.Contains(DateOnly.FromDateTime(date));
            border.Background = hasMismatch
                ? GetResourceBrush("CalendarMismatchBackgroundBrush", new SolidColorBrush(Color.FromRgb(255, 235, 235)))
                : GetResourceBrush("CalendarCellBackgroundBrush", Brushes.White);

            if (date.Date == DateTime.Today)
            {
                border.BorderBrush = Brushes.Orange;
                border.BorderThickness = new Thickness(2);
            }

            if (_selectedDate.HasValue && date.Date == _selectedDate.Value.Date)
            {
               border.BorderBrush = GetResourceBrush(
                    "CalendarSelectedBorderBrush",
                    new SolidColorBrush(Color.FromRgb(176, 176, 176)));
                border.BorderThickness = new Thickness(2);
                border.Background = GetResourceBrush("InteractionHighlightBrush", Brushes.LightGray);
            }
        }

        // 주간 합계 칸 생성
        private void CreateWeekSummaryCell(int weekIndex)
        {
            var border = new Border
            {
                BorderBrush = GetResourceBrush("CalendarHeaderBorderBrush", new SolidColorBrush(Color.FromRgb(208, 208, 208))),
                BorderThickness = new Thickness(0.5),
                Background = GetResourceBrush("CalendarWeeklySumBackgroundBrush", new SolidColorBrush(Color.FromRgb(248, 248, 248)))
            };

            var textBlock = new TextBlock
            {
                Name = $"WeekSum_{weekIndex}",
                Text = "",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = GetResourceBrush("CalendarTextBrush", Brushes.Black)
            };

            border.Child = textBlock;

            Grid.SetRow(border, weekIndex);
            Grid.SetColumn(border, 7); // 8번째 열(인덱스 7)
            CalendarGrid.Children.Add(border);
        }

        // 날짜 클릭 이벤트
        private void OnDayClick(DateTime date)
        {
            SelectedDate = date;
        }

        // 이전 달 버튼 클릭
        private void PrevMonthButton_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth = _currentMonth.AddMonths(-1);
            DisplayDate = _currentMonth;
            UpdateCalendar();
        }

        // 다음 달 버튼 클릭
        private void NextMonthButton_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth = _currentMonth.AddMonths(1);
            DisplayDate = _currentMonth;
            UpdateCalendar();
        }
        
        private void CurrentMonthText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            SyncMonthYearSelectors();
            MonthYearPopup.IsOpen = true;
            YearComboBox?.Focus();
        }

        private void ApplyMonthYearSelection_Click(object sender, RoutedEventArgs e)
        {
            if (YearComboBox?.SelectedValue is not int selectedYear)
            {
                return;
            }

            if (MonthComboBox?.SelectedValue is not int month || month < 1 || month > 12)
            {
                return;
            }

            _currentMonth = new DateTime(selectedYear, month, 1);
            DisplayDate = _currentMonth;
            UpdateCalendar();
            SyncMonthYearSelectors();
            MonthYearPopup.IsOpen = false;
        }

        private void CancelMonthYearSelection_Click(object sender, RoutedEventArgs e)
        {
            MonthYearPopup.IsOpen = false;
        }

        private void InitializeMonthYearSelectors()
        {
            if (MonthComboBox != null && MonthComboBox.Items.Count == 0)
            {
                for (int month = 1; month <= 12; month++)
                {
                    MonthComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = $"{month:00}월",
                        Tag = month
                    });
                }
            }

            SyncMonthYearSelectors();
        }

        private void SyncMonthYearSelectors()
        {
            UpdateYearComboBoxItems(_currentMonth.Year);

            if (YearComboBox != null)
            {
                YearComboBox.SelectedValue = _currentMonth.Year;
                YearComboBox.Text = _currentMonth.Year.ToString();
            }

            if (MonthComboBox != null)
            {
                MonthComboBox.SelectedValue = _currentMonth.Month;
                MonthComboBox.Text = $"{_currentMonth.Month:00}월";
            }
        }

        private void UpdateYearComboBoxItems(int centerYear)
        {
            if (YearComboBox == null)
            {
                return;
            }

            int startYear = Math.Max(1, centerYear - YearRange);
            int endYear = centerYear + YearRange;

            var years = new List<int>();
            for (int year = startYear; year <= endYear; year++)
            {
                years.Add(year);
            }

            YearComboBox.ItemsSource = years;
        }
        
        // 일별 데이터 업데이트
        public void SetDailySummaries(IReadOnlyDictionary<DateOnly, DailySummary> summaries)
        {
            _dailySummaries = summaries ?? new Dictionary<DateOnly, DailySummary>();
            UpdateDailySummaries();
        }
        
        public void SetDetailMismatchDates(IReadOnlyCollection<DateOnly>? dates)
        {
            _detailMismatchDates = dates is null
                ? new HashSet<DateOnly>()
                : new HashSet<DateOnly>(dates);

            UpdateDailySummaries();
        }

        // 화면에 데이터 표시
        private void UpdateDailySummaries()
        {
            // 주간 합계 초기화
            var weeklyTotals = new decimal[6];

            foreach (var kvp in _dayCells)
            {
                var date = kvp.Key;
                var border = kvp.Value;

                ApplyDayCellAppearance(date, border, false);

                // AmountPanel 찾기
                if (border.Child is Grid grid && grid.Children.Count > 1)
                {
                    var amountPanel = grid.Children[1] as StackPanel;
                    if (amountPanel != null)
                    {
                        amountPanel.Children.Clear();

                        // 해당 날짜의 데이터 확인
                        var dateOnly = DateOnly.FromDateTime(date);
                        if (_dailySummaries.TryGetValue(dateOnly, out var summary))
                        {
                            // 주차 계산
                            var firstDay = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
                            int startOffset = (int)firstDay.DayOfWeek;
                            int dayOfMonth = date.Day;
                            int weekIndex = (startOffset + dayOfMonth - 1) / 7;

                            if (weekIndex >= 0 && weekIndex < 6)
                            {
                                weeklyTotals[weekIndex] += (summary.IncomeAmount - summary.ExpenseAmount);
                            }

                            // 입금 표시
                            if (summary.IncomeAmount > 0)
                            {
                                var incomeText = new TextBlock
                                {
                                    Text = $"{summary.IncomeAmount:#,0}",
                                    Foreground = GetResourceBrush("IncomeTextBrush", Brushes.Green),
                                    FontSize = 14,
                                    HorizontalAlignment = HorizontalAlignment.Center
                                };
                                amountPanel.Children.Add(incomeText);
                            }

                            // 출금 표시
                            if (summary.ExpenseAmount > 0)
                            {
                                var expenseText = new TextBlock
                                {
                                    Text = $"{summary.ExpenseAmount:#,0}",
                                    Foreground = GetResourceBrush("ExpenseTextBrush", Brushes.Red),
                                    FontSize = 14,
                                    HorizontalAlignment = HorizontalAlignment.Center
                                };
                                amountPanel.Children.Add(expenseText);
                            }
                        }
                    }
                }
            }

            // 주간 합계 업데이트
            for (int week = 0; week < 6; week++)
            {
                var weekSumText = CalendarGrid.Children
                    .OfType<Border>()
                    .Where(b => Grid.GetRow(b) == week && Grid.GetColumn(b) == 7)
                    .Select(b => b.Child as TextBlock)
                    .FirstOrDefault(t => t != null && t.Name == $"WeekSum_{week}");

                if (weekSumText != null)
                {
                    if (weeklyTotals[week] != 0)
                    {
                        weekSumText.Text = weeklyTotals[week] > 0
                            ? $"+{weeklyTotals[week]:#,0}"
                            : $"{weeklyTotals[week]:#,0}";
                        weekSumText.Foreground = weeklyTotals[week] >= 0
                            ? GetResourceBrush("IncomeTextBrush", Brushes.Green)
                            : GetResourceBrush("ExpenseTextBrush", Brushes.Red);
                    }
                    else
                    {
                        weekSumText.Text = "-";
                        weekSumText.Foreground = GetResourceBrush("GrayTextBrush", Brushes.Gray);
                    }
                }
            }
        }

        // Property Changed Handlers
        private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not CustomCalendarControl control)
            {
                return;
            }

            control._selectedDate = e.NewValue as DateTime?;
            control.SelectedDateChanged?.Invoke(control, control._selectedDate);
            control.UpdateCalendar();
        }

        private static void OnDisplayDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not CustomCalendarControl control || e.NewValue is not DateTime newDate)
            {
                return;
            }
            if (control._isUpdatingDisplayDate)
            {
                return;
            }

            var normalized = new DateTime(newDate.Year, newDate.Month, 1);

            if (newDate != normalized)
            {
                control._isUpdatingDisplayDate = true;
                control.SetCurrentValue(DisplayDateProperty, normalized);
                control._isUpdatingDisplayDate = false;
                return;
            }

            control._currentMonth = normalized;
            control.DisplayDateChanged?.Invoke(control, normalized);
            control.UpdateCalendar();
        }
    }
}
