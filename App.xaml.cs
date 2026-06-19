using System;
using System.Threading;
using System.Windows;

namespace NetSpeedMonitor
{
    public partial class App : Application
    {
        private static Mutex _instanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "NetSpeedMonitor_Unique_Instance_Mutex";
            _instanceMutex = new Mutex(true, appName, out bool isNewInstance);

            if (!isNewInstance)
            {
                MessageBox.Show("Net Speed Monitor is already running.", "Already Running",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Environment.Exit(0);
            }

            base.OnStartup(e);
        }
    }
}
