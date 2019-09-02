using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Abt.Controls.SciChart;
using Abt.Controls.SciChart.Model.DataSeries;
using Hallupa.Library;
using TraderTools.Basics;
using TraderTools.Basics.Extensions;
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
        private DateRange _profitOverTimeVisibleRange;

        public SummaryViewModel()
        {
            DependencyContainer.ComposeParts(this);
            _broker = _brokersService.Brokers.First(b => b.Name == "FXCM");
            ProfitOverTimeVisibleRange = new DateRange();
        }

        public ChartViewModel ChartViewModel { get; } = new ChartViewModel();

        public XyDataSeries<DateTime, double> ProfitPerCompletedTradeSeries { get; } = new XyDataSeries<DateTime, double>();

        public XyDataSeries<DateTime, double> ProfitPerMonthSeries { get; } = new XyDataSeries<DateTime, double>();

        public XyDataSeries<DateTime, double> ProfitOverTime { get; } = new XyDataSeries<DateTime, double>();

        public XyDataSeries<DateTime, double> RMultiplePerCompletedTradeSeries { get; } = new XyDataSeries<DateTime, double>();

        public XyDataSeries<DateTime, double> RMultiplePerCompletedTradeDataTrendLine { get; } = new XyDataSeries<DateTime, double>();

        public DateRange ProfitOverTimeVisibleRange
        {
            get => _profitOverTimeVisibleRange;
            set
            {
                _profitOverTimeVisibleRange = value;
                OnPropertyChanged();
            }
        }

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

            // Calculate overall profit
            UpdateProfitOverTimeSeries(trades);

            // Calculate monthly profits
            UpdateMonthlyProfitSeries(trades);

            ProfitFromOpenTrades = trades
                    .Where(x => x.EntryDateTime != null && x.CloseDateTime == null && x.Profit != null)
                    .Sum(x => x.Profit.Value);

            OverallProfit = trades.Where(x => x.Profit != null).Sum(x => x.Profit.Value);
        }

        private void UpdateMonthlyProfitSeries(List<Trade> trades)
        {
            ProfitPerMonthSeries.Clear();

            var orderedTradesWithEntry = trades.Where(t => t.EntryDateTime != null).OrderBy(t => t.EntryDateTime.Value).ToList();
            if (orderedTradesWithEntry.Count == 0) return;

            var nowUTC = DateTime.UtcNow;
            var earliestDate = orderedTradesWithEntry[0].EntryDateTime.Value;

            for (var monthDateStart = new DateTime(earliestDate.Year, earliestDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                monthDateStart <= nowUTC;
                monthDateStart = monthDateStart.AddMonths(1))
            {
                var monthDateEnd = monthDateStart.AddMonths(1).AddSeconds(-1);

                var profit = 0M;
                foreach (var trade in orderedTradesWithEntry)
                {
                    if (trade.EntryDateTime <= monthDateEnd && (trade.CloseDateTime == null || trade.CloseDateTime >= monthDateStart))
                    {
                        var tradeProfit = GetTradeProfit(trade, monthDateEnd, Timeframe.D1) - GetTradeProfit(trade, monthDateStart, Timeframe.D1);
                        profit += tradeProfit;
                    }
                }

                ProfitPerMonthSeries.Append(new DateTime(monthDateStart.Year, monthDateStart.Month, 15), (double)profit);
            }
        }

        private void UpdateProfitOverTimeSeries(List<Trade> trades)
        {
            ProfitOverTime.Clear();

            var orderedTradesWithEntry = trades.Where(t => t.EntryDateTime != null).OrderBy(t => t.EntryDateTime.Value).ToList();
            if (orderedTradesWithEntry.Count == 0) return;

            var nowUTC = DateTime.UtcNow;
            var earliestTradeDate = orderedTradesWithEntry[0].EntryDateTime.Value;
            var earliestDate = new DateTime(earliestTradeDate.Year, earliestTradeDate.Month, earliestTradeDate.Day, 23, 59, 59, DateTimeKind.Utc);
            var latestDate = new DateTime(nowUTC.Year, nowUTC.Month, nowUTC.Day, 23, 59, 59, DateTimeKind.Utc);

            for (var periodDateEnd = earliestDate; periodDateEnd <= latestDate; periodDateEnd = periodDateEnd.AddHours(4))
            {
                var profit = 0M;
                foreach (var trade in orderedTradesWithEntry)
                {
                    if (trade.EntryDateTime <= periodDateEnd)
                    {
                        var tradeProfit = GetTradeProfit(trade, periodDateEnd, Timeframe.H4);
                        profit += tradeProfit;
                    }
                }

                ProfitOverTime.Append(periodDateEnd, (double)profit);
            }

            ProfitOverTimeVisibleRange = new DateRange(latestDate.AddMonths(-3), latestDate);
        }

        private decimal GetTradeProfit(Trade trade, DateTime dateTimeUTC, Timeframe candlesTimeframe)
        {
            if (trade.EntryPrice == null || trade.EntryDateTime == null)
            {
                return 0M;
            }

            if (trade.CloseDateTime != null && trade.CloseDateTime.Value <= dateTimeUTC)
            {
                return trade.Profit ?? 0M;
            }

            if (trade.EntryDateTime >= dateTimeUTC)
            {
                return 0M;
            }

            var latestCandle = _candlesService.GetLastClosedCandle(trade.Market, _broker, candlesTimeframe, dateTimeUTC, _broker.Status == ConnectStatus.Connected);

            if (latestCandle != null && trade.PricePerPip != null)
            {
                var marketDetails = _marketDetailsService.GetMarketDetails(_broker.Name, trade.Market);
                var closePriceToUse = trade.TradeDirection == TradeDirection.Long
                    ? (decimal)latestCandle.Value.CloseBid
                    : (decimal)latestCandle.Value.CloseAsk;
                var profitPips = PipsHelper.GetPriceInPips(trade.TradeDirection == TradeDirection.Long ? closePriceToUse - trade.EntryPrice.Value : trade.EntryPrice.Value - closePriceToUse, marketDetails);
                var totalRunningTime = (DateTime.UtcNow - trade.EntryDateTime.Value).TotalDays;
                var runningTime = (trade.EntryDateTime.Value - dateTimeUTC).TotalDays;

                var tradeProfit = trade.PricePerPip.Value * profitPips +
                                  (!totalRunningTime.Equals(0.0) && trade.Rollover != null
                                      ? trade.Rollover.Value * (decimal)(runningTime / totalRunningTime)
                                      : 0M);

                return tradeProfit;
            }

            return 0M;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}