using System;
using System.IO;
using System.Windows.Forms;

namespace PerfWidget
{
    // Start at login = a shortcut in the user's Startup folder, the proven autostart path on this box.
    static class Startup
    {
        public static string LinkPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "PerfWidget.lnk"); }
        }

        public static bool IsEnabled { get { return File.Exists(LinkPath); } }

        public static void Set(bool on)
        {
            if (!on)
            {
                if (File.Exists(LinkPath)) File.Delete(LinkPath);
                return;
            }
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic lnk = shell.CreateShortcut(LinkPath);
            lnk.TargetPath = Application.ExecutablePath;
            lnk.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            lnk.Description = "Performance widget on the taskbar";
            lnk.Save();
        }
    }
}
