using System;
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
using TraderTools.Basics.Extensions;
using TraderTools.Core.Services;
using TraderTools.Core.UI.ViewModels;
using TraderTools.Simulation;
using TraderTools.TradeLog.Views;

namespace TraderTools.TradeLog.ViewModels
{
    public class SimTrade : Trade
    {
        private decimal? _originalRMultiple;
        private decimal? _originalEntryPrice;
        private decimal? _originalClosePrice;
        private string _originalStatus;
        private decimal? _diffFromOriginalR;

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
        [Import] private BrokersService _brokersService;
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

                var producerConsumer = new ProducerConsumer<(MarketDetails Market, List<SimTrade> Orders)>(3,
                    d =>
                {
                    if (_stopSimulation)
                    {
                        return ProducerConsumerActionResult.Stop;
                    }

                    Log.Info($"Running simulation for {d.Market.Name} and {d.Orders.Count} trades");
                    var openTrades = new List<Trade>();
                    var closedTrades = new List<Trade>();

                    var runner = new SimulationRunner(_candlesService, _tradeCalculatorService, _marketDetailsService);
                    var timeframes = new List<Timeframe> { Timeframe.H2, Timeframe.M15 };
                    var timeframeIndicatorsRequired = new TimeframeLookup<Indicator[]>();

                    switch (options.StopOption)
                    {
                        case StopOption.InitialStopThenTrail2HR8EMA:
                            timeframeIndicatorsRequired[Timeframe.H2] = new[] { Indicator.EMA8, Indicator.ATR };
                            if (!timeframes.Contains(Timeframe.H2)) timeframes.Add(Timeframe.H2);
                            break;
                        case StopOption.InitialStopThenTrail4HR8EMA:
                            timeframeIndicatorsRequired[Timeframe.H4] = new[] { Indicator.EMA8, Indicator.ATR };
                            if (!timeframes.Contains(Timeframe.H4)) timeframes.Add(Timeframe.H4);
                            break;
                        case StopOption.InitialStopThenTrail2HR25EMA:
                            timeframeIndicatorsRequired[Timeframe.H2] = new[] { Indicator.EMA25, Indicator.ATR };
                            if (!timeframes.Contains(Timeframe.H2)) timeframes.Add(Timeframe.H2);
                            break;
                        case StopOption.InitialStopThenTrail4HR25EMA:
                            timeframeIndicatorsRequired[Timeframe.H4] = new[] { Indicator.EMA25, Indicator.ATR };
                            if (!timeframes.Contains(Timeframe.H4)) timeframes.Add(Timeframe.H4);
                            break;
                        case StopOption.DynamicTrailingStop:
                            timeframeIndicatorsRequired[Timeframe.H2] = new[] { Indicator.EMA25 };
                            if (!timeframes.Contains(Timeframe.H2)) timeframes.Add(Timeframe.H2);
                            break;
                    }

                    var timeframesAllCandles = SimulationRunner.PopulateCandles(Broker, d.Market.Name, true, timeframes.ToArray(), timeframeIndicatorsRequired,
                        _candlesService, out var m1Candles,  true);

                    runner.SimulateTrades(
                        d.Market, d.Orders.Cast<Trade>().ToList(), openTrades, closedTrades,
                        timeframes.ToList(), m1Candles, timeframesAllCandles,
                        UpdateOpenTradesAction);

                    foreach (var t in d.Orders.Union(openTrades).Union(closedTrades))
                    {
                        _tradeCalculatorService.RecalculateTrade(t, CalculateOptions.IncludeOpenTradesInRMultipleCalculation);
                        allTrades.Add(t);
                    }

                    Interlocked.Increment(ref marketsCompleted);
                    Log.Info($"Completed {marketsCompleted}/{totalMarketsForSimulation} markets");

                    _dispatcher.Invoke(() =>
                    {
                        var newTrades = d.Orders.Union(openTrades).Union(closedTrades).ToList();
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

                    var orders = groupedTrades.Select(t =>
                    {
                        var ret = CopyOriginalTradeForSimulation(t);

                        // For market entry trades, set the order price as the entry price
                        if (ret.OrderPrice == null && ret.EntryDateTime != null)
                        {
                            ret.AddOrderPrice(ret.EntryDateTime.Value, ret.EntryPrice);
                            ret.OrderPrice = ret.EntryPrice;
                            ret.OrderDateTime = new DateTime(ret.EntryDateTime.Value.Ticks, DateTimeKind.Utc);
                        }

                        // Apply order adjustment
                        var orderAdjustmentATRRatio = 0.0M;
                        switch (options.OrderOption)
                        {
                            case OrderOption.OriginalOrderPoint1PercentBetter:
                                orderAdjustmentATRRatio = 1.001M;
                                break;
                            case OrderOption.OriginalOrderPoint1PercentWorse:
                                orderAdjustmentATRRatio = -1.001M;
                                break;
                            case OrderOption.OriginalOrderPoint2PercentBetter:
                                orderAdjustmentATRRatio = 1.002M;
                                break;
                            case OrderOption.OriginalOrderPoint5PercentBetter:
                                orderAdjustmentATRRatio = 1.005M;
                                break;
                        }

                        if (orderAdjustmentATRRatio != 0.0M)
                        {
                            foreach (var order in ret.OrderPrices)
                            {
                                var newPrice = order.Price.Value * orderAdjustmentATRRatio;
                                order.Price = newPrice;
                            }

                            ret.OrderPrice = ret.OrderPrices[0].Price;

                            var candle = _candlesService.GetFirstCandleThatClosesBeforeDateTime(ret.Market, Broker, Timeframe.H2, ret.OrderDateTime.Value);
                            if (ret.TradeDirection == TradeDirection.Long)
                            {
                                ret.OrderType = ret.OrderPrice.Value <= (decimal)candle.Value.CloseAsk
                                    ? OrderType.LimitEntry
                                    : OrderType.StopEntry;
                            }
                            else
                            {
                                ret.OrderType = ret.OrderPrice.Value <= (decimal)candle.Value.CloseAsk
                                    ? OrderType.StopEntry
                                    : OrderType.LimitEntry;
                            }
                        }

                        ret.EntryDateTime = null;
                        ret.CloseDateTime = null;
                        ret.NetProfitLoss = null;
                        ret.EntryPrice = null;
                        ret.OrderAmount = ret.OrderAmount ?? ret.EntryQuantity;
                        ret.EntryQuantity = null;
                        ret.ClosePrice = null;
                        ret.CloseReason = null;
                        ret.CloseDateTime = null;
                        ret.RiskAmount = null;
                        ret.EntryPrice = null;
                        ret.NetProfitLoss = null;
                        ret.GrossProfitLoss = null;
                        ret.RMultiple = null;

                        if (options.StopOption == StopOption.InitialStopOnly || options.StopOption == StopOption.InitialStopThenTrail2HR8EMA
                                                                             || options.StopOption == StopOption.InitialStopThenTrail2HR25EMA
                                                                             || options.StopOption == StopOption.InitialStopThenTrail4HR8EMA
                                                                             || options.StopOption == StopOption.InitialStopThenTrail4HR25EMA
                                                                             || options.StopOption == StopOption.DynamicTrailingStop)
                        {
                            // Single stop price
                            if (ret.StopPrices.Count > 0)
                            {
                                for (var i = ret.StopPrices.Count - 1; i >= 1; i--)
                                {
                                    ret.StopPrices.RemoveAt(i);
                                }
                            }
                        }

                        switch (options.StopOption)
                        {
                            case StopOption.InitialStopThenTrail2HR8EMA:
                            {
                                ret.Custom1 = (int)StopUpdateStrategy.StopTrailIndicator;
                                ret.Custom2 = (int)Timeframe.H2;
                                ret.Custom3 = (int)Indicator.EMA8;
                                break;
                            }
                            case StopOption.InitialStopThenTrail2HR25EMA:
                            {
                                ret.Custom1 = (int)StopUpdateStrategy.StopTrailIndicator;
                                ret.Custom2 = (int)Timeframe.H2;
                                ret.Custom3 = (int)Indicator.EMA25;
                                break;
                            }
                            case StopOption.InitialStopThenTrail4HR8EMA:
                            {
                                ret.Custom1 = (int)StopUpdateStrategy.StopTrailIndicator;
                                ret.Custom2 = (int)Timeframe.H4;
                                ret.Custom3 = (int)Indicator.EMA8;
                                break;
                            }
                            case StopOption.InitialStopThenTrail4HR25EMA:
                            {
                                ret.Custom1 = (int)StopUpdateStrategy.StopTrailIndicator;
                                ret.Custom2 = (int)Timeframe.H4;
                                ret.Custom3 = (int)Indicator.EMA25;
                                break;
                            }
                            case StopOption.DynamicTrailingStop:
                            {
                                ret.Custom1 = (int)StopUpdateStrategy.DynamicTrailingStop;
                                break;
                            }
                        }

                        if (options.LimitOption == LimitOption.None)
                        {
                            // No limit
                            ret.LimitPrices.Clear();
                            ret.LimitPrice = null;
                        }

                        if (options.LimitOption == LimitOption.Fixed3RLimit)
                        {
                            if (ret.StopPrices.Count > 0 && ret.OrderPrice != null)
                            {
                                var limit = ret.OrderPrice.Value + ((ret.OrderPrice.Value - ret.StopPrices[0].Price.Value) * 3M);

                                ret.LimitPrices.Clear();
                                ret.AddLimitPrice(ret.StopPrices[0].Date, limit);
                                ret.LimitPrice = limit;
                            }
                        }

                        return ret;
                    }).ToList();

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

        private enum StopUpdateStrategy
        {
            StopTrailIndicator = 1,
            DynamicTrailingStop = 2
        }

        private void UpdateOpenTradesAction(UpdateTradeParameters p)
        {
            if (p.Trade.Custom1 == (int)StopUpdateStrategy.StopTrailIndicator && p.Trade.Custom2 != null && p.Trade.Custom3 != null)
            {
                StopHelper.TrailIndicator(p.Trade, (Timeframe)p.Trade.Custom2.Value, (Indicator)p.Trade.Custom3.Value, p.TimeframeCurrentCandles, p.TimeTicks);
            }
            else if (p.Trade.Custom1 == (int)StopUpdateStrategy.DynamicTrailingStop)
            {
                StopHelper.TrailDynamicStop(p.Trade, p.TimeframeCurrentCandles, p.TimeTicks);
            }
        }

        public void Update(List<Trade> trades)
        {
            _originalTrades = trades.Where(t => t.RMultiple != null).ToList();
        }

        private static SimTrade CopyOriginalTradeForSimulation(Trade tradeFrom)
        {
            var tradeTo = new SimTrade();
            tradeTo.Id = tradeFrom.Id;
            tradeTo.Market = tradeFrom.Market;
            tradeTo.EntryDateTime = tradeFrom.EntryDateTime;
            tradeTo.StopPrices = tradeFrom.StopPrices.OrderBy(s => s.Date).ToList();
            tradeTo.LimitPrices = tradeFrom.LimitPrices.OrderBy(s => s.Date).ToList();

            tradeTo.BaseAsset = tradeFrom.BaseAsset;
            tradeTo.Broker = tradeFrom.Broker;
            tradeTo.CloseDateTime = tradeFrom.CloseDateTime;
            tradeTo.ClosePrice = tradeFrom.ClosePrice;
            tradeTo.Comments = tradeFrom.Comments;
            tradeTo.CommissionAsset = tradeFrom.CommissionAsset;
            tradeTo.CommissionValue = tradeFrom.CommissionValue;
            tradeTo.Commission = tradeFrom.Commission;
            tradeTo.CloseReason = tradeFrom.CloseReason;
            tradeTo.OrderDateTime = tradeFrom.OrderDateTime;
            tradeTo.RMultiple = tradeFrom.RMultiple;
            tradeTo.EntryPrice = tradeFrom.EntryPrice;
            tradeTo.GrossProfitLoss = tradeFrom.GrossProfitLoss;
            tradeTo.CommissionValueCurrency = tradeFrom.CommissionValueCurrency;
            tradeTo.EntryQuantity = tradeFrom.EntryQuantity;
            tradeTo.EntryValue = tradeFrom.EntryValue;
            tradeTo.PricePerPip = tradeFrom.PricePerPip;
            tradeTo.Strategies = tradeFrom.Strategies;
            tradeTo.InitialLimit = tradeFrom.InitialLimit;
            tradeTo.LimitPrice = tradeFrom.LimitPrice;
            tradeTo.InitialStopInPips = tradeFrom.InitialStopInPips;
            tradeTo.OrderKind = tradeFrom.OrderKind;
            tradeTo.OrderPrices = tradeFrom.OrderPrices.OrderBy(s => s.Date).ToList();
            tradeTo.OrderPrice = tradeFrom.OrderPrice;
            tradeTo.InitialStop = tradeFrom.InitialStop;
            tradeTo.OrderType = tradeFrom.OrderType;
            tradeTo.TradeDirection = tradeFrom.TradeDirection;
            tradeTo.Timeframe = tradeFrom.Timeframe;
            tradeTo.StopPrice = tradeFrom.StopPrice;
            tradeTo.StopInPips = tradeFrom.StopInPips;
            tradeTo.Rollover = tradeFrom.Rollover;
            tradeTo.RiskPercentOfBalance = tradeFrom.RiskPercentOfBalance;
            tradeTo.RiskAmount = tradeFrom.RiskAmount;
            tradeTo.OrderExpireTime = tradeFrom.OrderExpireTime;
            tradeTo.OrderAmount = tradeFrom.OrderAmount;
            tradeTo.NetProfitLoss = tradeFrom.NetProfitLoss;
            tradeTo.LimitInPips = tradeFrom.LimitInPips;
            tradeTo.InitialLimitInPips = tradeFrom.InitialLimitInPips;

            tradeTo.OriginalClosePrice = tradeFrom.ClosePrice;
            tradeTo.OriginalEntryPrice = tradeFrom.EntryPrice;
            tradeTo.OriginalRMultiple = tradeFrom.RMultiple;
            tradeTo.OriginalStatus = tradeFrom.Status;

            return tradeTo;
        }
    }
}