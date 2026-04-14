using System.Drawing;
using System.Windows;
using Speaky.Models;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;

namespace Speaky.Services;

/// <summary>
/// System-Tray-Icon. Benutzt bewusst <see cref="Forms.NotifyIcon"/> aus WinForms,
/// damit wir keine zusätzliche NuGet-Dependency brauchen (UseWindowsForms=true ist
/// im csproj gesetzt).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notify;
    private readonly Icon _idleIcon;
    private readonly Icon _recordingIcon;
    private readonly RecordingState _state;

    public event Action? ShowWindowRequested;
    public event Action? ToggleRecordingRequested;
    public event Action? ExitRequested;

    public TrayIconService(RecordingState state)
    {
        _state = state;
        _idleIcon = CreateDotIcon(Color.DimGray);
        _recordingIcon = CreateDotIcon(Color.Crimson);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Speaky öffnen", null, (_, _) => ShowWindowRequested?.Invoke());
        menu.Items.Add("Aufnahme Start/Stop (Ctrl+Alt+S)", null, (_, _) => ToggleRecordingRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitRequested?.Invoke());

        _notify = new Forms.NotifyIcon
        {
            Icon = _idleIcon,
            Visible = true,
            Text = "Speaky – Bereit",
            ContextMenuStrip = menu,
        };
        _notify.DoubleClick += (_, _) => ShowWindowRequested?.Invoke();

        _state.PropertyChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Cross-thread safe: NotifyIcon ist an den UI-Thread gebunden.
        if (Application.Current?.Dispatcher is { } disp && !disp.CheckAccess())
        {
            disp.Invoke(() => OnStateChanged(sender, e));
            return;
        }

        _notify.Icon = _state.IsRecording ? _recordingIcon : _idleIcon;
        _notify.Text = _state.CurrentPhase switch
        {
            RecordingState.Phase.Recording => "Speaky – Aufnahme läuft",
            RecordingState.Phase.Transcribing => "Speaky – Transkribiert…",
            _ => "Speaky – Bereit",
        };
    }

    /// <summary>Baut dynamisch ein simples farbiges Punkt-Icon, damit keine .ico-Datei nötig ist.</summary>
    private static Icon CreateDotIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 4, 4, 24, 24);
            using var pen = new Pen(Color.White, 2);
            g.DrawEllipse(pen, 4, 4, 24, 24);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _state.PropertyChanged -= OnStateChanged;
        _notify.Visible = false;
        _notify.Dispose();
        _idleIcon.Dispose();
        _recordingIcon.Dispose();
    }
}
