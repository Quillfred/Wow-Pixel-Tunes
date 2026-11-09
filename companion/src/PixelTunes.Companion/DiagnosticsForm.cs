using PixelTunes.Core;

namespace PixelTunes.Companion;

/// <summary>Live view of what the companion sees, plus buttons to test each part on its own.</summary>
internal sealed class DiagnosticsForm : Form
{
    private readonly TextBox _status;
    private readonly PictureBox _preview;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 200 };
    private readonly Func<string> _getStatus;
    private readonly Func<Bitmap> _getCapture;

    public DiagnosticsForm(Func<string> getStatus, Func<Bitmap> getCapture,
        Func<MediaCommand, Task> testCommand, Func<string> saveSnapshot)
    {
        _getStatus = getStatus;
        _getCapture = getCapture;

        Text = $"PixelTunes {Program.Version} - Diagnostics";
        Size = new Size(900, 560);
        MinimumSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        Icon = TrayIcon.Create();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _status = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5f), BackColor = SystemColors.Window,
        };
        layout.Controls.Add(_status);

        layout.Controls.Add(new Label
        {
            AutoSize = true, Padding = new Padding(0, 8, 0, 2),
            Text = "Top-left corner of the WoW window, enlarged. You should see 7 colored squares starting with magenta and cyan:",
        });

        _preview = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Normal };
        _preview.Paint += PaintPreview;
        layout.Controls.Add(_preview);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        Button Add(string text, Action onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            b.Click += (_, _) => onClick();
            buttons.Controls.Add(b);
            return b;
        }
        Add("Test Play/Pause", () => _ = testCommand(MediaCommand.PlayPause));
        Add("Test Previous", () => _ = testCommand(MediaCommand.Previous));
        Add("Test Next", () => _ = testCommand(MediaCommand.Next));
        Add("Test Vol -", () => _ = testCommand(MediaCommand.VolumeDown));
        Add("Test Vol +", () => _ = testCommand(MediaCommand.VolumeUp));
        Add("Save capture snapshot", () =>
        {
            try
            {
                var path = saveSnapshot();
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Snapshot failed");
            }
        });
        Add("Copy status", () => Clipboard.SetText(_status.Text));
        Add("Open log folder", () => System.Diagnostics.Process.Start("explorer.exe", $"\"{Log.Folder}\""));
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        _refresh.Tick += (_, _) => RefreshView();
        Shown += (_, _) => { RefreshView(); _refresh.Start(); };
        FormClosed += (_, _) => _refresh.Stop();
    }

    private void RefreshView()
    {
        var text = _getStatus().Replace("\n", Environment.NewLine);
        if (_status.Text != text) _status.Text = text;
        _preview.Invalidate();
    }

    private void PaintPreview(object? sender, PaintEventArgs e)
    {
        var bmp = _getCapture();
        int zoom = Math.Max(1, Math.Min(_preview.Width / bmp.Width, _preview.Height / bmp.Height));
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        e.Graphics.DrawImage(bmp, new Rectangle(0, 0, bmp.Width * zoom, bmp.Height * zoom));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _refresh.Dispose();
        base.Dispose(disposing);
    }
}
