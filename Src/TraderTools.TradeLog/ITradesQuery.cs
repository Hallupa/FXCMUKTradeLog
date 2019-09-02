using System.Collections.Generic;
using TraderTools.Basics;

namespace TraderTools.TradeLog
{
    public interface ITradesQuery
    {
        object GetResults(List<Trade> trades);
    }
}