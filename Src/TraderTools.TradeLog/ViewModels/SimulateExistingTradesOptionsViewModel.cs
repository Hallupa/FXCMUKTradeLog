using System;
using Hallupa.Library;

namespace TraderTools.TradeLog.ViewModels
{
    public enum StopOption
    {
        Original,
        InitialStopOnly,
        InitialStopThenTrail2HR8EMA,
        InitialStopThenTrail2HR25EMA,
        InitialStopThenTrail4HR8EMA,
        InitialStopThenTrail4HR25EMA,

        // https://help.fxcm.com/markets/Trading/Execution-Rollover/Order-Types/876471181/What-is-a-Trailing-Stop-and-how-do-I-place-it.htm
        DynamicTrailingStop
    }

    public enum OrderOption
    {
        Original,
        OriginalOrderPoint1PercentWorse,
        OriginalOrderPoint1PercentBetter,
        OriginalOrderPoint2PercentBetter,
        OriginalOrderPoint5PercentBetter,
    }

    public enum LimitOption
    {
        Original,
        Fixed3RLimit,
        None
    }

    public class SimulateExistingTradesOptionsViewModel
    {
        public SimulateExistingTradesOptionsViewModel(Action closeAction)
        {
            var now = DateTime.UtcNow;
            StartDate = now.AddMonths(-2);
            EndDate = now.AddMonths(-1);

            RunSimulationCommand =new DelegateCommand(o =>
            {
                RunClicked = true;
                closeAction();
            });
        }

        public bool RunClicked { get; private set; }

        public DelegateCommand RunSimulationCommand { get; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public StopOption StopOption { get; set; } = StopOption.Original;

        public LimitOption LimitOption { get; set; } = LimitOption.Original;

        public OrderOption OrderOption { get; set; } = OrderOption.Original;
    }
}