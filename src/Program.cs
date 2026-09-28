using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace PerfWidget
{
    static class Program
    {
        public const string MessageWindowTitle = "PerfWidget.Message";
        public const int WM_TOGGLE_DETAILS = Native.WM_APP + 1;
        public const int WM_QUIT_WIDGET = Native.WM_APP + 2;

        [STAThread]
        static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (cmd == "--probe")
            {
                // Dump one reading to a file (a winexe has no console).
                string path = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "perfwidget-probe.txt");
                using (var s = new Sampler())
                {
                    s.Sample(); Thread.Sleep(1000);   // rate counters need two samples
                    File.WriteAllText(path, Sampler.Dump(s.Sample()));
                }
                return 0;
            }
            if (cmd == "--details" || cmd == "--quit")
            {
                IntPtr w = Native.FindWindow(null, MessageWindowTitle);
                if (w == IntPtr.Zero) return 2;
                Native.PostMessage(w, (uint)(cmd == "--quit" ? WM_QUIT_WIDGET : WM_TOGGLE_DETAILS), IntPtr.Zero, IntPtr.Zero);
                return 0;
            }
            if (cmd == "--startup")
            {
                Startup.Set(args.Length < 2 || args[1].ToLowerInvariant() != "off");
                return 0;
            }

            bool created;
            using (var mutex = new Mutex(true, @"Local\VoidharborPerfWidget", out created))
            {
                if (!created) return 0;   // already running, same as the Mac widget's single-instance guard
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new WidgetContext());
            }
            return 0;
        }
    }

    // Hidden top-level window: receives the TaskbarCreated broadcast (message-only windows do not)
    // and the WM_APP commands from --details / --quit.
    class MessageWindow : NativeWindow
    {
        public event EventHandler TaskbarCreated;
        public event EventHandler ToggleDetails;
        public event EventHandler QuitRequested;
        readonly uint taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");

        public MessageWindow()
        {
            var cp = new CreateParams();
            cp.Caption = Program.MessageWindowTitle;
            cp.Style = 0;                              // WS_OVERLAPPED, never shown
            cp.ExStyle = Native.WS_EX_TOOLWINDOW;
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)taskbarCreatedMsg) { if (TaskbarCreated != null) TaskbarCreated(this, EventArgs.Empty); }
            else if (m.Msg == Program.WM_TOGGLE_DETAILS) { if (ToggleDetails != null) ToggleDetails(this, EventArgs.Empty); }
            else if (m.Msg == Program.WM_QUIT_WIDGET) { if (QuitRequested != null) QuitRequested(this, EventArgs.Empty); }
            base.WndProc(ref m);
        }
    }

    class WidgetContext : ApplicationContext
    {
        readonly Sampler sampler = new Sampler();
        readonly MessageWindow messages = new MessageWindow();
        readonly DetailsForm details;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        WidgetForm widget;
        Reading last;
        bool quitting;

        public WidgetContext()
        {
            details = new DetailsForm(sampler);
            messages.TaskbarCreated += delegate { EnsureEmbedded(); };
            messages.ToggleDetails += delegate { ToggleDetails(); };
            messages.QuitRequested += delegate { Quit(); };
            details.QuitRequested += delegate { Quit(); };

            EnsureEmbedded();
            Tick(null, EventArgs.Empty);
            timer.Interval = 2000;
            timer.Tick += Tick;
            timer.Start();
        }

        void Tick(object sender, EventArgs e)
        {
            if (quitting) return;
            try { last = sampler.Sample(); } catch { return; }
            EnsureEmbedded();
            if (widget != null && widget.IsHandleCreated)
            {
                widget.Reposition();   // also re-asserts HWND_TOP over the taskbar's XAML sibling
                widget.Render(last);
            }
            details.Update(last);
        }

        // Explorer restarts destroy Shell_TrayWnd and every child with it, so the strip is rebuilt on demand.
        void EnsureEmbedded()
        {
            if (quitting) return;
            if (widget != null && !widget.IsHandleCreated) { widget.Dispose(); widget = null; }
            if (widget != null && widget.IsEmbedded) return;
            if (Native.Taskbar() == IntPtr.Zero) return;
            if (widget == null)
            {
                widget = new WidgetForm();
                widget.LeftClick += delegate { ToggleDetails(); };
                widget.RightClick += delegate { ToggleDetails(); };
            }
            widget.Embed();
            if (last != null) widget.Render(last);
        }

        void ToggleDetails()
        {
            if (details.Visible) { details.Hide(); return; }
            details.Update(last);
            var anchor = widget != null ? widget.ScreenRect : new System.Drawing.Rectangle(0, Screen.PrimaryScreen.Bounds.Bottom - 48, 400, 48);
            details.ShowAbove(anchor);
        }

        void Quit()
        {
            if (quitting) return;
            quitting = true;
            timer.Stop();
            try { details.Close(); } catch { }
            try { if (widget != null) widget.Dispose(); } catch { }
            sampler.Dispose();
            messages.DestroyHandle();
            ExitThread();
        }
    }
}
