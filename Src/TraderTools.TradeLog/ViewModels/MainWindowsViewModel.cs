using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Hallupa.Library;
using log4net;
using TraderTools.Basics;
using TraderTools.Brokers.FXCM;
using TraderTools.Core.Broker;
using TraderTools.Core.Services;
using TraderTools.Core.UI.ViewModels;
using TraderTools.Core.UI.Views;

namespace TraderTools.TradeLog.ViewModels
{
    public enum PageToShow
    {
        Summary,
        Trades,
        Results
    }

    public class MainWindowsViewModel : TradeViewModelBase, INotifyPropertyChanged
    {
        #region Fields
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly Action<Action<string, string>> _createLoginViewFunc;
        private readonly Func<(Action<string> show, Action close)> _createProgressingViewFunc;
        private FxcmBroker _fxcm;

        [Import]
        private BrokersService _brokersService;

        private string _loginOutButtonText;
        private bool _loginOutButtonEnabled = true;
        private Dispatcher _dispatcher;
        private bool _updateAccountEnabled = true;
        private string _updateAccountButtonText = "Update account";
        private bool _updatingAccount;
        private BrokerAccount _account;
        private IDisposable _accountUpdatedObserver;
        private DispatcherTimer _saveTimer;
        private PageToShow _page = PageToShow.Summary;
        private bool _editingTrade;

        #endregion

        public MainWindowsViewModel(Action<Action<string, string>> createLoginViewFunc, Func<(Action<string> show, Action close)> createProgressingViewFunc)
        {
            Log.Info("Application started");

            _saveTimer = new DispatcherTimer(
                TimeSpan.FromSeconds(2),
                DispatcherPriority.Normal,
                (o, args) =>
                {
                    _saveTimer.Stop();

                    if (_editingTrade == false)
                    {
                        SaveTrades();
                        ResultsViewModel.UpdateResults();
                        SummaryViewModel.Update(Trades.ToList());

                        if (TradeShowingOnChart != null)
                        {
                            ShowTrade(TradeShowingOnChart);
                        }
                    }
                },
                Dispatcher.CurrentDispatcher);
            _saveTimer.Stop();

            TradeListDisplayOptions = TradeListDisplayOptionsFlag.PoundsPerPip
                                      | TradeListDisplayOptionsFlag.Stop
                                      | TradeListDisplayOptionsFlag.Limit
                                      | TradeListDisplayOptionsFlag.OrderPrice
                                      | TradeListDisplayOptionsFlag.Comments
                                      | TradeListDisplayOptionsFlag.ResultR
                                      | TradeListDisplayOptionsFlag.ClosePrice;

            _loginOutButtonText = "Login";
            _dispatcher = Dispatcher.CurrentDispatcher;
            DependencyContainer.ComposeParts(this);

            // Setup brokers and load accounts
            var brokers = new IBroker[]
            {
                _fxcm = new FxcmBroker()
            };

            Broker = _fxcm;
            _brokersService.AddBrokers(brokers);
            _brokersService.LoadBrokerAccounts();

            _account = BrokersService.AccountsLookup[Broker];
            _accountUpdatedObserver = _account.AccountUpdatedObservable.Subscribe(d =>
                {
                    _dispatcher.Invoke(RefreshUI);
                });

            _createLoginViewFunc = createLoginViewFunc;
            _createProgressingViewFunc = createProgressingViewFunc;

            LoginOutCommand = new DelegateCommand(o => LoginOut(), o => LoginOutButtonEnabled);
            UpdateAccountCommand = new DelegateCommand(o => UpdateAccount(), o => UpdateAccountEnabled);
            ViewTradeCommand = new DelegateCommand(o =>
            {
                if (_fxcm.Status != ConnectStatus.Connected)
                {
                    MessageBox.Show("Login to get price data", "Login to FXCM", MessageBoxButton.OK);
                    return;
                }

                var progressViewActions = _createProgressingViewFunc();

                ViewTradeCommand.RaiseCanExecuteChanged();

                Task.Run(() =>
                {
                    ViewTrade(SelectedTrade);

                    _dispatcher.Invoke(() =>
                    {
                        ViewTradeCommand.RaiseCanExecuteChanged();
                        progressViewActions.close();
                    });
                });

                progressViewActions.show("Loading chart data...");

            });
            ViewTradeSetupCommand = new DelegateCommand(o =>
            {
                if (_fxcm.Status != ConnectStatus.Connected)
                {
                    MessageBox.Show("Login to get price data", "Login to FXCM", MessageBoxButton.OK);
                    return;
                }

                var progressViewActions = _createProgressingViewFunc();

                ViewTradeCommand.RaiseCanExecuteChanged();

                Task.Run(() =>
                {
                    ViewTradeSetup(SelectedTrade);

                    _dispatcher.Invoke(() =>
                    {
                        ViewTradeCommand.RaiseCanExecuteChanged();
                        progressViewActions.close();
                    });
                });

                progressViewActions.show("Loading chart data...");
            });

            ResultsViewModel = new TradesResultsViewModel(() =>
            {
                lock (Trades)
                {
                    return Trades.ToList();
                }
            })
            {
                ShowProfit = true,
                AdvStrategyNaming = true,
                ShowSubOptions = false,
                SubItemsIndex = 1
            };

            SummaryViewModel = new SummaryViewModel();

            Trades.CollectionChanged += TradesOnCollectionChanged;

            RefreshUI();
        }

        #region Properties

        public PageToShow Page
        {
            get { return _page; }
            set
            {
                _page = value;
                OnPropertyChanged();
            }
        }

        public TradesResultsViewModel ResultsViewModel { get; }

        public SummaryViewModel SummaryViewModel { get; }

        public bool UpdateAccountEnabled => !_updatingAccount;

        public string UpdateAccountButtonText
        {
            get => _updateAccountButtonText;
            set
            {
                _updateAccountButtonText = value;
                OnPropertyChanged();
            }
        }

        public DelegateCommand UpdateAccountCommand { get; }
        public DelegateCommand ViewTradeCommand { get; }
        public DelegateCommand ViewTradeSetupCommand { get; }
        [Import] public BrokersService BrokersService { get; private set; }
        [Import] public IBrokersCandlesService BrokerCandleService { get; private set; }

        public string LoginOutButtonText
        {
            get { return _loginOutButtonText; }
            set
            {
                _loginOutButtonText = value;
                OnPropertyChanged();
            }
        }

        public bool LoginOutButtonEnabled
        {
            get => _loginOutButtonEnabled;
            set
            {
                _loginOutButtonEnabled = value;
                LoginOutCommand.RaiseCanExecuteChanged();
                OnPropertyChanged();
            }
        }

        public DelegateCommand LoginOutCommand { get; }
        #endregion

        private void UpdateAccount()
        {
            if (_fxcm.Status != ConnectStatus.Connected)
            {
                MessageBox.Show("FXCM not logged in", "Unable to update account", MessageBoxButton.OK);
                return;
            }

            _updatingAccount = true;
            UpdateAccountCommand.RaiseCanExecuteChanged();
            var progressViewActions = _createProgressingViewFunc();

            Task.Run(() =>
            {
                Log.Info("Updating trades");
                _account.UpdateBrokerAccount(Broker, BrokerCandleService, BrokerAccount.UpdateOption.ForceUpdate);

                _dispatcher.Invoke(() =>
                {
                    _updatingAccount = false;
                    UpdateAccountButtonText = "Update account";
                    UpdateAccountCommand.RaiseCanExecuteChanged();
                    Log.Info("Trades updated");
                    SaveTrades();

                    SummaryViewModel.Update(Trades.ToList());

                    progressViewActions.close();
                });
            });

            progressViewActions.show("Updating account...");
        }

        protected override void EditTrade()
        {
            _editingTrade = true;

            try
            {
                base.EditTrade();
            }
            finally
            {
                _editingTrade = false;
                SaveTrades();
                ResultsViewModel.UpdateResults();
                SummaryViewModel.Update(Trades.ToList());
            }
        }

        private void TradesOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                foreach (var t in e.OldItems.Cast<TradeDetails>())
                {
                    t.PropertyChanged -= TradeDetailsOnPropertyChanged;
                }
            }

            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (var t in e.NewItems.Cast<TradeDetails>())
                {
                    t.PropertyChanged += TradeDetailsOnPropertyChanged;
                }
            }
        }

        private void TradeDetailsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            _saveTimer.Start();
        }

        private void RefreshUI()
        {
            Trades.Clear();

            foreach (var trade in _account.Trades
                .Where(x => x.OrderDateTime != null || x.EntryDateTime != null)
                .OrderByDescending(x => int.Parse(x.Id)))
            {
                Trades.Add(trade);
            }

            ResultsViewModel.UpdateResults();
            SummaryViewModel.Update(Trades.ToList());
        }

        private void LoginOut()
        {
            if (_updatingAccount)
            {
                MessageBox.Show("Account is currently updating", "Account updating", MessageBoxButton.OK);
                return;
            }

            LoginOutButtonEnabled = false;

            if (_fxcm.Status != ConnectStatus.Connected)
            {
                var progressViewActions = _createProgressingViewFunc();

                _createLoginViewFunc((username, password) =>
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            _fxcm.SetUsernamePassword(username, password);
                            _fxcm.Connect();
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Unable to login", ex);
                        }

                        _dispatcher.Invoke(() =>
                        {
                            progressViewActions.close();
                            LoginOutButtonEnabled = true;

                            if (_fxcm.Status == ConnectStatus.Connected)
                            {
                                LoginOutButtonText = "Logout";
                            }
                            else
                            {
                                LoginOutButtonText = "Login";
                                MessageBox.Show("Unable to login", "Failed", MessageBoxButton.OK);
                            }
                        });
                    });

                    progressViewActions.show("Logging in...");
                });
            }
            else
            {
                var progressViewActions = _createProgressingViewFunc();

                progressViewActions.show("Logging out...");
                Task.Run(() =>
                {
                    _fxcm.Disconnect();

                    _dispatcher.Invoke(() =>
                    {
                        progressViewActions.close();
                        LoginOutButtonEnabled = true;
                        LoginOutButtonText = "Login";
                    });
                });
            }
        }

        public void SaveTrades()
        {
            var fxcmBroker = BrokersService.Brokers.First(x => x.Name == "FXCM");
            var fxcm = BrokersService.AccountsLookup[fxcmBroker];
            fxcm.SaveAccount(BrokersService.DataDirectory);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}