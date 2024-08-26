using System;
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
        var state = App.rootHub.getDS4State(_device);

        if (_stick == Stick.Left)
        {
            var xAxisDrift = state.LX - 127;
            var yAxisDrift = state.LY - 127;
            if (xAxisDrift != 0)
            {
                Global.LeftStickDriftXAxis[_device] = Convert.ToSByte(xAxisDrift);
            }
            if (yAxisDrift != 0)
            {
                Global.LeftStickDriftYAxis[_device] = Convert.ToSByte(yAxisDrift);
            }
            MessageBox.Show($"Detected drift:\nX axis: {xAxisDrift}, Y axis: {yAxisDrift}",
                "DS4Windows", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        if (_stick == Stick.Right)
        {
            var xAxisDrift = state.RX - 127;
            var yAxisDrift = state.RY - 127;

            if (xAxisDrift != 0)
            {
                Global.RightStickDriftXAxis[_device] = Convert.ToSByte(xAxisDrift);
            }

            if (yAxisDrift != 0)
            {
                Global.RightStickDriftYAxis[_device] = Convert.ToSByte(yAxisDrift);
            }

            MessageBox.Show($"Detected drift:\nX axis: {xAxisDrift}, Y axis: {yAxisDrift}",
                "DS4Windows", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        Close();
    }
}

public enum Stick
{
    Left,
    Right
}