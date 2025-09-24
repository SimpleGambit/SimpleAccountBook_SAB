using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace SimpleAccountBook.Services
{
    /// <summary>
    /// 애플리케이션 테마를 관리하는 서비스
    /// 라이트/다크 모드 전환 및 사용자 설정 저장/로드
    /// </summary>
    public class ThemeService
    {
        private const string SettingsFileName = "theme_settings.json";
        private readonly string _settingsFilePath;

        public ThemeService()
        {
            // 설정 파일 경로를 애플리케이션 데이터 폴더에 저장
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimpleAccountBook"
            );
            Directory.CreateDirectory(appDataPath);
            _settingsFilePath = Path.Combine(appDataPath, SettingsFileName);
        }

        /// <summary>
        /// 현재 테마 타입 (Light 또는 Dark)
        /// </summary>
        public enum ThemeType
        {
            Light,
            Dark
        }

        /// <summary>
        /// 저장된 테마 설정을 불러옵니다
        /// </summary>
        public ThemeType LoadTheme()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<ThemeSettings>(json);
                    return settings?.Theme ?? ThemeType.Light;
                }
            }
            catch
            {
                // 오류 발생 시 기본값 반환
            }
            return ThemeType.Light;
        }

        /// <summary>
        /// 테마 설정을 파일에 저장합니다
        /// </summary>
        public void SaveTheme(ThemeType theme)
        {
            try
            {
                var settings = new ThemeSettings { Theme = theme };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch
            {
                // 저장 실패 시 무시
            }
        }

        /// <summary>
        /// 애플리케이션에 테마를 적용합니다
        /// </summary>
        public void ApplyTheme(ThemeType theme)
        {
            var app = Application.Current;
            if (app?.Resources == null) return;

            // 기존 테마 리소스 제거
            var existingTheme = app.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source?.OriginalString?.Contains("/Themes/") == true);
            
            if (existingTheme != null)
            {
                app.Resources.MergedDictionaries.Remove(existingTheme);
            }

            // 새 테마 적용
            var themeUri = theme == ThemeType.Dark
                ? new Uri("/Themes/DarkTheme.xaml", UriKind.Relative)
                : new Uri("/Themes/LightTheme.xaml", UriKind.Relative);

            var themeDict = new ResourceDictionary { Source = themeUri };
            app.Resources.MergedDictionaries.Add(themeDict);

            // 설정 저장
            SaveTheme(theme);
        }

        /// <summary>
        /// 현재 적용된 테마를 반환합니다
        /// </summary>
        public ThemeType GetCurrentTheme()
        {
            var app = Application.Current;
            if (app?.Resources == null) return ThemeType.Light;

            var currentTheme = app.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source?.OriginalString?.Contains("/Themes/") == true);

            if (currentTheme?.Source?.OriginalString?.Contains("DarkTheme") == true)
            {
                return ThemeType.Dark;
            }

            return ThemeType.Light;
        }

        /// <summary>
        /// 테마 설정을 저장하는 내부 클래스
        /// </summary>
        private class ThemeSettings
        {
            public ThemeType Theme { get; set; }
        }
    }
}
