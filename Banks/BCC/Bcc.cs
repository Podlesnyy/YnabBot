using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adp.Banks.Interfaces;

namespace Adp.Banks.BCC;

public class BCCBank : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");
    public bool IsItYour(string fileName) => fileName.Contains("bcctenge");

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(string fileContent)
    {
        var ret = new List<Transaction>();
        var list = fileContent.Split(Environment.NewLine);
        for (var i = 0; i < list.Length / 4; i++)
        {
            var transList = list.Skip(i * 4).Take(4).ToList();
            var memo = transList[1];
            var date = DateTime.Parse(transList[2], RussianCi);
            var sumStr = transList[3].Replace(" ", "").Replace("₸", "");
            var sum = -1 * Convert.ToDouble(sumStr);
            ret.Add(new Transaction("bccirontenge", date, sum, memo, 0, null, null));
        }

        return ret;
    }
}