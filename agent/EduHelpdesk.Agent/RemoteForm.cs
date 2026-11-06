using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using EduHelpdesk.AgentContract;

namespace EduHelpdesk.Agent;

// "IT would like to connect": shown by the tray icon when a technician sends a Quick Assist code from a ticket
// (RemoteSupport.cs). It names who is asking and for which ticket, shows the code large, and opens Quick Assist with the
// code already copied. Nothing happens without the person here pressing Open - and Quick Assist itself still asks them
// to Allow the technician to see their screen.
public sealed class RemoteForm : Form
{
    public Guid RequestId { get; }
    private readonly RemoteOffer _offer;
    private readonly Label _message;
    private readonly FlowLayoutPanel _buttons;

    public RemoteForm(RemoteOffer offer)
    {
        _offer = offer;
        RequestId = offer.Id;
        Text = "EduHelpdesk - IT would like to connect";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20);
        Font = new Font("Segoe UI", 10f);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        var heading = new Label { AutoSize = true, Text = "IT would like to connect", Font = new Font("Segoe UI", 14f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) };
        _message = new Label
        {
            AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0, 0, 0, 12),
            Text = $"{offer.TechnicianName} from IT would like to see your screen to help with {offer.TicketRef}, using Quick Assist.\n\nPress Open Quick Assist, then type this code where it asks for the code from your helper:"
        };
        var code = new TextBox
        {
            Text = offer.Code, ReadOnly = true, BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center, Width = 440,
            Font = new Font("Consolas", 26f, FontStyle.Bold), BackColor = SystemColors.Control, TabStop = false, Margin = new Padding(0, 0, 0, 12)
        };
        var open = new Button { Text = "Open Quick Assist", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        var no = new Button { Text = "No thanks", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        open.Click += (_, _) => Open();
        no.Click += (_, _) => { AgentStatus.WriteRemoteAnswer(RequestId, "declined"); Close(); };
        _buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        _buttons.Controls.AddRange([open, no]);
        layout.Controls.AddRange([heading, _message, code, _buttons]);
        Controls.Add(layout);
        AcceptButton = open;
        CancelButton = no;
    }

    private void Open()
    {
        try { Clipboard.SetText(_offer.Code); } catch (System.Runtime.InteropServices.ExternalException) { }
        if (!StartQuickAssist())
        {
            AgentStatus.WriteRemoteAnswer(RequestId, "unavailable");
            _message.Text = "Quick Assist isn't installed on this PC, so IT can't connect this way. IT can install it (it's free from the Microsoft Store), or help another way.";
            Finish();
            return;
        }
        AgentStatus.WriteRemoteAnswer(RequestId, "accepted");
        _message.Text = $"Quick Assist is opening. Type the code below where it asks for the code from your helper (it's also copied, so you can paste it), press Submit, then Allow when it asks.\n\nYou can stop sharing at any time from Quick Assist.";
        TopMost = false;
        Finish();
    }

    // After an answer: just a Close button, the code still showing to copy from.
    private void Finish()
    {
        _buttons.Controls.Clear();
        var close = new Button { Text = "Close", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        close.Click += (_, _) => Close();
        _buttons.Controls.Add(close);
        AcceptButton = CancelButton = close;
    }

    // The Store's Quick Assist by its link, or the one built into older Windows.
    private static bool StartQuickAssist()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-quick-assist:") { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception) { }
        var builtIn = Path.Combine(Environment.SystemDirectory, "quickassist.exe");
        if (!File.Exists(builtIn)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(builtIn) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception) { return false; }
    }
}
