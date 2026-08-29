using System.Windows;
using System.Windows.Controls;
using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.Views;

public partial class MessageWindow : ThemedWindow
{
    private MessageWindow(Window? owner, string title, string message, bool question)
    {
        InitializeComponent();

        if (owner is not null && owner.IsLoaded) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Title = title;
        TitleText.Text = title;
        BodyText.Text = message;
        Icon = AppIcons.CreateWindowIcon(question ? AppIcons.Attention : AppIcons.Idle);

        if (question)
        {
            Buttons.Children.Add(Make("No", isCancel: true, primary: false));
            Buttons.Children.Add(Make("Yes", isCancel: false, primary: true));
        }
        else
        {
            Buttons.Children.Add(Make("OK", isCancel: true, primary: true));
        }
    }

    private Button Make(string text, bool isCancel, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 84,
            IsCancel = isCancel,
            IsDefault = primary,
            Margin = new Thickness(0, 0, isCancel && !primary ? 8 : 0, 0)
        };

        if (primary) button.SetResourceReference(StyleProperty, "Primary.Button");
        if (!isCancel) button.Click += (_, _) => DialogResult = true;

        return button;
    }

    public static void Show(Window? owner, string title, string message) =>
        new MessageWindow(owner, title, message, question: false).ShowDialog();

    public static bool Ask(Window? owner, string title, string question) =>
        new MessageWindow(owner, title, question, question: true).ShowDialog() == true;
}
