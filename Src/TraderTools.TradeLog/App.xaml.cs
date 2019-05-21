using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Abt.Controls.SciChart.Visuals;
using Hallupa.Library;
using log4net;
using TraderTools.Core.Services;
using TraderTools.Core.UI.Services;

namespace TraderTools.TradeLog
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected override void OnStartup(StartupEventArgs e)
        {
            Log.Info("Starting application");

            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Hallupa\FXCMTradeLog");
            BrokersService.DataDirectory = path;
            BrokersCandlesService.EarliestDateTime = new DateTime(2016, 1, 1);

            DependencyContainer.AddAssembly(typeof(App).Assembly);
            DependencyContainer.AddAssembly(typeof(ChartingService).Assembly);
            DependencyContainer.AddAssembly(typeof(BrokersService).Assembly);
        }
    }
}