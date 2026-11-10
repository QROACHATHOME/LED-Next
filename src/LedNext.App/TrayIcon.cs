using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace LedNext.App;

/// <summary>
/// LED-Next's icon in the notification area (system tray). Minimizing the window hides it here; double-click
/// the icon (or "Open LED-Next") to bring it back, "Exit" to quit. Uses Windows Forms' NotifyIcon, since WPF
/// has none.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;
    private readonly IntPtr _hIcon;

    public TrayIcon(Action open, Action exit)
    {
        (_image, _hIcon) = DrawIcon();
        var menu = new Forms.ContextMenuStrip();
        var openItem = menu.Items.Add("Open LED-Next", null, (_, _) => open());
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "LED-Next",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
    }

    /// <summary>Tooltip text (Windows shows at most 63 characters).</summary>
    public string Text
    {
        get => _icon.Text;
        set => _icon.Text = value.Length > 63 ? value[..63] : value;
    }

    public void Dispose()
    {
        _icon.Visible = false; // otherwise the icon lingers until the mouse passes over it
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
        DestroyIcon(_hIcon);
    }

    /// <summary>A small control panel: dark rounded square with four lit buttons.</summary>
    private static (Icon Icon, IntPtr Handle) DrawIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var panel = new GraphicsPath())
            {
                const int r = 8;
                panel.AddArc(1, 1, r, r, 180, 90);
                panel.AddArc(31 - r, 1, r, r, 270, 90);
                panel.AddArc(31 - r, 31 - r, r, r, 0, 90);
                panel.AddArc(1, 31 - r, r, r, 90, 90);
                panel.CloseFigure();
                using var fill = new SolidBrush(Color.FromArgb(32, 34, 40));
                g.FillPath(fill, panel);
            }
            var colors = new[] { Color.FromArgb(255, 60, 60), Color.FromArgb(60, 220, 90), Color.FromArgb(70, 130, 255), Color.FromArgb(255, 210, 40) };
            for (int i = 0; i < 4; i++)
            {
                using var b = new SolidBrush(colors[i]);
                g.FillEllipse(b, 4 + (i % 2) * 13, 4 + (i / 2) * 13, 11, 11);
            }
        }
        var h = bmp.GetHicon();
        return (Icon.FromHandle(h), h);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
