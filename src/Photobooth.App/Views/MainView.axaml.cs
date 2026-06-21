using Avalonia.Controls;

namespace Photobooth.App.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        Focusable = true; // so keyboard triggers work in single-view (Pi) mode
    }
}
