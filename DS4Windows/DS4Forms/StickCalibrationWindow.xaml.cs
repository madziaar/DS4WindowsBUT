using System;
using System.Windows;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WinWPF.DS4Forms;

public partial class StickCalibrationWindow : Window
{
    private Stick _stick;
    private int _device;
    private ProfileSettingsViewModel _profileSettingsVM;

    public StickCalibrationWindow(Stick stick, int device, ProfileSettingsViewModel profileSettingsVm)
    {
        _stick = stick;
        _device = device;
        _profileSettingsVM = profileSettingsVm;
        InitializeComponent();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var state = App.rootHub.getDS4State(_device);

        const int neutralState = 128;
        if (_stick == Stick.Left)
        {
            var xAxisDrift = state.LX - neutralState;
            var yAxisDrift = state.LY - neutralState;
            if (xAxisDrift != 0)
            {
                _profileSettingsVM.LeftStickDriftXAxis = Convert.ToSByte(xAxisDrift);
            }
            if (yAxisDrift != 0)
            {
                _profileSettingsVM.LeftStickDriftYAxis = Convert.ToSByte(yAxisDrift);
            }
            MessageBox.Show($"Detected drift:\nX axis: {xAxisDrift}, Y axis: {yAxisDrift}",
                "DS4Windows", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        if (_stick == Stick.Right)
        {
            var xAxisDrift = state.RX - neutralState;
            var yAxisDrift = state.RY - neutralState;

            if (xAxisDrift != 0)
            {
                _profileSettingsVM.RightStickDriftXAxis = Convert.ToSByte(xAxisDrift);
            }

            if (yAxisDrift != 0)
            {
                _profileSettingsVM.RightStickDriftYAxis = Convert.ToSByte(yAxisDrift);
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