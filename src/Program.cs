using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using Nivan.Shield.Core;
using Nivan.Shield.UI;
using Nivan.Shield.AskPass;

namespace Nivan.Shield
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (String.Equals(
                Environment.GetEnvironmentVariable("NIVAN_SHIELD_ASKPASS"),
                "1",
                StringComparison.Ordinal))
                return AskPassProgram.Main(args);

            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\NivanShield.Singleton", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Nivan Shield is already running.", "Nivan Shield", MessageBoxButton.OK, MessageBoxImage.Information);
                    return 0;
                }

                MainController controller = null;
                try
                {
                    Window window;
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Nivan.Shield.MainWindow.xaml"))
                    {
                        if (stream == null) throw new InvalidOperationException("The embedded application interface is missing.");
                        window = (Window)XamlReader.Load(stream);
                    }

                    Application application = new Application();
                    application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    controller = new MainController(window);
                    controller.Initialize();
                    window.Show();
                    application.Run();
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        exception.ToString(),
                        "Nivan Shield could not start",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return 1;
                }
                finally
                {
                    if (controller != null) controller.Dispose();
                }
            }
            return 0;
        }
    }
}
