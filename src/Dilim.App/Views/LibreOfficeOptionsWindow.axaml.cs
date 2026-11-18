using Avalonia.Controls;
using Avalonia.Interactivity;
using Dilim.App.Services;

namespace Dilim.App.Views;

/// <summary>Modal LibreOffice switches of the Engines page (its view model is the DataContext).</summary>
public partial class LibreOfficeOptionsWindow : Window
{
    public LibreOfficeOptionsWindow()
    {
        InitializeComponent();
        Motion.Track(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
