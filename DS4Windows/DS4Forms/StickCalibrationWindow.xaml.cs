using System.Windows;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

public partial class StickCalibrationWindow : Window
{
    private Stick _stick;
    private int _device;

    public StickCalibrationWindow(Stick stick, int device)
    {
        _stick = stick;
        _device = device;
        InitializeComponent();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        // TODO consider what happens if stick is recalibrated and we recalibrate it again
        var state = App.rootHub.getDS4State(_device);
        switch (_stick)
        {
            case Stick.Left:
                MessageBox.Show($"LX: {state.LX} LY: {state.LY}",
                    "DS4Windows", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case Stick.Right:
                MessageBox.Show($"RX: {state.RX} RY: {state.RY}",
                    "DS4Windows", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }

        Close();
    }
}

public enum Stick
{
    Left,
    Right
}