using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Filee.Share;

/// <summary>A Windows share source. The user chooses the destination in the system share sheet.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var files = args.Where(File.Exists).Select(Path.GetFullPath).ToArray();
        if (files.Length == 0)
        {
            MessageBox.Show("Paylaşılacak dosya yok. Klasörleri önce ZIP yapın.", "Filee · Paylaş");
            return;
        }
        var form = new Form { Text = "Filee · Paylaş", Width = 420, Height = 220, StartPosition = FormStartPosition.CenterScreen };
        var label = new Label { Text = "Windows paylaşım penceresinde alıcıyı seçin.\n\nİşiniz bitince bu pencereyi kapatabilirsiniz.", Dock = DockStyle.Fill, Padding = new Padding(20) };
        form.Controls.Add(label);
        var show = new Button { Text = "Windows paylaşımını aç", Dock = DockStyle.Bottom, Height = 42 };
        form.Controls.Add(show);
        void ShowSharing()
        {
            try { DataTransferManagerInterop.ShowShareUIForWindow(form.Handle); }
            catch (Exception ex) { label.Text = ex.Message; }
        }
        show.Click += (_, _) => ShowSharing();
        using var openTimer = new System.Windows.Forms.Timer { Interval = 250 };
        openTimer.Tick += (_, _) => { openTimer.Stop(); ShowSharing(); };
        DataTransferManager? manager = null;
        form.Shown += (_, _) =>
        {
            try
            {
                manager = DataTransferManagerInterop.GetForWindow(form.Handle);
                manager.DataRequested += async (_, e) =>
                {
                    var deferral = e.Request.GetDeferral();
                    try
                    {
                        var items = new List<IStorageItem>();
                        foreach (var file in files) items.Add(await StorageFile.GetFileFromPathAsync(file));
                        e.Request.Data.Properties.Title = files.Length == 1 ? Path.GetFileName(files[0]) : $"Filee · {files.Length} dosya";
                        e.Request.Data.SetStorageItems(items);
                    }
                    catch (Exception ex) { e.Request.FailWithDisplayText(ex.Message); }
                    finally { deferral.Complete(); }
                };
                // Wait until the form's initial activation has finished; it must not steal focus back from the sheet.
                openTimer.Start();
            }
            catch (Exception ex) { label.Text = ex.Message; }
        };
        Application.Run(form);
        GC.KeepAlive(manager);
    }
}
