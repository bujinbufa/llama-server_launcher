using System;
using System.Windows;

namespace LlamaManager
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 捕获未处理的 UI 线程异常
            this.DispatcherUnhandledException += (s, args) =>
            {
                LogError("UI线程异常", args.Exception);
                System.Windows.MessageBox.Show(args.Exception.ToString(), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            // 捕获非 UI 线程异常
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception ?? new Exception("未知异常");
                LogError("非UI线程异常", ex);
                System.Windows.MessageBox.Show(ex.ToString(), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }

        private void LogError(string type, Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText("error.log", $"[{DateTime.Now}] {type}: {ex}\n\n");
            }
            catch { }
        }
    }
}