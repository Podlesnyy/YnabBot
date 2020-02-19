using System.Collections.Generic;

namespace Adp.Banks.Interfaces
{
    public interface IBank
    {
        bool IsItYour(string fileName);
        List<Transaction> Parse(string file);
    }
}