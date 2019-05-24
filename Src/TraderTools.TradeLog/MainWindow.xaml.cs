using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Hallupa.Library;
using log4net;
using Octokit;
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

            Title = Title + $" {typeof(MainWindow).Assembly.GetName().Version}";

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

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.CurrentDispatcher.BeginInvoke((Action)(() =>
            {
                if (Properties.Settings.Default.ShowInfoWindow)
                {
                    var infoWindow = new InfoView
                    {
                        Owner = this
                    };

                    infoWindow.ShowDialog();

                    if (infoWindow.DoNotShowAgainCheckBox.IsChecked == true)
                    {
                        Properties.Settings.Default.ShowInfoWindow = false;
                        Properties.Settings.Default.Save();
                    }
                }

                Task.Run(CheckForNewerVersion);
            }));
        }

        private void CheckForNewerVersion()
        {
            try
            {
                var github = new GitHubClient(new ProductHeaderValue("Hallupa"));
                var releases = github.Repository.Release.GetAll("Hallupa", "FXCMUKTradeLog").Result;

                if (releases.Count > 0)
                {
                    var latestReleaseVersion = releases[0].TagName.Replace("v", "");
                    var assemblyVersion = typeof(MainWindow).Assembly.GetName().Version;
                    var currentVersion = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";

                    if (latestReleaseVersion != currentVersion)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(
                                "Newer version is available - please download from https://github.com/Hallupa/FXCMUKTradeLog",
                                "Newer version available",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Unable to check GitHub releases");
            }
        }

        private void OnClosing(object sender, CancelEventArgs cancelEventArgs)
        {
            _vm.SaveTrades();
            _brokersService.Dispose();
        }
    }
}