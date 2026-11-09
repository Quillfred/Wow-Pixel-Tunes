using PixelTunes.Core;

// Draws the addon's strip in the top-left of its client area (block size 4) on a busy
// background, and "presses" a button every 2 seconds: Next, Next, Play/Pause, Previous, ...
// Each press is logged so the companion's log can be compared against it.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var form = new Form { Text = "World of Warcraft", ClientSize = new Size(800, 600), StartPosition = FormStartPosition.Manual, Location = new Point(100, 100) };
        int seq = 0, cmd = 0, presses = 0;
        int[] script = [2, 4, 4, 5, 1, 3];
        var log = Path.Combine(AppContext.BaseDirectory, "fakewow.log");
        File.WriteAllText(log, "");
        form.Paint += (_, e) =>
        {
            var rng = new Random(seq);
            for (int y = 0; y < 60; y += 3)
                for (int x = 0; x < 300; x += 3)
                    using (var b = new SolidBrush(Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256))))
                        e.Graphics.FillRectangle(b, x, y, 3, 3);
            var v = Protocol.Encode(cmd, seq);
            for (int i = 0; i < v.Length; i++)
            {
                var (r, g, bl) = Protocol.ColorOf(v[i]);
                using var br = new SolidBrush(Color.FromArgb(r, g, bl));
                e.Graphics.FillRectangle(br, i * 4, 0, 4, 4);
            }
        };
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) =>
        {
            if (presses >= script.Length) { timer.Stop(); return; }
            cmd = script[presses++];
            seq = (seq + 1) % 64;
            File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss.fff} pressed {(MediaCommand)cmd} seq {seq}\n");
            form.Invalidate();
        };
        timer.Start();
        Application.Run(form);
    }
}
