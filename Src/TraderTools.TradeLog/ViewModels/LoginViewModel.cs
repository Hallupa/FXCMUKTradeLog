using System;
using System.Windows.Controls;
using Hallupa.Library;

namespace TraderTools.TradeLog.ViewModels
{
    public class LoginViewModel
    {
        private readonly Action _closeViewAction;
        private readonly Action<string, string> _loginAction;

        public LoginViewModel(Action closeViewAction, Action<string, string> loginAction)
        {
            Username = Properties.Settings.Default.Username;
            
            _closeViewAction = closeViewAction;
            _loginAction = loginAction;
            LoginCommand = new DelegateCommand(Login);
        }

        public DelegateCommand LoginCommand { get; }

        public string Username { get; set; }

        private void Login(object obj)
        {
            var passwordBox = (PasswordBox)obj;
            var password = passwordBox.Password;
            _closeViewAction();

            Properties.Settings.Default.Username = Username;
            Properties.Settings.Default.Save();

            _loginAction(Username, password);
        }
    }
}