using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Adp.Banks.Interfaces;
using CsvHelper;
using CsvHelper.Configuration;

namespace Adp.Banks.BCC;

// ReSharper disable once UnusedType.Global
public class Binance : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");
    public bool IsItYour(string fileName) => fileName.Contains("part-");

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(string fileContent)
    {
        var ret = new List<Transaction>();
        var config = new CsvConfiguration(RussianCi) { Delimiter = ",", HasHeaderRecord = true, BadDataFound = null };
        var csv = new CsvReader(new StringReader(fileContent), config);

        csv.Read();
        while (csv.Read())
        {
            var status = csv.GetField<string>(9);
            if (status is not "Completed")
                continue;

            var id = csv.GetField<string>(0);
            var type = csv.GetField<string>(1);
            var sum = (type == "Sell" ? 1 : -1) * Convert.ToDouble(csv.GetField<string>(6), CultureInfo.InvariantCulture);
            var schet = csv.GetField<string>(2);
            var fiatType = csv.GetField<string>(3);
            var priceFromAnotherCurrency = Convert.ToDouble(csv.GetField<string>(4), CultureInfo.InvariantCulture);
            var exchange = Convert.ToDouble(csv.GetField<string>(5), CultureInfo.InvariantCulture);
            var couterParty = csv.GetField<string>(8);

            var date = csv.GetField<DateTime>(10);


            var memo = $"{priceFromAnotherCurrency} {fiatType} по курсу {exchange}. Партнер {couterParty}. Order Number {id}";


            ret.Add(new Transaction(schet, date, sum, memo, 0, id, type));
        }

        return ret;
    }
}