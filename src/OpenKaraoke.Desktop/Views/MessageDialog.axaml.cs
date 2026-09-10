using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenKaraoke.Desktop.Views;

/// <summary>
/// Korean replacement for the WPF <c>MessageBox</c>: shows an informational notice or a
/// yes/no confirmation and returns the user's choice.
/// </summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public MessageDialog(string title, string message, bool confirm)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        CancelButton.IsVisible = confirm;
    }

    /// <summary>Shows a notice with a single 확인 button.</summary>
    public static async Task InfoAsync(Window owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message, confirm: false);
        await dialog.ShowDialog<bool>(owner);
    }

    /// <summary>Shows a 확인/취소 confirmation; returns true when the user confirms.</summary>
    public static async Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message, confirm: true);
        return await dialog.ShowDialog<bool>(owner);
    }

    private void OnConfirmClicked(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}
