using System.Collections.Generic;

namespace Domain;

public class User
{
    public int Id { get; set; }
    public string MessengerUserId { get; set; }
    public YnabAccess Access { get; set; }
    public YnabAccount DefaultYnabAccount { get; set; }
    public List< BankAccountToYnabAccount > BankAccountToYnabAccounts { get; set; }
}