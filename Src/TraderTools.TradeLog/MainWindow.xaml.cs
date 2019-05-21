using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Reflection;
using System.Windows;
using Hallupa.Library;
using log4net;
using TraderTools.Core.Services;
using TraderTools.TradeLog.ViewModels;
using TraderTools.TradeLog.Views;

namespace TraderTools.TradeLog
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        [Import]
        private BrokersService _brokersService;

        private MainWindowsViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();

            DependencyContainer.ComposeParts(this);

#if DEBUG
            Log.Info("Running in debug mode");
#else
            Logger.Visibility = Visibility.Collapsed;
#endif

            Func<(Action<string> show, Action close)> createProgressingViewFunc = () =>
            {
                var view = new ProgressView();
                view.Owner = this;

                return (text =>
                    {
                        view.TextToShow.Text = text;
                        view.ShowDialog();
                    },
                    () => view.Close());
            };

            Action<Action<string, string>> createLoginViewFunc = loginAction =>
            {
                var view = new LoginView { Owner = this };
                var loginVm = new LoginViewModel(() => view.Close(), loginAction);
                view.DataContext = loginVm;
                view.Topmost = true;
                view.ShowDialog();
            };

            _vm = new MainWindowsViewModel(createLoginViewFunc, createProgressingViewFunc);

            DataContext = _vm;
            Closing += OnClosing;
        }

        private void OnClosing(object sender, CancelEventArgs cancelEventArgs)
        {
            _vm.SaveTrades();
            _brokersService.Dispose();
        }
    }
}