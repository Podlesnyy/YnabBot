using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adp.Banks.Interfaces;
using Newtonsoft.Json;
using NLog;
using YNAB.SDK;
using YNAB.SDK.Model;

namespace Adp.YnabClient.Ynab;

internal sealed class TransactionsAdder( API ynabApi, BudgetSummary budget, YNAB.SDK.Model.Account account )
{
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly StringBuilder progress = new();
    private readonly List< SaveTransaction > saveTransactions = new();
    private readonly List< UpdateTransaction > updateTransactions = new();
    private TransactionsResponse transactionSinceOldest;

    public string Result => progress.ToString();

    public void AddTransactions( IReadOnlyCollection< Transaction > transactions )
    {
        progress.AppendLine( $"Added {transactions.Count} transactions for {budget.Name}/{account.Name}" );

        transactionSinceOldest = GetTransactionSinceDate( transactions.Min( static item => item.Date ) );

        foreach ( var transaction in transactions )
            AddTransaction( transaction );

        if ( updateTransactions.Any() )
        {
            logger.Trace( "Updated: " + JsonConvert.SerializeObject( updateTransactions, Formatting.Indented ) );
            ynabApi.Transactions.UpdateTransactions( budget.Id.ToString(), new UpdateTransactionsWrapper( updateTransactions ) );
            progress.AppendLine( $"Updated: {updateTransactions.Count}" );
        }

        if ( !saveTransactions.Any() )
            return;

        logger.Trace( "New: " + JsonConvert.SerializeObject( saveTransactions, Formatting.Indented ) );
        ynabApi.Transactions.CreateTransaction( budget.Id.ToString(), new SaveTransactionsWrapper( null, saveTransactions ) );
        progress.AppendLine( $"New: {saveTransactions.Count}" );
    }

    private void AddTransaction( Transaction transaction )
    {
        logger.Trace( "Adding transaction: " + JsonConvert.SerializeObject( transaction, Formatting.Indented ) );

        var transactionAdder = new TransactionAdder( transactionSinceOldest, transaction );

        if ( transactionAdder.IsAddBefore() )
            return;

        var updateTransaction = transactionAdder.GetUpdateTransaction();
        if ( updateTransaction != null )
        {
            updateTransactions.Add( updateTransaction );
        }
        else
        {
            var saveTransaction = transactionAdder.GetSaveTransaction();
            saveTransaction.AccountId = account.Id;

            saveTransactions.Add( saveTransaction );
        }
    }

    private TransactionsResponse GetTransactionSinceDate( DateTime date ) => ynabApi.Transactions.GetTransactionsByAccount( budget.Id.ToString(), account.Id.ToString(), date );
}