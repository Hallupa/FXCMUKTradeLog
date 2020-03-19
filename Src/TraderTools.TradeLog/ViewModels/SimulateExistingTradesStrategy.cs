using System;
using System.Collections.Generic;
using System.Linq;
using TraderTools.Basics;
using TraderTools.Core.Trading;
using TraderTools.Simulation;

namespace TraderTools.TradeLog.ViewModels
{
    [RequiredTimeframeCandles(Timeframe.M1)]
    [RequiredTimeframeCandles(Timeframe.H2, Indicator.ATR, Indicator.EMA8, Indicator.EMA25)]
    [RequiredTimeframeCandles(Timeframe.H4, Indicator.ATR, Indicator.EMA8, Indicator.EMA25)]
    public class SimulateExistingTradesStrategy : IStrategy
    {
        private readonly SimulateExistingTradesOptionsViewModel _options;
        private List<SimTrade> _trades;
        public string Name => "Simulation";

        public SimulateExistingTradesStrategy(List<SimTrade> trades, SimulateExistingTradesOptionsViewModel options)
        {
            _options = options;
            _trades = trades.ToList();
        }

        public List<Trade> CreateNewTrades(
            MarketDetails market,
            TimeframeLookup<List<CandleAndIndicators>> candlesLookup,
            List<Trade> existingTrades,
            ITradeDetailsAutoCalculatorService calculatorService,
            DateTime currentTime)
        {
            if (candlesLookup[Timeframe.M1].Count == 0) return null;

            var candle = candlesLookup[Timeframe.M1][candlesLookup[Timeframe.M1].Count - 1];
            ProcessExistingTrades(existingTrades, currentTime, candle, candlesLookup);

            List<Trade> ret = null;
            for (var i = _trades.Count - 1; i >= 0; i--)
            {
                var t = _trades[i];
                if (t.OriginalTrade.OrderPrices.Count > 0 && t.OriginalTrade.OrderPrices[0].Date <= currentTime || t.OriginalTrade.EntryDateTime <= currentTime)
                {
                    if (ret == null) ret = new List<Trade>();

                    ret.Add(t);
                    _trades.RemoveAt(i);
                }
            }

            if (ret != null)
            {
                ProcessExistingTrades(ret, currentTime, candle, candlesLookup);
            }

            return ret;
        }

        private void ProcessExistingTrades(List<Trade> existingTrades, DateTime currentDateTime, CandleAndIndicators candleAndIndicators, TimeframeLookup<List<CandleAndIndicators>> candlesLookup)
        {
            var candle = candleAndIndicators.Candle;

            foreach (var t in existingTrades.Cast<SimTrade>())
            {
                UpdateTradeOrders(currentDateTime, candle, t);

                UpdateTradeStops(currentDateTime, t, candlesLookup);

                UpdateTradeLimits(currentDateTime, t);
            }
        }

        private void UpdateTradeLimits(DateTime currentDateTime, SimTrade t)
        {
            if (_options.LimitOption == LimitOption.None)
            {
                // Nothing to do
            }
            else if (_options.LimitOption == LimitOption.Fixed3RLimit)
            {
                if (t.StopPrices.Count > 0 && t.OrderPrice != null && t.LimitPrices.Count == 0)
                {
                    var limit = t.OrderPrice.Value + ((t.OrderPrice.Value - t.StopPrices[0].Price.Value) * 3M);

                    t.LimitPrices.Clear();
                    t.AddLimitPrice(t.StopPrices[0].Date, limit);
                    t.LimitPrice = limit;
                }
            }
            else if (_options.LimitOption == LimitOption.Fixed2RLimit)
            {
                if (t.StopPrices.Count > 0 && t.OrderPrice != null && t.LimitPrices.Count == 0)
                {
                    var limit = t.OrderPrice.Value + ((t.OrderPrice.Value - t.StopPrices[0].Price.Value) * 2M);

                    t.LimitPrices.Clear();
                    t.AddLimitPrice(t.StopPrices[0].Date, limit);
                    t.LimitPrice = limit;
                }
            }
            else if (_options.LimitOption == LimitOption.Fixed1Point5RLimit)
            {
                if (t.StopPrices.Count > 0 && t.OrderPrice != null && t.LimitPrices.Count == 0)
                {
                    var limit = t.OrderPrice.Value + ((t.OrderPrice.Value - t.StopPrices[0].Price.Value) * 1.5M);

                    t.LimitPrices.Clear();
                    t.AddLimitPrice(t.StopPrices[0].Date, limit);
                    t.LimitPrice = limit;
                }
            }
            else if (_options.LimitOption == LimitOption.Fixed1RLimit)
            {
                if (t.StopPrices.Count > 0 && t.OrderPrice != null && t.LimitPrices.Count == 0)
                {
                    var limit = t.OrderPrice.Value + ((t.OrderPrice.Value - t.StopPrices[0].Price.Value) * 1M);

                    t.LimitPrices.Clear();
                    t.AddLimitPrice(t.StopPrices[0].Date, limit);
                    t.LimitPrice = limit;
                }
            }
            else if (_options.LimitOption == LimitOption.Original)
            {
                if (t.OriginalTrade.LimitPrices.Count > 0 && t.LimitIndex + 1 < t.OriginalTrade.LimitPrices.Count &&
                    (t.LimitIndex == -1 || t.OriginalTrade.LimitPrices[t.LimitIndex + 1].Date <= currentDateTime))
                {
                    t.LimitIndex++;
                    var price = t.OriginalTrade.LimitPrices[t.LimitIndex].Price;
                    var date = t.OriginalTrade.LimitPrices[t.LimitIndex].Date;
                    t.AddLimitPrice(date, price);
                    t.LimitPrice = price;
                }
            }
        }
        //todo // Run with original setups then test alternatives

        private void UpdateTradeStops(DateTime currentDateTime, SimTrade t, TimeframeLookup<List<CandleAndIndicators>> candlesLookup)
        {
            // Update stop
            var addInitialStopOnly = _options.StopOption == StopOption.InitialStopOnly
                                     || _options.StopOption == StopOption.InitialStopThenTrail2HR8EMA
                                     || _options.StopOption == StopOption.InitialStopThenTrail2HR25EMA
                                     || _options.StopOption == StopOption.InitialStopThenTrail4HR8EMA
                                     || _options.StopOption == StopOption.InitialStopThenTrail4HR25EMA
                                     || _options.StopOption == StopOption.DynamicTrailingStop;

            if (t.OriginalTrade.StopPrices.Count > 0 && t.StopIndex + 1 < t.OriginalTrade.StopPrices.Count &&
                (t.StopIndex == -1 || t.OriginalTrade.StopPrices[t.StopIndex + 1].Date <= currentDateTime))
            {
                if ((addInitialStopOnly && t.StopIndex == -1) || !addInitialStopOnly)
                {
                    t.StopIndex++;
                    var price = t.OriginalTrade.StopPrices[t.StopIndex].Price;
                    var date = t.OriginalTrade.StopPrices[t.StopIndex].Date;
                    t.AddStopPrice(date, price);
                    t.StopPrice = price;
                }
            }

            ApplyStopStrategy(t, candlesLookup, currentDateTime);
        }

        private void UpdateTradeOrders(DateTime currentDateTime, Candle candle, SimTrade t)
        {
            // Update order prices
            if (t.OriginalTrade.OrderPrices.Count > 0 && t.OrderIndex + 1 < t.OriginalTrade.OrderPrices.Count &&
                (t.OrderIndex == -1 || t.OriginalTrade.OrderPrices[t.OrderIndex + 1].Date <= currentDateTime))
            {
                t.OrderIndex++;
                var price = ProcessPriceOption(t.OriginalTrade.OrderPrices[t.OrderIndex].Price);
                var date = t.OriginalTrade.OrderPrices[t.OrderIndex].Date;
                t.AddOrderPrice(date, price);
                t.OrderPrice = price;
                t.OrderAmount = t.OriginalTrade.OrderAmount;
                if (t.OrderDateTime == null) t.OrderDateTime = date;
                ApplyOrderType(price, t, candle);
                t.OrderKind = OrderKind.EntryPrice;
            }

            // Update market order price
            if (t.OrderPrices.Count == 0 && t.OriginalTrade.OrderPrices.Count == 0 && t.EntryPrice != null &&
                t.OriginalTrade.EntryDateTime <= currentDateTime)
            {
                var price = ProcessPriceOption(t.OriginalTrade.EntryPrice);
                var date = t.OriginalTrade.EntryDateTime.Value;
                t.AddOrderPrice(date, price);
                t.OrderPrice = price;
                t.OrderAmount = t.OriginalTrade.EntryQuantity;
                t.OrderDateTime = date;
                ApplyOrderType(price, t, candle);
                t.OrderKind = OrderKind.EntryPrice;
            }
        }

        private void ApplyOrderType(decimal? price, Trade t, Candle candle)
        {
            if (t.TradeDirection == TradeDirection.Long)
            {
                t.OrderType = price <= (decimal)candle.CloseAsk
                    ? OrderType.LimitEntry
                    : OrderType.StopEntry;
            }
            else
            {
                t.OrderType = price <= (decimal)candle.CloseAsk
                    ? OrderType.StopEntry
                    : OrderType.LimitEntry;
            }
        }

        private decimal? ProcessPriceOption(decimal? price)
        {
            if (price == null) return null;

            // Apply order adjustment
            var orderAdjustmentATRRatio = 0.0M;
            switch (_options.OrderOption)
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
                return price * orderAdjustmentATRRatio;
            }

            return price;
        }

        private void ApplyStopStrategy(Trade t, TimeframeLookup<List<CandleAndIndicators>> candlesLookup, DateTime currentTime)
        {
            StopUpdateStrategy? stopUpdate = null;
            Timeframe timeframe = Timeframe.D1;
            Indicator indicator = Indicator.EMA8;

            switch (_options.StopOption)
            {
                case StopOption.InitialStopThenTrail2HR8EMA:
                {
                    stopUpdate = StopUpdateStrategy.StopTrailIndicator;
                    timeframe = Timeframe.H2;
                    indicator = Indicator.EMA8;
                    break;
                }
                case StopOption.InitialStopThenTrail2HR25EMA:
                {
                    stopUpdate = StopUpdateStrategy.StopTrailIndicator;
                    timeframe = Timeframe.H2;
                    indicator = Indicator.EMA25;
                    break;
                }
                case StopOption.InitialStopThenTrail4HR8EMA:
                {
                    stopUpdate = StopUpdateStrategy.StopTrailIndicator;
                    timeframe = Timeframe.H4;
                    indicator = Indicator.EMA8;
                    break;
                }
                case StopOption.InitialStopThenTrail4HR25EMA:
                {
                    stopUpdate = StopUpdateStrategy.StopTrailIndicator;
                    timeframe = Timeframe.H4;
                    indicator = Indicator.EMA25;
                    break;
                }

                case StopOption.DynamicTrailingStop:
                {
                    stopUpdate = StopUpdateStrategy.DynamicTrailingStop;
                    break;
                }
            }

            if (stopUpdate != null)
            {
                if (stopUpdate == StopUpdateStrategy.StopTrailIndicator)
                {
                    StopHelper.TrailIndicator(t, timeframe, indicator, candlesLookup, currentTime.Ticks);
                }
                else if (stopUpdate == StopUpdateStrategy.DynamicTrailingStop)
                {
                    StopHelper.TrailDynamicStop(t, candlesLookup, currentTime.Ticks);
                }
            }
        }
    }
}