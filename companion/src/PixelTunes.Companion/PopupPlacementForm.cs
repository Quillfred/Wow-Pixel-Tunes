namespace PixelTunes.Companion;

/// <summary>
/// "Move popup..." mode: shows a draggable preview of the Now Playing popup over WoW.
/// Drag to move, scroll or use the buttons to resize, Enter/Done to save, Esc/Cancel to discard.
/// The position is saved relative to the WoW window, so it survives resolution changes.
/// </summary>
internal sealed class PopupPlacementForm : Form
{
    public const double MinScale = 0.6, MaxScale = 2.5, ScaleStep = 0.1;

    private readonly Rectangle _area;
    private readonly AppSettings _settings;
    private readonly string _title, _artist;
    private readonly FlowLayoutPanel _toolbar;
    private double _scale;
    private Point? _dragOffset;

    /// <summary>Raised after the new position/size has been saved.</summary>
    public event Action? Saved;

    public PopupPlacementForm(Rectangle area, AppSettings settings, string title, string artist)
    {
        _area = area;
        _settings = settings;
        _title = title;
        _artist = artist;
        _scale = Math.Clamp(settings.ToastScale, MinScale, MaxScale);

        Text = "PixelTunes - Move popup";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(40, 40, 40);
        Font = new Font("Segoe UI", 9f);

        _toolbar = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Color.FromArgb(40, 40, 40),
            Padding = new Padding(4),
            WrapContents = false,
        };
        AddButton("Done", Save, bold: true);
        AddButton("Cancel", Close);
        AddButton("Reset", ResetToDefault);
        AddButton("Bigger", () => Rescale(_scale + ScaleStep));
        AddButton("Smaller", () => Rescale(_scale - ScaleStep));
        Controls.Add(_toolbar);

        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) _dragOffset = e.Location; };
        MouseMove += (_, e) =>
        {
            if (_dragOffset is { } off && e.Button == MouseButtons.Left)
                MoveTo(new Point(Cursor.Position.X - off.X, Cursor.Position.Y - off.Y));
        };
        MouseUp += (_, _) => _dragOffset = null;
        MouseWheel += (_, e) => Rescale(_scale + Math.Sign(e.Delta) * ScaleStep);
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) Save();
            else if (e.KeyCode == Keys.Escape) Close();
        };

        Load += (_, _) =>
        {
            var size = PreviewSize;
            LayoutForSize(size);
            var pos = ToastRenderer.Locate(_area, size, _settings, DeviceDpi);
            MoveTo(pos);
            Activate();
        };
    }

    private Size PreviewSize => ToastRenderer.SizeFor(DeviceDpi, _scale);
    private int ToolbarHeight => (int)(40 * DeviceDpi / 96.0);

    private void AddButton(string text, Action onClick, bool bold = false)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = bold ? Color.FromArgb(30, 150, 70) : Color.FromArgb(70, 70, 70),
            Margin = new Padding(3, 2, 3, 2),
            Font = new Font("Segoe UI", 9f, bold ? FontStyle.Bold : FontStyle.Regular),
            TabStop = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) => onClick();
        _toolbar.Controls.Add(b);
    }

    private void LayoutForSize(Size preview)
    {
        int toolbarMinWidth = _toolbar.PreferredSize.Width;
        ClientSize = new Size(Math.Max(preview.Width, toolbarMinWidth), preview.Height + ToolbarHeight);
        _toolbar.SetBounds(0, preview.Height, ClientSize.Width, ToolbarHeight);
        Invalidate();
    }

    /// <summary>Moves the form so the preview's top-left is at <paramref name="previewTopLeft"/>, kept inside the WoW area.</summary>
    private void MoveTo(Point previewTopLeft)
    {
        int x = Math.Clamp(previewTopLeft.X, _area.Left, Math.Max(_area.Left, _area.Right - Width));
        int y = Math.Clamp(previewTopLeft.Y, _area.Top, Math.Max(_area.Top, _area.Bottom - Height));
        Location = new Point(x, y);
    }

    private void Rescale(double scale)
    {
        scale = Math.Round(Math.Clamp(scale, MinScale, MaxScale), 2);
        if (Math.Abs(scale - _scale) < 0.001) return;
        var old = PreviewSize;
        var center = new Point(Left + old.Width / 2, Top + old.Height / 2);
        _scale = scale;
        var size = PreviewSize;
        LayoutForSize(size);
        MoveTo(new Point(center.X - size.Width / 2, center.Y - size.Height / 2)); // grow/shrink around the centre
    }

    private void ResetToDefault()
    {
        _scale = 1;
        var size = PreviewSize;
        LayoutForSize(size);
        var defaults = new AppSettings(); // default corner placement
        MoveTo(ToastRenderer.Locate(_area, size, defaults, DeviceDpi));
    }

    private void Save()
    {
        var size = PreviewSize;
        double cx = Left + size.Width / 2.0, cy = Top + size.Height / 2.0;
        _settings.ToastCustomPosition = true;
        _settings.ToastCenterX = Math.Clamp((cx - _area.Left) / _area.Width, 0, 1);
        _settings.ToastCenterY = Math.Clamp((cy - _area.Top) / _area.Height, 0, 1);
        _settings.ToastScale = _scale;
        _settings.Save();
        Log.Info($"Popup placement saved: center=({_settings.ToastCenterX:0.000},{_settings.ToastCenterY:0.000}) scale={_scale:0.00}");
        Saved?.Invoke();
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var size = PreviewSize;
        var rect = new Rectangle(0, 0, size.Width, size.Height);
        ToastRenderer.Paint(e.Graphics, rect, DeviceDpi, _scale,
            $"Drag me · scroll to resize · {Math.Round(_scale * 100)}%", _title, _artist, null);
        float s = DeviceDpi / 96f;
        using var pen = new Pen(ToastRenderer.Accent, Math.Max(1, 2 * s)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        e.Graphics.DrawRectangle(pen, rect.X + 1, rect.Y + 1, rect.Width - 3, rect.Height - 3);
    }
}
