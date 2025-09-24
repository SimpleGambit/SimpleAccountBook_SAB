using System.Windows;
using SimpleAccountBook.Services;

namespace SimpleAccountBook;

public partial class SettingsWindow : Window
{
    private readonly ThemeService _themeService;

    public SettingsWindow()
    {
        InitializeComponent();
        
        // 테마 서비스 초기화
        _themeService = new ThemeService();
        
        // 현재 적용된 테마에 맞게 라디오 버튼 선택
        var currentTheme = _themeService.GetCurrentTheme();
        if (currentTheme == ThemeService.ThemeType.Dark)
        {
            DarkThemeRadio.IsChecked = true;
        }
        else
        {
            LightThemeRadio.IsChecked = true;
        }
    }

    /// <summary>
    /// 테마 라디오 버튼 체크 이벤트 - 테마를 즉시 적용합니다
    /// </summary>
    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_themeService == null) return;

        // 라이트 테마 선택 시
        if (LightThemeRadio.IsChecked == true)
        {
            _themeService.ApplyTheme(ThemeService.ThemeType.Light);
        }
        // 다크 테마 선택 시
        else if (DarkThemeRadio.IsChecked == true)
        {
            _themeService.ApplyTheme(ThemeService.ThemeType.Dark);
        }

        // MainWindow의 달력을 즉시 업데이트
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.UpdateCalendarTheme();
        }
    }

    /// <summary>
    /// 닫기 버튼 클릭 - 창을 닫습니다
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
