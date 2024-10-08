﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adp.Banks.Interfaces;
using NLog;
using YNAB.SDK;
using YNAB.SDK.Model;

namespace Adp.YnabClient.Ynab;

internal sealed class Account
{
    public Account()
    {
        logger.Info( "Init YNAB API" );
    }

    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    private readonly object objLock = new();

    public Dictionary< BudgetSummary, List< YNAB.SDK.Model.Account > > DicAccounts { get; private set; } = new();

    public string AddTransactions( IEnumerable< Transaction > transactions, string accessToken )
    {
        lock ( objLock )
        {
            var ynabApi = new API( accessToken );
            var ret = new StringBuilder();
            var grTrans = transactions.GroupBy( static item => ( Budget: item.YnabBudget, Account: item.YnabAccount ) );
            foreach ( var grTran in grTrans )
            {
                var budget = DicAccounts.Keys.FirstOrDefault( item => item.Name == grTran.Key.Budget );
                if ( budget == null )
                {
                    ret.AppendLine( "Cant find budget with name " + grTran.Key.Budget );
                    continue;
                }

                var account = DicAccounts[ budget ].FirstOrDefault( item => item.Name == grTran.Key.Account );

                if ( account == null )
                {
                    ret.AppendLine( "Cant find account with name " + grTran.Key.Account + " at budget " + grTran.Key.Budget );
                    continue;
                }

                var ynabAccountTransactionAdder = new TransactionsAdder( ynabApi, budget, account );
                ynabAccountTransactionAdder.AddTransactions( grTran.ToList() );
                ret.AppendLine( ynabAccountTransactionAdder.Result );
            }

            return ret.ToString();
        }
    }

    internal void LoadBudgets( string accessToken )
    {
        logger.Info( "Loading accounts" );

        var ynabApi = new API( accessToken );

        lock ( objLock )
            DicAccounts = ynabApi.Budgets.GetBudgets().Data.Budgets.ToDictionary( static item => item, item => ynabApi.Accounts.GetAccounts( item.Id.ToString() ).Data.Accounts );
    }
}