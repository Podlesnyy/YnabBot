using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;

namespace Adp.Banks.BCC;

public class Kaspi : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");

    public bool IsItYour(string fileName)
    {
        return fileName.Contains("kaspi");
    }

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(string fileContent)
    {
        var ret = new List<Transaction>();
        var list = fileContent.Split(Environment.NewLine);

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var line in list)
        {
            var regexObj = new Regex("(.*?) (.*) ₸ (.*)", RegexOptions.Singleline | RegexOptions.Multiline);
            var match = regexObj.Match(line);
            var date = DateTime.Parse(match.Groups[1].Value, RussianCi);
            var sumStr = match.Groups[2].Value.Replace(" ", "");
            var sum = -1 * Convert.ToDouble(sumStr, RussianCi);
            var memo = match.Groups[3].Value;
            ret.Add(new Transaction("kaspi", date, sum, memo, 0, null, null));
        }

        return ret;
    }
}