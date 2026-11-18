using Avalonia.Controls;
using Dilim.App.Services;
using Dilim.App.ViewModels;

namespace Dilim.App.Views;

public partial class EngineSetupWindow : Window
{
    public EngineSetupWindow()
    {
        InitializeComponent();
        Motion.Track(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is EngineSetupViewModel vm)
                vm.CloseRequested += (_, _) => Close();
        };
    }
}
