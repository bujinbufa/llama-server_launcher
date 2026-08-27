using System;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace LlamaManager.Services
{
    public class TrayIconManager : IDisposable
    {
        private WinForms.NotifyIcon? trayIcon;

        public TrayIconManager(Action onDoubleClick)
        {
            var ico = new System.Drawing.Icon(
                System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/app.ico")).Stream);

            trayIcon = new WinForms.NotifyIcon
            {
                Icon = ico,
                Text = "Llama启动器",
                Visible = false
            };

            trayIcon.DoubleClick += (s, e) => onDoubleClick();
        }

        public void Show() => trayIcon!.Visible = true;
        public void Hide() => trayIcon!.Visible = false;

        public void Dispose()
        {
            trayIcon?.Dispose();
            trayIcon = null;
        }
    }
}
