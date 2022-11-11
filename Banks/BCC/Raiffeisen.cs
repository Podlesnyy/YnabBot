using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Adp.Banks.Interfaces;
using CsvHelper;
using CsvHelper.Configuration;

namespace Adp.Banks.BCC;

public class Raiffeisen : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");
    public bool IsItYour(string fileName) => fileName.Contains("account_statement_");

    public string FileEncoding => "windows-1251";

    public List<Transaction> Parse(string fileContent)
    {
        var ret = new List<Transaction>();
        var config = new CsvConfiguration(RussianCi) { Delimiter = ";", HasHeaderRecord = true, BadDataFound = null };
        var csv = new CsvReader(new StringReader(fileContent), config);

        csv.Read();
        while (csv.Read())
        {
            const string schet = "raiffeisen3406";
            var date = csv.GetField<DateTime>(0);
            var memo = csv.GetField<string>(1);
            var sum = -1 * Convert.ToDouble(csv.GetField<string>(5).Replace(" ", string.Empty), CultureInfo.InvariantCulture);

            ret.Add(new Transaction(schet, date, sum, memo, 0, null, null));
        }

        return ret;
    }
}