using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Speaky.Models;
using Color = System.Windows.Media.Color;

namespace Speaky;

/// <summary>
/// Kompakte GUI. Reagiert auf den geteilten <see cref="RecordingState"/> –
/// alle UI-Updates laufen über den PropertyChanged-Handler, egal ob die Änderung
/// vom Button-Click oder vom globalen Hotkey kam.
/// </summary>
public partial class MainWindow : Window
{
    private RecordingState? _state;
    private Action? _onToggle;

    public MainWindow()
    {
        InitializeComponent();

        // Startposition: rechts unten, so dass das Fenster Chat/Eingabefelder nicht verdeckt.
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Bottom - Height - 24;
    }

    /// <summary>
    /// Wird vom App-Composition-Root aufgerufen, nachdem alle Services gebaut sind.
    /// </summary>
    public void Wire(RecordingState state, Action onToggle)
    {
        _state = state;
        _onToggle = onToggle;

        _state.PropertyChanged += OnStateChanged;
        ModeCombo.SelectedIndex = (int)_state.Mode;
        EmojiSlider.Value = _state.EmojiCount;
        ApplyState();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnStateChanged(sender, e));
            return;
        }
        ApplyState();
    }

    private void ApplyState()
    {
        if (_state is null) return;

        StatusLabel.Text = _state.StatusText;

        switch (_state.CurrentPhase)
        {
            case RecordingState.Phase.Idle:
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                ToggleButton.Content = "▶  Start (Ctrl+Alt+S)";
                ToggleButton.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
                ToggleButton.IsEnabled = true;
                break;
            case RecordingState.Phase.Recording:
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
                ToggleButton.Content = "■  Stop (Ctrl+Alt+S)";
                ToggleButton.Background = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                ToggleButton.IsEnabled = true;
                break;
            case RecordingState.Phase.Transcribing:
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFB, 0xC0, 0x2D));
                ToggleButton.Content = "…  Transkribiert";
                ToggleButton.Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                ToggleButton.IsEnabled = false;
                break;
        }

        // VU-Meter Breite anhand Level
        var maxWidth = Math.Max(0, ((Border)LevelBar.Parent).ActualWidth);
        LevelBar.Width = maxWidth * _state.InputLevel;

        EmojiPanel.Visibility = _state.Mode == RecordingMode.Emoji
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _onToggle?.Invoke();
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_state is null) return;
        if (ModeCombo.SelectedIndex < 0) return;
        _state.Mode = (RecordingMode)ModeCombo.SelectedIndex;
        ApplyState();
    }

    private void EmojiSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_state is null) return;
        _state.EmojiCount = (int)e.NewValue;
        EmojiCountLabel.Text = _state.EmojiCount.ToString();
    }

    /// <summary>
    /// Fenster schließen minimiert in den Tray, statt die App zu beenden.
    /// Beendet wird nur über den Tray ("Beenden").
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            base.OnClosing(e);
        }
    }
}
