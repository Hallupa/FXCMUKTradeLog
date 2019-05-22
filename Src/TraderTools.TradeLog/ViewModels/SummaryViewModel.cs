using System;
using System.Collections.Generic;
using System.Linq;
using Abt.Controls.SciChart;
using Abt.Controls.SciChart.Model.DataSeries;
using Abt.Controls.SciChart.Visuals.RenderableSeries;
using TraderTools.Basics;
using TraderTools.Core.UI;

namespace TraderTools.TradeLog.ViewModels
{
    public class SummaryViewModel
    {
        public SummaryViewModel()
        {
        }

        public ChartViewModel ChartViewModel { get; } = new ChartViewModel();

        public XyDataSeries<DateTime, double> Data { get; } = new XyDataSeries<DateTime, double>();

        public void Update(List<TradeDetails> trades)
        {
            Data.Clear();
            var orderedTrades = trades.Where(c => c.CloseDateTimeLocal != null).OrderBy(c => c.CloseDateTimeLocal).ToList();

            foreach (var t in orderedTrades)
            {
                if (t.Profit != null)
                {
                    Data.Append(t.CloseDateTimeLocal.Value, (double) t.Profit.Value);
                }
            }
        }
    }
}