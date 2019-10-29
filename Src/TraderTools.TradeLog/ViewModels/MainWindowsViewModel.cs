using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Abt.Controls.SciChart.Numerics.CoordinateCalculators;
using Abt.Controls.SciChart.Visuals.Annotations;
using Hallupa.Library;
using log4net;
using TraderTools.Basics;
using TraderTools.Brokers.FXCM;
using TraderTools.Core.UI.Services;
using TraderTools.Core.UI.ViewModels;
using TraderTools.Core.UI.Views;

namespace TraderTools.TradeLog.ViewModels
{
    public enum PageToShow
    {
        Summary,
        Trades,
        Results,
        SimulateTrades,
        QueryTrades
    }

    public class MainWindowsViewModel : TradeViewModelBase, INotifyPropertyChanged
    {
        #region Fields
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly Action<Action<string, string>> _createLoginViewFunc;
        private readonly Func<(Action<string> show, Action<string> updateText, Action close)> _createProgressingViewFunc;
        private FxcmBroker _fxcm;

        [Import] private IBrokersService _brokersService;
        [Import] private IBrokersCandlesService _candlesService;
        [Import] private ChartingService _chartingService;
        [Import] private IMarketDetailsService _marketsService;
        [Import] private ITradeDetailsAutoCalculatorService _tradeAutoCalculatorService;
        [Import] private IDataDirectoryService _dataDirectoryService;
        private string _loginOutButtonText;
        private bool _loginOutButtonEnabled = true;
        private Dispatcher _dispatcher;
        private bool _updateAccountEnabled = true;
        private string _updateAccountButtonText = "Update account";
        private bool _updatingAccount;
        private IBrokerAccount _account;
        private DispatcherTimer _saveTimer;
        private PageToShow _page = PageToShow.Summary;
        private bool _editingTrade;
        private bool _refreshUIOnSave;
        private bool _shownLoginToGetLatestPriceData;

        #endregion

        public MainWindowsViewModel(Action<Action<string, string>> createLoginViewFunc, Func<(Action<string> show, Action<string> updateText, Action close)> createProgressingViewFunc)
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

                        if (_refreshUIOnSave && !_updatingAccount)
                        {
                            RefreshUI(false);

                            _refreshUIOnSave = false;
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
                                      | TradeListDisplayOptionsFlag.ClosePrice
                                      | TradeListDisplayOptionsFlag.Strategies
                                      | TradeListDisplayOptionsFlag.Status
                                      | TradeListDisplayOptionsFlag.Risk
                                      | TradeListDisplayOptionsFlag.Rollover
                                      | TradeListDisplayOptionsFlag.Timeframe
                                      | TradeListDisplayOptionsFlag.Dates
                                      | TradeListDisplayOptionsFlag.Profit;

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
            _brokersService.LoadBrokerAccounts(_tradeAutoCalculatorService, _dataDirectoryService);

            _account = BrokersService.AccountsLookup[Broker];
            _createLoginViewFunc = createLoginViewFunc;
            _createProgressingViewFunc = createProgressingViewFunc;

            LoginOutCommand = new DelegateCommand(o => LoginOut(), o => LoginOutButtonEnabled);
            UpdateAccountCommand = new DelegateCommand(o => UpdateAccount(), o => UpdateAccountEnabled);
            ViewTradeCommand = new DelegateCommand(o =>
            {
                if (_fxcm.Status != ConnectStatus.Connected && !_shownLoginToGetLatestPriceData)
                {
                    _shownLoginToGetLatestPriceData = true;
                    MessageBox.Show("Login to get latest price data", "Login to FXCM", MessageBoxButton.OK);
                }

                var progressViewActions = _createProgressingViewFunc();

                ViewTradeCommand.RaiseCanExecuteChanged();

                Task.Run(() =>
                {
                    if (SelectedTrade.Timeframe != null && LargeChartTimeframeOptions.Contains(SelectedTrade.Timeframe.Value))
                    {
                        LargeChartTimeframe = SelectedTrade.Timeframe.Value;
                    }

                    ViewTrade(SelectedTrade, _fxcm.Status == ConnectStatus.Connected);

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
                if (SelectedTrade.Timeframe != null && LargeChartTimeframeOptions.Contains(SelectedTrade.Timeframe.Value))
                {
                    LargeChartTimeframe = SelectedTrade.Timeframe.Value;
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
                ShowSubOptions = true,
                SubItemsIndex = 1
            };

            SummaryViewModel = new SummaryViewModel();
            SimulateTradesViewModel = new SimulateExistingTradesViewModel();
            QueryTradesViewModel =new QueryTradesViewModel();

            Trades.CollectionChanged += TradesOnCollectionChanged;

            RefreshUI(true);

            _chartingService.ChartLineChangedObservable.Subscribe(ChartLinesChanged);
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
        
        private void ChartLinesChanged(object obj)
        {
            if (TradeShowingOnChart != null && ChartViewModel != null && ChartViewModel.ChartPaneViewModels.Count > 0 && ChartViewModel.ChartPaneViewModels[0].TradeAnnotations != null)
            {
                TradeShowingOnChart.ChartLines = new List<ChartLine>();

                var annotations = ChartViewModel.ChartPaneViewModels[0].TradeAnnotations.OfType<LineAnnotation>().Where(l => (l.Tag != null && ((string)l.Tag).StartsWith("Added"))).ToList();
                foreach (var a in annotations)
                {
                    var categoryCoordCalc = (ICategoryCoordinateCalculator)a.ParentSurface.XAxis.GetCurrentCoordinateCalculator();

                    var line = new ChartLine
                    {
                        DateTimeUTC1 = a.X1 is DateTime time ? time.ToUniversalTime() : categoryCoordCalc.TransformIndexToData((int)a.X1).ToUniversalTime(),
                        DateTimeUTC2 = a.X2 is DateTime time2 ? time2.ToUniversalTime() : categoryCoordCalc.TransformIndexToData((int)a.X2).ToUniversalTime(),
                        Price1 = a.Y1 is decimal y1 ? y1 : (decimal)((double)a.Y1),
                        Price2 = a.Y2 is decimal y2 ? y2 : (decimal)((double)a.Y2)
                    };

                    TradeShowingOnChart.ChartLines.Add(line);
                }

                DelayedSaveAndUIRefresh(false);
            }
        }

        private void DelayedSaveAndUIRefresh(bool refreshUI)
        {
            _refreshUIOnSave = _refreshUIOnSave || refreshUI;
            _saveTimer.Start();
        }

        public TradesResultsViewModel ResultsViewModel { get; }

        public SummaryViewModel SummaryViewModel { get; }

        public SimulateExistingTradesViewModel SimulateTradesViewModel { get; }

        public QueryTradesViewModel QueryTradesViewModel { get; }

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
        [Import] public IBrokersService BrokersService { get; private set; }
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
                _account.UpdateBrokerAccount(Broker, BrokerCandleService, _marketsService, _tradeAutoCalculatorService, str => progressViewActions.updateText(str), UpdateOption.ForceUpdate);

                _dispatcher.Invoke(() =>
                {
                    _updatingAccount = false;
                    UpdateAccountButtonText = "Update account";
                    UpdateAccountCommand.RaiseCanExecuteChanged();
                    Log.Info("Trades updated");
                    SaveTrades();

                    progressViewActions.updateText("Updating day candles...");
                    var completed = 0;
                    var total = 0;

                    // Update candles
                    var updateCandles = new ProducerConsumer<string>(
                        6,
                        m =>
                        {
                            try
                            {
                                _candlesService.UpdateCandles(_brokersService.GetBroker("FXCM"), m, Timeframe.D1);
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"Unable to update market: {m}", ex);
                            }

                            Interlocked.Increment(ref completed);

                            progressViewActions.updateText($"Updated day candles for {completed} of {total} markets");

                            return ProducerConsumerActionResult.Success;
                        });

                    foreach (var market in _marketsService.GetAllMarketDetails())
                    {
                        total++;
                        updateCandles.Add(market.Name);
                    }

                    updateCandles.SetProducerCompleted();
                    updateCandles.Start();
                    updateCandles.WaitUntilConsumersFinished();

                    RefreshUI(true);

                    progressViewActions.close();

                    UpdateLoginButtonText();
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
                SimulateTradesViewModel.Update(Trades.ToList());
            }
        }

        private void TradesOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                foreach (var t in e.OldItems.Cast<Trade>())
                {
                    t.PropertyChanged -= TradeOnPropertyChanged;
                }
            }

            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (var t in e.NewItems.Cast<Trade>())
                {
                    t.PropertyChanged += TradeOnPropertyChanged;
                }
            }
        }

        private void TradeOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_updatingAccount) return;

            DelayedSaveAndUIRefresh(true);
        }

        private void RefreshUI(bool recreateTrades = false)
        {
            if (recreateTrades)
            {
                Trades.Clear();

                foreach (var trade in _account.Trades
                    .Where(x => x.OrderDateTime != null || x.EntryDateTime != null)
                    .OrderByDescending(x => int.Parse(x.Id)))
                {
                    Trades.Add(trade);
                }
            }

            ResultsViewModel.UpdateResults();
            SummaryViewModel.Update(Trades.ToList());
            SimulateTradesViewModel.Update(Trades.ToList());

            if (TradeShowingOnChart != null && Trades.Contains(TradeShowingOnChart))
            {
                ShowTrade(TradeShowingOnChart);
            }
        }

        private void LoginOut()
        {
            if (_updatingAccount)
            {
                MessageBox.Show("Account is currently updating", "Account updating", MessageBoxButton.OK);
                return;
            }

            LoginOutButtonEnabled = false;
            var loginAttempted = false;

            if (_fxcm.Status != ConnectStatus.Connected)
            {
                var progressViewActions = _createProgressingViewFunc();

                _createLoginViewFunc((username, password) =>
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            loginAttempted = true;
                            _fxcm.SetUsernamePassword(username, password);
                            _fxcm.Connect();

                            if (_fxcm.Status == ConnectStatus.Connected)
                            {
                                foreach (var marketDetails in _fxcm.GetMarketDetailsList())
                                {
                                    _marketsService.AddMarketDetails(marketDetails);
                                }

                                _marketsService.SaveMarketDetailsList();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Unable to login", ex);
                        }

                        _dispatcher.Invoke(() =>
                        {
                            progressViewActions.close();
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
                        UpdateLoginButtonText();
                    });
                });
            }

            LoginOutButtonEnabled = true;

            UpdateLoginButtonText();
            if (_fxcm.Status != ConnectStatus.Connected && loginAttempted)
            {
                MessageBox.Show("Unable to login", "Failed", MessageBoxButton.OK);
            }
        }

        private void UpdateLoginButtonText()
        {
            if (_fxcm.Status == ConnectStatus.Connected)
            {
                LoginOutButtonText = "Logout";
            }
            else
            {
                LoginOutButtonText = "Login";
            }
        }

        public void SaveTrades()
        {
            var fxcmBroker = BrokersService.Brokers.First(x => x.Name == "FXCM");
            var fxcm = BrokersService.AccountsLookup[fxcmBroker];
            fxcm.SaveAccount();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}