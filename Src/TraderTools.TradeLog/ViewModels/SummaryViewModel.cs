using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Abt.Controls.SciChart;
using Abt.Controls.SciChart.Model.DataSeries;
using Abt.Controls.SciChart.Visuals.RenderableSeries;
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

        public XyDataSeries<DateTime, double> TotalProfitPerCompletedTradesData { get; } = new XyDataSeries<DateTime, double>();

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

        public void Update(List<TradeDetails> trades)
        {
            ProfitPerCompletedTradeData.Clear();
            var orderedTrades = trades.Where(c => c.CloseDateTimeLocal != null).OrderBy(c => c.CloseDateTimeLocal).ToList();

            foreach (var t in orderedTrades)
            {
                if (t.Profit != null)
                {
                    ProfitPerCompletedTradeData.Append(t.CloseDateTimeLocal.Value, (double) t.Profit.Value);
                }
            }

            TotalProfitPerCompletedTradesData.Clear();
            var totalProfit = 0.0M;
            foreach (var t in orderedTrades)
            {
                if (t.Profit != null)
                {
                    totalProfit += t.Profit.Value;
                    TotalProfitPerCompletedTradesData.Append(t.CloseDateTimeLocal.Value, (double)totalProfit);
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