using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;

namespace Adp.Banks.BCC;

public class BinanceBuffer : IBank
{
    public bool IsItYour(string fileName) => fileName.Contains("binance");

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(string fileContent)
    {
        var ret = new List<Transaction>();
        var withoutDoubleLines = Regex.Replace(fileContent, @"(?:\r?\n|\r){2,}", Environment.NewLine);
        var list = withoutDoubleLines.Split("Связаться с контрагентом").ToList();
        Console.WriteLine(fileContent);
        Console.WriteLine(list.Count);
        // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
        foreach (var transPlain in list)
        {
            var transList = transPlain.Split(Environment.NewLine).Where(static str => !string.IsNullOrEmpty(str)).ToList();
            if (transList.Count < 10)
                continue;

            var status = transList[9];
            if (status is not "Завершено")
                continue;

            var id = transList[3];
            var type = transList[0];
            var sumStr = Regex.Replace(transList[7], "[^0-9.]", string.Empty);
            var sum = (type == "Продать" ? 1 : -1) * Convert.ToDouble(sumStr, CultureInfo.InvariantCulture);
            var schet = transList[4];
            var fiatType = Regex.Replace(transList[5], "[0-9,.]", string.Empty);
            var priceFromAnotherCurrencyStr = Regex.Replace(transList[5], "[^0-9.]", string.Empty);
            var priceFromAnotherCurrency = Convert.ToDouble(priceFromAnotherCurrencyStr, CultureInfo.InvariantCulture);
            var exchangeStr = Regex.Replace(transList[6], "[^0-9.]", string.Empty);
            var exchange = Convert.ToDouble(exchangeStr, CultureInfo.InvariantCulture);
            var couterParty = transList[8];
            var date = Convert.ToDateTime(transList[2]);
            var memo = $"{priceFromAnotherCurrency} {fiatType} по курсу {exchange}. Партнер {couterParty}. Order Number {id}";
            ret.Add(new Transaction(schet, date, sum, memo, 0, id, type));
        }

        return ret;
    }
}