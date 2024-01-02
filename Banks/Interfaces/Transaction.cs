using System;

namespace Adp.Banks.Interfaces;

public sealed class Transaction( string bankAccount, DateTime date, double amount, string memo, int mcc, string id, string payee, string ynabBudget = null, string ynabAccount = null )
{
    public string BankAccount { get; } = bankAccount;
    public DateTime Date { get; } = date;
    public double Amount { get; } = amount;
    public string Memo { get; } = memo;
    public int Mcc { get; } = mcc;
    public string Id { get; } = id;
    public string Payee { get; } = payee;
    public string YnabBudget { get; set; } = ynabBudget;
    public string YnabAccount { get; set; } = ynabAccount;
}