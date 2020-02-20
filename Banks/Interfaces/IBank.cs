using System.Collections.Generic;

namespace Adp.Banks.Interfaces
{
    public interface IBank
    {
        string FileEncoding { get; }
        bool IsItYour(string fileName);
        List<Transaction> Parse(string fileContent);
    }
}