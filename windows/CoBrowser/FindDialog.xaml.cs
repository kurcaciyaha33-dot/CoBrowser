using System.Windows;

namespace CoBrowser;

public partial class FindDialog : Window
{
    public string SearchText => QueryBox.Text.Trim();
    public FindDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => { QueryBox.Focus(); QueryBox.SelectAll(); };
    }
    private void Find_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
