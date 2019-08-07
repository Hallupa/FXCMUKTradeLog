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
    public class TradeWithSimulation : Trade
    {
        private decimal? _rMultipleSimulation;
        private decimal? _simEntryPrice;
        private decimal? _simClosePrice;

        public decimal? RMultipleSimulation
        {
            get => _rMultipleSimulation;
            set
            {
                _rMultipleSimulation = value;
                OnPropertyChanged();
            }
        }

        public decimal? SimEntryPrice
        {
            get => _simEntryPrice;
            set
            {
                _simEntryPrice = value;
                OnPropertyChanged();
            }
        }

        public decimal? SimClosePrice
        {
            get => _simClosePrice;
            set
            {
                _simClosePrice = value;
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
        private IBroker _broker;

        public SimulateExistingTradesViewModel()
        {
            DependencyContainer.ComposeParts(this);
            RunSimulationCommand = new DelegateCommand(o => RunSimulation());
            _broker = _brokersService.Brokers.First(b => b.Name == "FXCM");
        }

        public DelegateCommand RunSimulationCommand { get; private set; }

        private void RunSimulation()
        {
            foreach(var t in Trades.Cast<TradeWithSimulation>())
            {
                t.RMultipleSimulation = null;
                t.SimClosePrice = null;
                t.SimEntryPrice = null;
            }

            Task.Run(() =>
            {
                var allTrades = new List<Trade>();
                var marketsCompleted = 0;
                var totalMarketsForSimulation = 0;

                var producerConsumer = new ProducerConsumer<(MarketDetails Market, List<Trade> Orders)>(3,
                    d =>
                {
                    Log.Info($"Running simulation for {d.Market.Name} and {d.Orders.Count} trades");
                    var openTrades = new List<Trade>();
                    var closedTrades = new List<Trade>();

                    var runner = new SimulationRunner(_candlesService, _tradeCalculatorService, _marketDetailsService);
                    var timeframes = new Timeframe[] { };
                    var timeframesAllCandles = runner.PopulateCandles(_broker, d.Market.Name, true, timeframes, null, 
                            false, false, null, null, _candlesService, out var m1Candles);

                    runner.SimulateTrades(null, d.Market, d.Orders, openTrades, closedTrades,
                        timeframes.ToList(), m1Candles, timeframesAllCandles);

                    allTrades.AddRange(d.Orders);
                    allTrades.AddRange(openTrades);
                    allTrades.AddRange(closedTrades);

                    Interlocked.Increment(ref marketsCompleted);
                    Log.Info($"Completed {marketsCompleted}/{totalMarketsForSimulation} markets");

                    _dispatcher.Invoke(() =>
                    {
                        // Update trades
                        foreach (var t in d.Orders.Union(openTrades).Union(closedTrades))
                        {
                            var simulatedTrade = (TradeWithSimulation)Trades.FirstOrDefault(x => x.Id == t.Id);
                            if (simulatedTrade != null)
                            {
                                simulatedTrade.RMultipleSimulation = t.RMultiple;
                                simulatedTrade.SimEntryPrice = t.EntryPrice;
                                simulatedTrade.SimClosePrice = t.ClosePrice;
                            }
                        }
                    });

                    return ProducerConsumerActionResult.Success;
                });

                // Create orders
                foreach (var groupedTrades in Trades.GroupBy(t => t.Market))
                {
                    var market = _marketDetailsService.GetMarketDetails("FXCM", groupedTrades.Key);
                    totalMarketsForSimulation++;

                    var orders = groupedTrades.Select(t =>
                    {
                        var ret = new Trade();
                        _tradeCalculatorService.AddTrade(ret);
                        CopyTradeDetails(t, ret);
                        ret.EntryDateTime = null;
                        ret.CloseDateTime = null;
                        ret.NetProfitLoss = null;
                        ret.RMultiple = null;
                        ret.EntryPrice = null;
                        ret.OrderAmount = ret.OrderAmount ?? ret.EntryQuantity;
                        ret.EntryQuantity = null;
                        ret.ClosePrice = null;
                        ret.CloseReason = null;
                        ret.CloseDateTime = null;

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

        public void Update(List<Trade> trades)
        {
            Trades.Clear();
            foreach (var trade in trades)
            {
                if (trade.StopPrices.Count != 1 || trade.LimitPrices.Count != 1 || trade.RMultiple == null || trade.CloseDateTime == null) continue;

                var t = CreateTrade(trade);
                Trades.Add(t);
            }
        }

        private static void CopyTradeDetails(Trade tradeFrom, Trade tradeTo)
        {
            tradeTo.Id = tradeFrom.Id;
            tradeTo.Market = tradeFrom.Market;
            tradeTo.EntryDateTime = tradeFrom.EntryDateTime;
            tradeTo.StopPrices = new List<DatePrice> { tradeFrom.StopPrices[0] };
            tradeTo.LimitPrices = new List<DatePrice> { tradeFrom.LimitPrices[0] };
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
            tradeTo.OrderPrices = tradeFrom.OrderPrices.ToList();
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
        }

        private static TradeWithSimulation CreateTrade(Trade trade)
        {
            var t = new TradeWithSimulation();
            CopyTradeDetails(trade, t);

            return t;
        }
    }
}