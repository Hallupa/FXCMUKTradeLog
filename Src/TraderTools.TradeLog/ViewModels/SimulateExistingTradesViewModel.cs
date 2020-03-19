using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Hallupa.Library;
using log4net;
using TraderTools.Basics;
using TraderTools.Core.UI.ViewModels;
using TraderTools.Simulation;
using TraderTools.TradeLog.Views;

namespace TraderTools.TradeLog.ViewModels
{
    public enum StopUpdateStrategy
    {
        StopTrailIndicator = 1,
        DynamicTrailingStop = 2
    }

    public class SimTrade : Trade
    {
        public SimTrade(Trade originalTrade)
        {
            OriginalTrade = originalTrade;
        }

        public int OrderIndex = -1;
        public int StopIndex = -1;
        public int LimitIndex = -1;

        private decimal? _originalRMultiple;
        private decimal? _originalEntryPrice;
        private decimal? _originalClosePrice;
        private string _originalStatus;
        private decimal? _diffFromOriginalR;

        public Trade OriginalTrade { get; private set; }

        public decimal? OriginalRMultiple
        {
            get => _originalRMultiple;
            set
            {
                _originalRMultiple = value;
                OnPropertyChanged();
            }
        }

        public decimal? DiffFromOriginalR
        {
            get => _diffFromOriginalR;
            set
            {
                _diffFromOriginalR = value;
                OnPropertyChanged();
            }
        }

        public decimal? OriginalEntryPrice
        {
            get => _originalEntryPrice;
            set
            {
                _originalEntryPrice = value;
                OnPropertyChanged();
            }
        }

        public decimal? OriginalClosePrice
        {
            get => _originalClosePrice;
            set
            {
                _originalClosePrice = value;
                OnPropertyChanged();
            }
        }

        public string OriginalStatus
        {
            get => _originalStatus;
            set
            {
                _originalStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public class SimulateExistingTradesViewModel : TradeViewModelBase
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        [Import] private IBrokersCandlesService _candlesService;
        [Import] private ITradeDetailsAutoCalculatorService _tradeCalculatorService;
        [Import] private IMarketDetailsService _marketDetailsService;
        [Import] private IBrokersService _brokersService;
        private List<Trade> _originalTrades = new List<Trade>();
        private decimal? _totalSimR;
        private decimal? _totalOriginalR;
        private bool _updatingCandles;
        private bool _simulationRunning;
        private bool _stopSimulation;
        private decimal? _totalOpenTrades;
        private int? _totalTrades;

        public SimulateExistingTradesViewModel()
        {
            ShowClosedTrades = true;
            DependencyContainer.ComposeParts(this);
            RunSimulationCommand = new DelegateCommand(o => RunSimulation(), o => !SimulationRunning);
            StopSimulationCommand = new DelegateCommand(o => StopSimulation(), o => SimulationRunning);
            UpdateCandlesCommand = new DelegateCommand(o => UpdateCandles());
            Broker = _brokersService.Brokers.First(b => b.Name == "FXCM");

            LargeChartTimeframe = Timeframe.M1;
            SmallChartTimeframe = Timeframe.H2;

            ViewTradeCommand = new DelegateCommand(o =>
            {
                ViewTradeCommand.RaiseCanExecuteChanged();

                Task.Run(() =>
                {
                    ViewTrade(SelectedTrade, false);

                    _dispatcher.Invoke(() =>
                    {
                        ViewTradeCommand.RaiseCanExecuteChanged();
                    });
                });
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
        }

        private void StopSimulation()
        {
            _stopSimulation = true;
        }

        public bool SimulationRunning
        {
            get => _simulationRunning;
            set
            {
                _simulationRunning = value;
                RunSimulationCommand.RaiseCanExecuteChanged();
                OnPropertyChanged();
            }
        }

        private void UpdateCandles()
        {
            if (_updatingCandles) return;
            _updatingCandles = true;
            var fxcm = _brokersService.Brokers.First(x => x.Name == "FXCM");
            var completed = 0;
            var totalMarkets = 0;

            Task.Run(() =>
            {
                var producerConsumer =
                    new ProducerConsumer<(string Market, Timeframe Timeframe)>(10,
                        data =>
                        {
                            Log.Info($"Updating candles for {data.Market} {data.Timeframe}");
                            _candlesService.UpdateCandles(fxcm, data.Market, data.Timeframe);
                            _candlesService.UnloadCandles(data.Market, data.Timeframe, fxcm);
                            var completedUpto = Interlocked.Increment(ref completed);
                            Log.Info($"Completed candles for {data.Market} {data.Timeframe} {completedUpto}/{totalMarkets}");

                            return ProducerConsumerActionResult.Success;
                        });


                foreach (var market in _marketDetailsService.GetAllMarketDetails())
                {
                    foreach (var timeframe in new[] { Timeframe.D1, Timeframe.H8, Timeframe.H4, Timeframe.H2, Timeframe.M1, Timeframe.M15 })
                    {
                        producerConsumer.Add((market.Name, timeframe));
                        totalMarkets++;
                    }
                }

                producerConsumer.SetProducerCompleted();
                producerConsumer.Start();
                producerConsumer.WaitUntilConsumersFinished();

                _dispatcher.Invoke(() => { _updatingCandles = false; });
                Log.Info("Updated FX candles");
            });
        }

        public TradesResultsViewModel ResultsViewModel { get; }

        public DelegateCommand RunSimulationCommand { get; private set; }

        public DelegateCommand StopSimulationCommand { get; private set; }

        public DelegateCommand UpdateCandlesCommand { get; private set; }

        public decimal? TotalSimR
        {
            get { return _totalSimR; }
            set
            {
                _totalSimR = value;
                OnPropertyChanged();
            }
        }

        public decimal? TotalOriginalR
        {
            get => _totalOriginalR;
            set
            {
                _totalOriginalR = value;
                OnPropertyChanged();
            }
        }

        public decimal? TotalOpenTrades
        {
            get => _totalOpenTrades;
            set
            {
                _totalOpenTrades = value;
                OnPropertyChanged();
            }
        }

        public int? TotalTrades
        {
            get => _totalTrades;
            set
            {
                _totalTrades = value;
                OnPropertyChanged();
            }
        }

        private void RunSimulation()
        {
            SimulationRunning = true;
            _stopSimulation = false;
            var optionsView = new SimulateExistingTradesOptionsView { Owner = Application.Current.MainWindow };
            optionsView.ShowDialog();
            var options = optionsView.ViewModel;

            if (!options.RunClicked) return;

            Trades.Clear();
            ResultsViewModel.UpdateResults();
            TotalSimR = null;
            TotalOriginalR = null;
            TotalOpenTrades = null;
            TotalTrades = null;

            Task.Run(() =>
            {
                var allTrades = new List<Trade>();
                var marketsCompleted = 0;
                var totalMarketsForSimulation = 0;

                var producerConsumer = new ProducerConsumer<(MarketDetails Market, List<SimTrade> Orders)>(2,
                    d =>
                {
                    if (_stopSimulation)
                    {
                        return ProducerConsumerActionResult.Stop;
                    }

                    Log.Info($"Running simulation for {d.Market.Name} and {d.Orders.Count} trades");

                    var runner = new SimulationRunner(_candlesService, _tradeCalculatorService, _marketDetailsService);
                    var trades = runner.Run(new SimulateExistingTradesStrategy(d.Orders.ToList(), optionsView.ViewModel), d.Market, Broker, updatePrices: true, cacheCandles: false);



                    foreach (var t in trades)
                    {
                        _tradeCalculatorService.RecalculateTrade(t);
                        allTrades.Add(t);
                    }

                    Interlocked.Increment(ref marketsCompleted);
                    Log.Info($"Completed {marketsCompleted}/{totalMarketsForSimulation} markets");

                    _dispatcher.Invoke(() =>
                    {
                        var newTrades = trades;
                        foreach (var t in newTrades.Cast<SimTrade>())
                        {
                            t.DiffFromOriginalR = t.RMultiple != null && t.OriginalRMultiple != null
                                ? (decimal?)t.RMultiple.Value - t.OriginalRMultiple.Value
                                : null;
                        }

                        Trades.AddRange(newTrades);
                        TotalSimR = Trades.Where(t => t.RMultiple != null).Sum(t => t.RMultiple.Value);
                        TotalOriginalR = Trades.Cast<SimTrade>().Where(t => t.OriginalRMultiple != null).Sum(t => t.OriginalRMultiple.Value);
                        TotalOpenTrades = Trades.Count(t => t.CloseDateTime == null);
                        TotalTrades = Trades.Count;
                        ResultsViewModel.UpdateResults();
                    });

                    return ProducerConsumerActionResult.Success;
                });

                // Create orders
                var tradesToUse = _originalTrades.Where(
                    t => (t.OrderDateTime >= options.StartDate || t.OrderDateTime == null)
                         && t.EntryDateTime >= options.StartDate
                         && t.CloseDateTime != null && t.CloseDateTime <= options.EndDate
                         && t.CloseReason != TradeCloseReason.ManualClose).ToList();

                foreach (var groupedTrades in tradesToUse.GroupBy(t => t.Market))
                {
                    var market = _marketDetailsService.GetMarketDetails("FXCM", groupedTrades.Key);
                    totalMarketsForSimulation++;

                    var orders = groupedTrades.Select(CopyBasicDetailsFromOriginalTradeForSimulation).ToList();

                    producerConsumer.Add((market, orders));
                }

                // Run simulation
                producerConsumer.Start();
                producerConsumer.SetProducerCompleted();
                producerConsumer.WaitUntilConsumersFinished();

                _dispatcher.Invoke(() =>
                {
                    SimulationRunning = false;
                    MessageBox.Show(Application.Current.MainWindow, "Simulation complete", "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            });
        }
        
        public void Update(List<Trade> trades)
        {
            _originalTrades = trades.Where(t => t.RMultiple != null).ToList();
        }

        private static SimTrade CopyBasicDetailsFromOriginalTradeForSimulation(Trade tradeFrom)
        {
            var tradeTo = new SimTrade(tradeFrom);
            tradeTo.Id = tradeFrom.Id;
            tradeTo.Market = tradeFrom.Market;


            tradeTo.BaseAsset = tradeFrom.BaseAsset;
            tradeTo.Broker = tradeFrom.Broker;

            tradeTo.Comments = tradeFrom.Comments;
            tradeTo.CommissionAsset = tradeFrom.CommissionAsset;
            tradeTo.CommissionValue = tradeFrom.CommissionValue;
            tradeTo.Commission = tradeFrom.Commission;

            tradeTo.CommissionValueCurrency = tradeFrom.CommissionValueCurrency;

            tradeTo.Strategies = tradeFrom.Strategies;

            tradeTo.TradeDirection = tradeFrom.TradeDirection;
            tradeTo.Timeframe = tradeFrom.Timeframe;

            tradeTo.Rollover = tradeFrom.Rollover;

            tradeTo.OriginalClosePrice = tradeFrom.ClosePrice;
            tradeTo.OriginalEntryPrice = tradeFrom.EntryPrice;
            tradeTo.OriginalRMultiple = tradeFrom.RMultiple;
            tradeTo.OriginalStatus = tradeFrom.Status;

            return tradeTo;
        }
    }
}