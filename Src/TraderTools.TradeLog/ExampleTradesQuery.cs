using System;
using System.Collections.Generic;
using System.Linq;
using TraderTools.Basics;
using TraderTools.TradeLog;

namespace TradeQuery
{
    public class ExampleTradesQuery : ITradesQuery
    {
        public object GetResults(List<Trade> trades)
        {
            return ShowOpenTradesProfitsForLatestDay(trades);
        }

        private object ShowOpenTradesProfitsForLatestDay(List<Trade> trades)
        {
            // Show profits for latest day for open trades
            var now = DateTime.UtcNow;
            return
                from trade in trades
                where trade.EntryDateTime != null && (trade.CloseDateTime == null || trade.CloseDateTime >= new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc))
                let profitLatestDay = trade.ProfitLatestDay
                orderby trade.ProfitLatestDay
                select new { Id = trade.Id, trade.Market, ProfitLatestDay = trade.ProfitLatestDay.ToString("£0.00"), Status = trade.Status, trade.Strategies };
        }

        private object ShowProfitsForEachStrategy(List<Trade> trades)
        {
            // Show profit for each strategy
            return
                from trade in trades
                let strategies = trade.Strategies.Split(',')
                from strategy in strategies
                group trade by strategy into strategyGroup
                let TradeProfit = strategyGroup.Sum(t => t.Profit)
                orderby TradeProfit descending
                select new { Strategy = strategyGroup.Key, TradeProfit = TradeProfit.Value.ToString("0.00") };
        }

        /*
         * Trade object properties:
         *   Strategies                (CSV of strategies)
         *   EntryPrice
         *   EntryDateTime
         *   CloseDateTime
         *   Profit
         *   InitialStop
         *   InitialStopInPips
         *   InitialLimit
         *   InitialLimitInPips
         *   ProfitLatestDay
         */
    }
}