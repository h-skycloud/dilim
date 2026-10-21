using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Filee.Core.Localization;

namespace Filee.App.Views;

/// <summary>A persistent-in-session, always-on-top file shelf. It holds references, never temporary copies.</summary>
public sealed class ShelfWindow : Window
{
    private readonly StackPanel _items = new() { Spacing = 8 };
    private readonly HashSet<string> _paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly ILocalizer _loc;

    public ShelfWindow(ILocalizer loc)
    {
        _loc = loc; Title = loc["tool.float"]; Topmost = true; Width = 320; Height = 400; MinWidth = 250; MinHeight = 180;
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        body.Children.Add(new TextBlock { Text = loc["tool.shelf.hint"], TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        body.Children.Add(_items); Content = new ScrollViewer { Content = body };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
        Opened += (_, _) =>
        {
            var screen = Screens.Primary;
            if (screen is not null) Position = new PixelPoint(screen.WorkingArea.Right - (int)(Width * screen.Scaling) - 20, screen.WorkingArea.Y + 80);
        };
    }

    public void Add(IEnumerable<string> files)
    {
        foreach (var file in files.Where(_paths.Add))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var button = new Button { Content = Path.GetFileName(file), HorizontalAlignment = HorizontalAlignment.Stretch };
            ToolTip.SetTip(button, file);
            button.Click += (_, _) =>
            {
                if (File.Exists(file) || Directory.Exists(file)) Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            };
            // Start native file drag after a small movement while holding the left mouse button.
            Point? start = null;
            PointerPressedEventArgs? pressed = null;
            button.PointerPressed += (_, e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) { start = e.GetPosition(button); pressed = e; } };
            button.PointerReleased += (_, _) => start = null;
            button.PointerMoved += async (_, e) =>
            {
                if (start is not { } at || pressed is null || !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed || Math.Abs(e.GetPosition(button).X - at.X) + Math.Abs(e.GetPosition(button).Y - at.Y) < 6) return;
                start = null;
                var item = File.Exists(file) ? (IStorageItem?)await StorageProvider.TryGetFileFromPathAsync(file) : await StorageProvider.TryGetFolderFromPathAsync(file);
                if (item is null) return;
                var data = new DataTransfer(); data.Add(DataTransferItem.CreateFile(item));
                await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Copy);
            };
            var remove = new Button { Content = "×", Margin = new Thickness(6, 0, 0, 0) };
            ToolTip.SetTip(remove, _loc["tool.shelf.remove"]);
            remove.Click += (_, _) => { _paths.Remove(file); _items.Children.Remove(row); };
            Grid.SetColumn(remove, 1); row.Children.Add(button); row.Children.Add(remove); _items.Children.Add(row);
        }
    }
}
