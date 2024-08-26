using System.Windows;

namespace DS4WinWPF.DS4Forms;

public partial class StickCalibrationWindow : Window
{
    public StickCalibrationWindow()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty StickProperty =
        DependencyProperty.Register(nameof(Stick), typeof(Stick), typeof(StickCalibrationWindow));

    public Stick Stick
    {
        get => (Stick)GetValue(StickProperty);
        set => SetValue(StickProperty, value);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {

        Close();
    }
}

public enum Stick
{
    Left,
    Right
}