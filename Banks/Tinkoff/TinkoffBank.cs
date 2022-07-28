using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Adp.Banks.Interfaces;
using CsvHelper;
using CsvHelper.Configuration;

namespace Adp.Banks.Tinkoff;

public class TinkoffBank : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");

    public bool IsItYour(string fileName) => fileName.Contains("operations");

    public string FileEncoding => "windows-1251";

    public List<Transaction> Parse(string fileContent)
    {
        var ret = new List<Transaction>();
        var config = new CsvConfiguration(RussianCi) {Delimiter = ";", CultureInfo = RussianCi, HasHeaderRecord = true, BadDataFound = null};
        var csv = new CsvReader(new StringReader(fileContent), config);

        csv.Read();
        while (csv.Read())
        {
            var status = csv.GetField<string>(3);
            if (status != "OK" && status != "WAITING")
                continue;

            var date = csv.GetField<DateTime>(0).Date;
            var mccCode = csv.GetField<string>(10);
            var mcc = string.IsNullOrEmpty(mccCode) ? 0 : Convert.ToInt32(mccCode);
            var memo = csv.GetField<string>(11);
            var sum = -1 * csv.GetField<double>(4);

            ret.Add(new Transaction("tinkoff", date, sum, memo, mcc, null, null));
        }

        return ret;
    }
}