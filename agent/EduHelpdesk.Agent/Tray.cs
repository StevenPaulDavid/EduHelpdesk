using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace EduHelpdesk.Agent;

// The icon by the clock, started for whoever signs in (Installer.AddTray). It shows this PC's asset tag and name, so
// someone ringing IT can read them out, and copies either. It only reads what the service wrote (AgentStatus) and talks
// to nobody. One per sign-in session.
public static class Tray
{
    public static int Run()
    {
        using var mutex = new Mutex(true, @"Local\EduHelpdeskAgentTray", out var first);
        if (!first) return 0;
        var thread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.Run(new TrayContext());
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return 0;
    }

    private sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _icon;
        private readonly ToolStripMenuItem _tag = new() { Enabled = false };
        private readonly ToolStripMenuItem _name = new() { Enabled = false };
        private readonly ToolStripMenuItem _copyTag;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 60_000 };
        private AgentStatus.Snapshot _status = AgentStatus.Read();

        public TrayContext()
        {
            _copyTag = new ToolStripMenuItem("Copy asset tag", null, (_, _) => Copy(_status.AssetTag));
            var menu = new ContextMenuStrip();
            menu.Items.AddRange([_tag, _name, new ToolStripSeparator(), _copyTag, new ToolStripMenuItem("Copy computer name", null, (_, _) => Copy(_status.ComputerName))]);
            _tag.Font = new Font(menu.Font, FontStyle.Bold);
            _icon = new NotifyIcon { Icon = DrawIcon(), ContextMenuStrip = menu, Visible = true };
            _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowCard(); };
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
            Refresh();
        }

        private string TagText => _status.AssetTag.Length > 0 ? _status.AssetTag
            : _status.Status == "waiting" ? "waiting for IT to confirm"
            : "not reported yet";

        private void Refresh()
        {
            _status = AgentStatus.Read();
            _tag.Text = $"Asset tag: {TagText}";
            _name.Text = $"Computer name: {_status.ComputerName}";
            _copyTag.Enabled = _status.AssetTag.Length > 0;
            var tip = _status.AssetTag.Length > 0 ? $"EduHelpdesk - {_status.AssetTag} ({_status.ComputerName})" : $"EduHelpdesk - {_status.ComputerName}";
            _icon.Text = tip.Length > 120 ? tip[..120] : tip;
        }

        private void ShowCard()
        {
            Refresh();
            _icon.ShowBalloonTip(8000, "This computer", $"Asset tag: {TagText}\nComputer name: {_status.ComputerName}\nQuote these when you contact IT.", ToolTipIcon.Info);
        }

        private static void Copy(string text)
        {
            if (text.Length == 0) return;
            try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.ExternalException) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _icon.Visible = false;
                _icon.Dispose();
            }
            base.Dispose(disposing);
        }

        // A teal square with "EH", drawn rather than shipped, at the size Windows asks for.
        private static Icon DrawIcon()
        {
            var size = SystemInformation.SmallIconSize.Width;
            using var bitmap = new Bitmap(size, size);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using var brush = new SolidBrush(Color.FromArgb(15, 118, 110));
                using var path = new GraphicsPath();
                var r = size / 4f;
                path.AddArc(0, 0, r, r, 180, 90);
                path.AddArc(size - r - 1, 0, r, r, 270, 90);
                path.AddArc(size - r - 1, size - r - 1, r, r, 0, 90);
                path.AddArc(0, size - r - 1, r, r, 90, 90);
                path.CloseFigure();
                graphics.FillPath(brush, path);
                using var font = new Font("Segoe UI", size * 0.38f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                graphics.DrawString("EH", font, Brushes.White, new RectangleF(0, 0, size, size), format);
            }
            return Icon.FromHandle(bitmap.GetHicon());
        }
    }
}
