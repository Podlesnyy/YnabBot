using System;
using System.Collections.Generic;
using System.Linq;
using Adp.Banks.Interfaces;
using Newtonsoft.Json;
using NLog;
using YNAB.SDK.Model;

namespace Adp.YnabClient.Ynab;

internal sealed class TransactionAdder
{
    private readonly int amount;
    private readonly DateTime date;
    private readonly bool hasTransactionId;
    private readonly string id;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly int mcc;
    private readonly string memo;
    private readonly string payee;
    private readonly List<TransactionDetail> transactionsWithSameAmountAndDate;

    public TransactionAdder(TransactionsResponse transactionSinceOldest, Transaction transaction)
    {
        id = transaction.Id;
        hasTransactionId = !string.IsNullOrEmpty(id);
        date = transaction.Date.Date;
        mcc = transaction.Mcc;
        memo = GetMemo(transaction.Memo);
        payee = GetPayee(transaction.Payee);
        amount = GetAmount(transaction.Amount);

        transactionsWithSameAmountAndDate = transactionSinceOldest.Data.Transactions.Where(item => item.Amount == amount && item.Date.Date == transaction.Date.Date).ToList();
        logger.Trace("Old transaction with same amount and same date: " + JsonConvert.SerializeObject(transactionsWithSameAmountAndDate, Formatting.Indented));
    }

    public bool IsAddBefore() => ExistTransactionWithSameImportId() || ExistTransactionWithImportIdInsideMemo() || ExistTransactionWithNonEmptyPayeeName() || ExistTransactionWithEmptyImportIdAndTheSameMemo();

    public UpdateTransaction GetUpdateTransaction()
    {
        var holdTransaction = GetHoldTransaction();
        if (holdTransaction == null)
            return null;

        return new UpdateTransaction(holdTransaction.Id,
            holdTransaction.AccountId,
            holdTransaction.Date,
            holdTransaction.Amount,
            null,
            !string.IsNullOrEmpty(holdTransaction.PayeeName) ? holdTransaction.PayeeName : payee,
            holdTransaction.CategoryId,
            hasTransactionId ? $"{memo}:{id}" : memo,
            null,
            true,
            UpdateTransaction.FlagColorEnum.Purple);
    }

    public SaveTransaction GetSaveTransaction() =>
        new SaveTransaction(default,
            date,
            amount,
            payeeName: payee,
            memo: memo,
            approved: hasTransactionId,
            flagColor: hasTransactionId ? SaveTransaction.FlagColorEnum.Orange : SaveTransaction.FlagColorEnum.Red,
            importId: hasTransactionId ? id : null);

    private bool ExistTransactionWithEmptyImportIdAndTheSameMemo()
    {
        return !hasTransactionId && transactionsWithSameAmountAndDate.Any(item => item.ImportId == null && item.Memo == memo && string.IsNullOrEmpty(payee));
    }

    private bool ExistTransactionWithNonEmptyPayeeName()
    {
        return transactionsWithSameAmountAndDate.Any(item => !string.IsNullOrEmpty(item.PayeeName));
    }

    private bool ExistTransactionWithImportIdInsideMemo()
    {
        return hasTransactionId && transactionsWithSameAmountAndDate.Any(item => !string.IsNullOrEmpty(item.Memo) && item.Memo.Contains(id));
    }

    private bool ExistTransactionWithSameImportId()
    {
        return transactionsWithSameAmountAndDate.Any(item => item.ImportId != null && item.ImportId == id);
    }

    private static int GetAmount(double transactionAmount) => (int) (-1000 * transactionAmount);

    private string GetPayee(string transactionPayee)
    {
        var ret = !string.IsNullOrEmpty(transactionPayee) ? transactionPayee : mcc != 0 ? MccCodes.GetCodeDescription(mcc) : null;

        return TruncateString(ret, 50);
    }

    private static string GetMemo(string transactionMemo) => TruncateString(transactionMemo, 200);

    private static string TruncateString(string str, int maxLength)
    {
        if (str == null)
            return string.Empty;

        return str.Length <= maxLength ? str : str.Substring(0, maxLength);
    }

    private TransactionDetail GetHoldTransaction()
    {
        return transactionsWithSameAmountAndDate.FirstOrDefault(item => item.Id != null && item.ImportId == null && (hasTransactionId && !item.Memo.Contains(id) || !string.IsNullOrEmpty(payee)));
    }
}