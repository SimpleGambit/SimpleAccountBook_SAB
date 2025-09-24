using System;
using System.Windows;
using System.Windows.Controls;

namespace SimpleAccountBook;

public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow(string fileName, bool isRetry)
    {
        InitializeComponent();
        FileName = fileName;
        MessageTextBlock.Text = isRetry
            ? $"{fileName} 파일의 비밀번호가 올바르지 않습니다. 다시 입력해주세요."
            : $"{fileName} 파일은 암호화된 파일입니다. 비밀번호를 입력해주세요.";
    }

    public string? Password { get; private set; }

    public string FileName { get; }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Password = PasswordBox.Password;
        DialogResult = true;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PasswordBox.Focus();
    }
}