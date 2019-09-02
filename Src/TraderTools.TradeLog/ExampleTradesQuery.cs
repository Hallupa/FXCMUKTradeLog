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
            // Show profit for each strategy
            return trades
                .SelectMany(t => t.Strategies != null && t.Strategies.Contains(",")
                    ? t.Strategies.Split(',').Select(x => new {Strategy = x, Trade = t})
                    : new[] {new {Strategy = "None", Trade = t}})
                .GroupBy(x => x.Strategy)
                .Select(g => new
                    {Strategy = g.Key, TotalProfit = g.Sum(t => t.Trade.Profit != null ? t.Trade.Profit.Value : 0M)});
        }
    }
}