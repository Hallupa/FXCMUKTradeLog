﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Abt.Controls.SciChart.Model.DataSeries;
using TraderTools.Basics;
using TraderTools.Core.UI;

namespace TraderTools.TradeLog.ViewModels
{
    public class SummaryViewModel : INotifyPropertyChanged
    {
        private decimal _profitFromOpenTrades;
        private decimal _overallProfit;

        public SummaryViewModel()
        {
        }

        public ChartViewModel ChartViewModel { get; } = new ChartViewModel();

        public XyDataSeries<DateTime, double> ProfitPerCompletedTradeData { get; } = new XyDataSeries<DateTime, double>();

        public XyDataSeries<DateTime, double> ProfitPerMonth { get; } = new XyDataSeries<DateTime, double>();

        public XyDataSeries<DateTime, double> TotalProfitPerCompletedTradesData { get; } = new XyDataSeries<DateTime, double>();

        public XyDataSeries<DateTime, double> RMultiplePerCompletedTradeData { get; } = new XyDataSeries<DateTime, double>();

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
            ProfitPerCompletedTradeData.Clear();
            RMultiplePerCompletedTradeData.Clear();

            var now = DateTime.Now;
            var orderedTrades = trades.OrderBy(c => c.CloseDateTimeLocal ?? now).ToList();

            foreach (var t in orderedTrades)
            {
                if (t.CloseDateTimeLocal == null) continue;

                if (t.Profit != null)
                {
                    ProfitPerCompletedTradeData.Append(t.CloseDateTimeLocal ?? now, (double)t.Profit.Value);
                }

                if (t.RMultiple != null)
                {
                    RMultiplePerCompletedTradeData.Append(t.CloseDateTimeLocal ?? now, (double)t.RMultiple.Value);
                }
            }

            // Calculate monthly profits
            ProfitPerMonth.Clear();
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
                        ProfitPerMonth.Append(pointDate, monthTrades.Where(x => x.Profit != null).Sum(x => (double)x.Profit.Value));
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

            TotalProfitPerCompletedTradesData.Clear();
            var totalProfit = 0.0M;
            foreach (var t in orderedTrades)
            {
                if (t.Profit != null)
                {
                    totalProfit += t.Profit.Value;
                    TotalProfitPerCompletedTradesData.Append(t.CloseDateTimeLocal ?? now, (double)totalProfit);
                }
            }

            ProfitFromOpenTrades = trades
                    .Where(x => x.EntryDateTime != null && x.CloseDateTime == null && x.Profit != null)
                    .Sum(x => x.Profit.Value);

            OverallProfit = trades.Where(x => x.Profit != null).Sum(x => x.Profit.Value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}