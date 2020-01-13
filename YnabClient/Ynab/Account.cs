using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adp.Banks.Interfaces;
using NLog;
using YNAB.SDK;
using YNAB.SDK.Model;

namespace Adp.YnabClient.Ynab
{
    internal sealed class Account
    {
        private readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly object objLock = new object();
        private readonly API ynabApi;

        public Account(string accessToken)
        {
            logger.Info("Init YNAB API");
            ynabApi = new API(accessToken);
        }

        public Dictionary<BudgetSummary, List<YNAB.SDK.Model.Account>> DicAccounts { get; private set; } = new Dictionary<BudgetSummary, List<YNAB.SDK.Model.Account>>();

        public string AddTransactions(IEnumerable<Transaction> transactions)
        {
            lock (objLock)
            {
                var ret = new StringBuilder();
                var grTrans = transactions.GroupBy(item => (Budget: item.YnabBudget, Account: item.YnabAccount));
                foreach (var grTran in grTrans)
                {
                    var budget = DicAccounts.Keys.FirstOrDefault(item => item.Name == grTran.Key.Budget);
                    if (budget == null)
                    {
                        ret.AppendLine("Cant find budget with name " + grTran.Key.Budget);
                        continue;
                    }

                    var account = DicAccounts[budget].FirstOrDefault(item => item.Name == grTran.Key.Account);

                    if (account == null)
                    {
                        ret.AppendLine("Cant find account with name " + grTran.Key.Account + " at budget " + grTran.Key.Budget);
                        continue;
                    }

                    var ynabAccountTransactionAdder = new TransactionsAdder(ynabApi, budget, account);
                    ynabAccountTransactionAdder.AddTransactions(grTran.ToList());
                    ret.AppendLine(ynabAccountTransactionAdder.Result);
                }

                return ret.ToString();
            }
        }

        internal void LoadBudgets()
        {
            logger.Info("Loading accounts");

            lock (objLock)
            {
                DicAccounts = ynabApi.Budgets.GetBudgets().Data.Budgets.ToDictionary(item => item, item => ynabApi.Accounts.GetAccounts(item.Id.ToString()).Data.Accounts);
            }
        }
    }
}