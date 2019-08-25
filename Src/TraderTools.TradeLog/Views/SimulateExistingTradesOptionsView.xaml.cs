using System.Windows;
using TraderTools.TradeLog.ViewModels;

namespace TraderTools.TradeLog.Views
{
    /// <summary>
    /// Interaction logic for SimulateExistingTradesOptionsView.xaml
    /// </summary>
    public partial class SimulateExistingTradesOptionsView : Window
    {
        public SimulateExistingTradesOptionsView()
        {
            InitializeComponent();

            ViewModel = new SimulateExistingTradesOptionsViewModel(Close);
            DataContext = ViewModel;
        }

        public SimulateExistingTradesOptionsViewModel ViewModel { get; private set; }
    }
}
