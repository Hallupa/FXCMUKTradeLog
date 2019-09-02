using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Hallupa.Library;
using log4net;
using TraderTools.Basics;
using TraderTools.Core.Broker;
using TraderTools.Core.Services;

namespace TraderTools.TradeLog.ViewModels
{
    public class QueryTradesViewModel : INotifyPropertyChanged
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private object _results;
        public event PropertyChangedEventHandler PropertyChanged;
        private static int _classNumber = 1;
        private IBroker _broker;
        private BrokerAccount _account;
        private Action<string> _setCode;
        private Func<string> _getCode;
        private string _code;

        [Import] private BrokersService _brokersService;

        public QueryTradesViewModel()
        {
            DependencyContainer.ComposeParts(this);

            _broker = _brokersService.Brokers.First(b => b.Name == "FXCM");
            _account = _brokersService.AccountsLookup[_broker];

            SetInitialValue();
        }

        public string CodeText
        {
            get => _code;
            set
            {
                _code = value;
                OnPropertyChanged();
            }
        }

        public DelegateCommand RunQueryCommand { get; private set; }

        public object Results
        {
            get { return _results; }
            set
            {
                _results = value;
                OnPropertyChanged();
            }
        }

        private void SetInitialValue()
        {
            var filename = "ExampleTradesQuery.cs";
            if (File.Exists(filename))
            {
                CodeText = File.ReadAllText(filename);
            }

            RunQueryCommand = new DelegateCommand(o => RunQuery());
        }

        private void RunQuery()
        {
            var query = GetTradesQuery(CodeText);

            if (query != null)
            {
                try
                {
                    Results = query.GetResults(_account.Trades);
                }
                catch (Exception ex)
                {
                    Log.Error($"Error running query: {ex}");
                }
            }
            else
            {
                Results = null;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private ITradesQuery GetTradesQuery(string code)
        {
            _classNumber++;

            var namespaceRegex = new Regex(@"namespace [a-zA-Z\.\r\n ]*{");
            var match = namespaceRegex.Match(code);
            if (match.Success)
            {
                code = namespaceRegex.Replace(code, "");

                var removeLastBraces = new Regex("}[ \n]*$");
                code = removeLastBraces.Replace(code, "");
            }

            // Get class name
            var classNameRegex = new Regex("public class ([a-zA-Z0-9]*)");
            match = classNameRegex.Match(code);
            var className = match.Groups[1].Captures[0].Value;

            var a = Compile(code
                    .Replace($"class {className}", "class Test" + _classNumber)
                    .Replace($"public {className}", "public Test" + _classNumber)
                    .Replace($"private {className}", "public Test" + _classNumber),
                "System.dll", "System.Core.dll", "TraderTools.Core.dll", "Hallupa.Library.dll", "TraderTools.Indicators.dll", "TraderTools.Basics.dll",
                "TraderTools.TradeLog.exe",
                @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\PresentationCore.dll",
                @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.ComponentModel.Composition.dll");

            if (a.Errors.Count > 0)
            {
                foreach (var error in a.Errors)
                {
                    Log.Error(error);
                }

                return null;
            }

            // create Test instance
            var t = a.CompiledAssembly.GetType("Test" + _classNumber);

            if (t == null)
            {
                Log.Error("Unable to create class 'Test'");
                return null;
            }

            return (ITradesQuery)Activator.CreateInstance(t);
        }

        private static CompilerResults Compile(string code, params string[] assemblies)
        {
            var csp = new Microsoft.CodeDom.Providers.DotNetCompilerPlatform.CSharpCodeProvider();
            var cps = new CompilerParameters();
            cps.ReferencedAssemblies.AddRange(assemblies);
            cps.GenerateInMemory = false;
            cps.GenerateExecutable = false;
            var compilerResults = csp.CompileAssemblyFromSource(cps, code);


            return compilerResults;
        }
    }
}