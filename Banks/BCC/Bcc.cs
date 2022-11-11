using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adp.Banks.Interfaces;

namespace Adp.Banks.BCC;

public class BccBank : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");
    public bool IsItYour(string fileName) => fileName.Contains("bcctenge");

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(string fileContent)
    {
        var ret = new List<Transaction>();
        var list = fileContent.Split("statementlogo").Skip(1).ToList();
        foreach (var t in list)
        {
            var transList = t.Split(Environment.NewLine);
            var memo = transList[1];
            var date = DateTime.Parse(transList[2], RussianCi);
            var sumStr = transList[3].Replace(" ", string.Empty).Replace("₸", string.Empty);
            var sum = -1 * Convert.ToDouble(sumStr, CultureInfo.InvariantCulture);
            if (transList.Length > 5)
            {
                var cashbackSumStr = transList[4].Replace("Кешбэк:", string.Empty).Replace(" ", string.Empty).Replace("₸", string.Empty);
                var cashbackSum = -1 * Convert.ToDouble(cashbackSumStr, CultureInfo.InvariantCulture);
                ret.Add(new Transaction("bccirontenge", date, cashbackSum, memo, 0, null, "Cashback"));
            }

            ret.Add(new Transaction("bccirontenge", date, sum, memo, 0, null, null));
        }

        return ret;
    }
}