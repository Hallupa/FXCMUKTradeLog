using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hallupa.Library;
using log4net;
using TraderTools.Basics;
using TraderTools.Core.Services;
using TraderTools.Core.UI.ViewModels;
using TraderTools.Simulation;

namespace TraderTools.TradeLog.ViewModels
{
    public class SimTrade : Trade
    {
        private decimal? _originalRMultiple;
        private decimal? _originalEntryPrice;
        private decimal? _originalClosePrice;
        private string _originalStatus;
        public decimal? OriginalRMultiple
        {
            get => _originalRMultiple;
            set
            {
                _originalRMultiple = value;
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

        public SimulateExistingTradesViewModel()
        {
            DependencyContainer.ComposeParts(this);
            RunSimulationCommand = new DelegateCommand(o => RunSimulation());
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

        public TradesResultsViewModel ResultsViewModel { get; }

        public DelegateCommand RunSimulationCommand { get; private set; }

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

        private void RunSimulation()
        {
            Trades.Clear();
            ResultsViewModel.UpdateResults();
            TotalSimR = null;

            Task.Run(() =>
            {
                var allTrades = new List<Trade>();
                var marketsCompleted = 0;
                var totalMarketsForSimulation = 0;

                var producerConsumer = new ProducerConsumer<(MarketDetails Market, List<SimTrade> Orders)>(3,
                    d =>
                {
                    Log.Info($"Running simulation for {d.Market.Name} and {d.Orders.Count} trades");
                    var openTrades = new List<Trade>();
                    var closedTrades = new List<Trade>();

                    var runner = new SimulationRunner(_candlesService, _tradeCalculatorService, _marketDetailsService);
                    var timeframes = new[] { Timeframe.H2, Timeframe.M15 };
                    var timeframeIndicatorsRequired = new TimeframeLookup<Indicator[]>();
                    timeframeIndicatorsRequired[Timeframe.H2] = new[] { Indicator.EMA8, Indicator.ATR };

                    var timeframesAllCandles = runner.PopulateCandles(Broker, d.Market.Name, true, timeframes, timeframeIndicatorsRequired,
                            true, false, null, null, _candlesService, out var m1Candles);

                    runner.SimulateTrades(
                        null, d.Market, d.Orders.Cast<Trade>().ToList(), openTrades, closedTrades,
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
                        Trades.AddRange(d.Orders.Union(openTrades).Union(closedTrades));
                        TotalSimR = Trades.Where(t => t.RMultiple != null).Sum(t => t.RMultiple.Value);
                        TotalOriginalR = Trades.Cast<SimTrade>().Where(t => t.OriginalRMultiple != null).Sum(t => t.OriginalRMultiple.Value);
                        ResultsViewModel.UpdateResults();
                    });

                    return ProducerConsumerActionResult.Success;
                });

                // Create orders
                foreach (var groupedTrades in _originalTrades.GroupBy(t => t.Market))
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

                        //if (!ret.Strategies.Contains("Channel"))
                        // {
                        ret.Custom1 = (int)StopUpdateStrategy.StopTrailIndicator;
                        ret.Custom2 = (int)Timeframe.H2;
                        ret.Custom3 = (int)Indicator.EMA8;

                        // Single stop price
                        if (ret.StopPrices.Count > 0)
                        {
                            for (var i = ret.StopPrices.Count - 1; i >= 1; i--)
                            {
                                ret.StopPrices.RemoveAt(i);
                            }
                        }

                        // No limit
                        ret.LimitPrices.Clear();
                        ret.LimitPrice = null;
                        //}

                        // Fixed 3R limit
                        /*if (ret.StopPrices.Count > 0 && ret.OrderPrice != null)
                        {
                            var limit = ret.OrderPrice.Value + ((ret.OrderPrice.Value - ret.StopPrices[0].Price.Value) * 3M);

                            ret.LimitPrices.Clear();
                            ret.AddLimitPrice(ret.StopPrices[0].Date, limit);
                            ret.LimitPrice = limit;
                        }*/

                        return ret;
                    }).ToList();

                    producerConsumer.Add((market, orders));
                }

                // Run simulation
                producerConsumer.Start();
                producerConsumer.SetProducerCompleted();
                producerConsumer.WaitUntilConsumersFinished();
            });
        }

        private enum StopUpdateStrategy
        {
            StopTrailIndicator = 1
        }

        private void UpdateOpenTradesAction(UpdateTradeParameters p)
        {
            if (p.Trade.Custom1 == (int)StopUpdateStrategy.StopTrailIndicator && p.Trade.Custom2 != null && p.Trade.Custom3 != null)
            {
                StopHelper.TrailIndicator(p.Trade, (Timeframe)p.Trade.Custom2.Value, (Indicator)p.Trade.Custom3.Value, p.TimeframeCurrentCandles, p.TimeTicks);
            }
        }

        public void Update(List<Trade> trades)
        {
            _originalTrades.Clear();
            //var earliest = new DateTime(2019, 5, 1);
            //var latest = new DateTime(2019, 8, 1);
            var earliest = new DateTime(2019, 3, 1);
            var latest = new DateTime(2019, 4, 1);
            foreach (var trade in trades.Where(t =>
                (t.OrderDateTime >= earliest || t.EntryDateTime >= earliest) && t.CloseDateTime != null && t.CloseDateTime <= latest && t.CloseReason != TradeCloseReason.ManualClose))
            {
                if (trade.RMultiple == null) continue;
                //if (trade.Id != "34728772") continue;

                // Order datetime should be 15:57 local time?

                // var t = CreateTrade(trade);
                _originalTrades.Add(trade);
            }
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