using System;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace Adp.Banks.Interfaces;

public sealed class Transaction
{
    public Transaction( string bankAccount, DateTime date, double amount, string memo, int mcc, string id, string payee,
                        string ynabBudget = null, string ynabAccount = null )
    {
        BankAccount = bankAccount;
        Date = date;
        Amount = amount;
        Memo = memo;
        Mcc = mcc;
        Id = id;
        Payee = payee;
        YnabBudget = ynabBudget;
        YnabAccount = ynabAccount;
    }

    // ReSharper disable once UnusedMember.Global
    public Transaction()
    {
    }

    public string BankAccount { get; set; }
    public DateTime Date { get; set; }
    public double Amount { get; set; }
    public string Memo { get; set; }
    public int Mcc { get; set; }
    public string Id { get; set; }
    public string Payee { get; set; }
    public string YnabBudget { get; set; }
    public string YnabAccount { get; set; }
}