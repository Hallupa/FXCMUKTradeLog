using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using Abt.Controls.SciChart.Model.DataSeries;
using Hallupa.Library;
using TraderTools.Basics;
using TraderTools.Basics.Helpers;
using TraderTools.Core.Services;
using TraderTools.Core.UI;

namespace TraderTools.TradeLog.ViewModels
{
    public class SummaryViewModel : INotifyPropertyChanged
    {
        private decimal _profitFromOpenTrades;
        private decimal _overallProfit;
        [Import] private IBrokersCandlesService _candlesService;
        [Import] private BrokersService _brokersService;
        [Import] private IMarketDetailsService _marketDetailsService;
        private IBroker _broker;

        public SummaryViewModel()
        {
            DependencyContainer.ComposeParts(this);
            _broker = _brokersService.Brokers.First(b => b.Name == "FXCM");
        }

        public ChartViewModel ChartViewModel { get; } = new ChartViewModel();

        public XyDataSeries<DateTime, double> ProfitPerCompletedTradeSeries { get; } = new XyDataSeries<DateTime, double>();

        public XyDataSeries<DateTime, double> ProfitPerMonthSeries { get; } = new XyDataSeries<DateTime, double>();

        public XyDataSeries<DateTime, double> DailyProfitSeries { get; } = new XyDataSeries<DateTime, double>();

        public XyDataSeries<DateTime, double> RMultiplePerCompletedTradeSeries { get; } = new XyDataSeries<DateTime, double>();

        public XyDataSeries<DateTime, double> RMultiplePerCompletedTradeDataTrendLine { get; } = new XyDataSeries<DateTime, double>();

        public decimal ProfitFromOpenTrades
        {
            get => _profitFromOpenTrades;
            set
            {
                _profitFromOpenTrades = value;
                OnPropertyChanged();
            }
        }

        public decimal OverallProfit
        {
            get => _overallProfit;
            set
            {
                _overallProfit = value;
                OnPropertyChanged();
            }
        }

        public void Update(List<Trade> trades)
        {
            ProfitPerCompletedTradeSeries.Clear();
            RMultiplePerCompletedTradeSeries.Clear();
            
            var now = DateTime.Now;
            var orderedTrades = trades.OrderBy(c => c.CloseDateTimeLocal ?? now).ToList();

            foreach (var t in orderedTrades)
            {
                if (t.CloseDateTimeLocal == null) continue;

                if (t.Profit != null)
                {
                    ProfitPerCompletedTradeSeries.Append(t.CloseDateTimeLocal ?? now, (double)t.Profit.Value);
                }

                if (t.RMultiple != null)
                {
                    RMultiplePerCompletedTradeSeries.Append(t.CloseDateTimeLocal ?? now, (double)t.RMultiple.Value);
                }
            }

            // Calculate monthly profits
            ProfitPerMonthSeries.Clear();
            if (orderedTrades.Count > 2)
            {
                var earliest = orderedTrades[0].CloseDateTimeLocal ?? now;
                var latest = orderedTrades.Last(c => c.CloseDateTimeLocal != null).CloseDateTimeLocal ?? now;
                var date = new DateTime(earliest.Year, earliest.Month, 1);

                while (date < latest)
                {
                    var monthTrades = orderedTrades.Where(t => t.Profit != null
                                                               && (t.CloseDateTimeLocal ?? now) >= date
                                                               && (t.CloseDateTimeLocal ?? now) < date.AddMonths(1)).ToList();
                    if (monthTrades.Count > 0)
                    {
                        var pointDate = date.AddDays(14);
                        ProfitPerMonthSeries.Append(pointDate, monthTrades.Where(x => x.Profit != null).Sum(x => (double)x.Profit.Value));
                    }

                    date = date.AddMonths(1);
                }
            }

            // Calculate monthly averages for R-Multiples
            RMultiplePerCompletedTradeDataTrendLine.Clear();
            if (orderedTrades.Count > 2)
            {
                var earliest = orderedTrades[0].CloseDateTimeLocal ?? now;
                var latest = orderedTrades.Last().CloseDateTimeLocal ?? now;
                var date = new DateTime(earliest.Year, earliest.Month, 1);

                while (date < latest)
                {
                    var monthTrades = orderedTrades.Where(t => t.RMultiple != null
                                                               && (t.CloseDateTimeLocal ?? now) >= date
                                                               && (t.CloseDateTimeLocal ?? now) < date.AddMonths(1)).ToList();
                    if (monthTrades.Count > 0)
                    {
                        var pointDate = date.AddDays(14);
                        if (pointDate > latest)
                        {
                            pointDate = latest;
                        }

                        RMultiplePerCompletedTradeDataTrendLine.Append(pointDate, monthTrades.Where(x => x.RMultiple != null).Average(x => (double)x.RMultiple.Value));
                    }

                    date = date.AddMonths(1);
                }
            }

            DailyProfitSeries.Clear();

            UpdateDailyProfitSeries(trades);

            ProfitFromOpenTrades = trades
                    .Where(x => x.EntryDateTime != null && x.CloseDateTime == null && x.Profit != null)
                    .Sum(x => x.Profit.Value);


            OverallProfit = trades.Where(x => x.Profit != null).Sum(x => x.Profit.Value);
        }

        private void UpdateDailyProfitSeries(List<Trade> trades)
        {
            var nowUTC = DateTime.UtcNow;
            var activeTrades = new List<Trade>();
            var outstandingOrderedTrades =
                trades.Where(t => t.EntryDateTime != null).OrderBy(t => t.EntryDateTime.Value).ToList();
            if (outstandingOrderedTrades.Count == 0) return;

            var earliestDate = outstandingOrderedTrades[0].EntryDateTime.Value;
            decimal totalProfit = 0M, completedTradesProfit = 0M;


            for (var date = new DateTime(earliestDate.Year, earliestDate.Month, earliestDate.Day, 23, 59, 59, DateTimeKind.Utc);
                date <= new DateTime(nowUTC.Year, nowUTC.Month, nowUTC.Day, 23, 59, 59, DateTimeKind.Utc);
                date = date.AddDays(1))
            {
                // Add active trades
                for (var i = 0; i < outstandingOrderedTrades.Count; i++)
                {
                    // Add active trades
                    if (outstandingOrderedTrades[i].EntryDateTime.Value <= date)
                    {
                        activeTrades.Add(outstandingOrderedTrades[i]);
                        outstandingOrderedTrades.RemoveAt(i);
                        i--;
                    }
                    else
                    {
                        break;
                    }
                }

                // Remove completed trades
                for (var i = 0; i < activeTrades.Count; i++)
                {
                    if (activeTrades[i].CloseDateTime != null && activeTrades[i].CloseDateTime.Value <= date)
                    {
                        completedTradesProfit += activeTrades[i].Profit != null
                            ? activeTrades[i].Profit.Value
                            : 0M;
                        activeTrades.RemoveAt(i);
                        i--;
                    }
                }

                // Calculate current active trades profit
                var activeTradesProfit = 0M;
                foreach (var trade in activeTrades)
                {
                    var candles = _candlesService.GetCandles(_broker, trade.Market, Timeframe.D1,
                        _broker.Status == ConnectStatus.Connected, null, date);
                    if (candles.Count > 0 && trade.PricePerPip != null)
                    {
                        var latestCandle = candles.Last();
                        var profitPips = PipsHelper.GetPriceInPips(
                            trade.TradeDirection == TradeDirection.Long
                                ? (decimal) latestCandle.CloseBid - trade.EntryPrice.Value
                                : trade.EntryPrice.Value - (decimal) latestCandle.CloseAsk,
                            _marketDetailsService.GetMarketDetails(_broker.Name, trade.Market));
                        var totalRunningTime = (trade.EntryDateTime.Value - DateTime.UtcNow).TotalDays;
                        var currentRunningTime = (trade.EntryDateTime.Value - date).TotalDays;

                        var tradeProfit = trade.PricePerPip.Value * profitPips +
                                          (!totalRunningTime.Equals(0.0) && trade.Rollover != null
                                              ? trade.Rollover.Value * (decimal) (currentRunningTime / totalRunningTime)
                                              : 0M);

                        activeTradesProfit += tradeProfit;
                    }
                }

                DailyProfitSeries.Append(new DateTime(date.Year, date.Month, date.Day), (double) (completedTradesProfit + activeTradesProfit));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}