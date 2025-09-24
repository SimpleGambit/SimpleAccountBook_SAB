using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using SimpleAccountBook.Services;

namespace SimpleAccountBook
{
    public partial class App : System.Windows.Application
    {
        private StreamWriter? _logWriter;
        private ThemeService? _themeService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 로그 초기화
            var logDir = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, $"SimpleAccountBook_{DateTime.Now:yyyy-MM-dd}.txt");

            _logWriter = new StreamWriter(logPath, append: true,
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            { AutoFlush = true };

            Trace.Listeners.Clear();
            Trace.Listeners.Add(new DefaultTraceListener());
            Trace.Listeners.Add(new TextWriterTraceListener(_logWriter, "file"));
            Trace.AutoFlush = true;
            Debug.AutoFlush = true;

            Debug.WriteLine($"==== App start {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");

            // 테마 서비스 초기화 및 저장된 테마 적용
            _themeService = new ThemeService();
            var savedTheme = _themeService.LoadTheme();
            _themeService.ApplyTheme(savedTheme);
            Debug.WriteLine($"Theme loaded: {savedTheme}");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Debug.WriteLine($"==== App exit  {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
            Debug.Flush();
            _logWriter?.Flush();
            _logWriter?.Dispose();
            base.OnExit(e);
        }
    }
}
