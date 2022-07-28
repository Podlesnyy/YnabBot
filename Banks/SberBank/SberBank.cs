using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Adp.Banks.Interfaces;
using CsvHelper;
using CsvHelper.Configuration;

namespace Adp.Banks.SberBank;

public class SberBank : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");

    public bool IsItYour(string fileName) => fileName.Replace("_", " ").Contains("Операции по карте ");

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(string fileContent)
    {
        var ret = new List<Transaction>();

        var config = new CsvConfiguration(RussianCi) {Delimiter = ";", HasHeaderRecord = true, BadDataFound = null};
        var csv = new CsvReader(new StringReader(fileContent), config);

        csv.Read();
        while (csv.Read())
        {
            var schet = csv.GetField<string>(1);
            var date = DateTime.Parse(csv.GetField<string>(2), RussianCi);
            var id = $"{schet}_{csv.GetField<string>(4)}_{date:yyyyMMdd}";
            var mccField = csv.GetField<string>(5);
            var mcc = string.IsNullOrEmpty(mccField) ? 0 : Convert.ToInt32(mccField);
            var memo = csv.GetField<string>(8);
            var sum = -1 * csv.GetField<double>(11);

            ret.Add(new Transaction(schet, date, sum, memo, mcc, id, null));
        }

        return ret;
    }
}