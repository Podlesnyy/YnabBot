using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Adp.Banks.Interfaces;
using CsvHelper;
using CsvHelper.Configuration;

namespace Adp.Banks.VTB;

public class VtbBank : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");

    public bool IsItYour(string fileName) => fileName.Contains("details");

    public string FileEncoding => "windows-1251";

    public List<Transaction> Parse(string fileContent)
    {
        var ret = new List<Transaction>();
        var config = new CsvConfiguration(RussianCi) {Delimiter = ";", CultureInfo = RussianCi, HasHeaderRecord = true, BadDataFound = null};
        fileContent = string.Join( "\r\n", fileContent.Split("\r\n").Skip(11) );
        var csv = new CsvReader(new StringReader(fileContent), config);
            
        csv.Read();
        while (csv.Read())
        {
            var status = csv.GetField<string>(8);
            if (status != "Исполнено" && status != "В обработке" )
                continue;

            var schet = csv.GetField<string>(0).Substring(1);
            var date = csv.GetField<DateTime>(2).Date;
            var memo = csv.GetField<string>(7);
            var sum = -1 * csv.GetField<double>(3);

            ret.Add(new Transaction(schet, date, sum, memo, 0, null, null));
        }

        return ret;
    }
}