using System.Windows;
using System.Windows.Input;
using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.Views;

public partial class TextPromptWindow : ThemedWindow
{
    private TextPromptWindow(string title, string prompt, string? initial)
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        Input.Text = initial ?? string.Empty;
        Icon = AppIcons.CreateWindowIcon(AppIcons.Idle);

        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    public static string? Prompt(Window owner, string title, string prompt, string? initial)
    {
        var window = new TextPromptWindow(title, prompt, initial) { Owner = owner };
        return window.ShowDialog() == true ? window.Input.Text.Trim() : null;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
