using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Adp.Banks.Interfaces;
using CsvHelper;
using CsvHelper.Configuration;

namespace Adp.Banks.BCC;

// ReSharper disable once UnusedMember.Global
public class RaiffeisenCertif() : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");

    public bool IsItYour(string fileName) => fileName.Contains("account_certificate_");

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(string fileContent)
    {
        var ret = new List<Transaction>();
        var config = new CsvConfiguration(RussianCi) { Delimiter = ";", HasHeaderRecord = true, BadDataFound = null };
        var csv = new CsvReader(new StringReader(fileContent), config);

        csv.Read();
        while (csv.Read())
        {
            var date = csv.GetField<DateTime>(0);
            var transId = csv.GetField<string>(2);
            if (transId == string.Empty)
                transId = null;

            var postup = csv.GetField<string>(6);
            var rasxod = csv.GetField<string>(7);
            var sumStr = postup == string.Empty ? rasxod : $"-{postup}";
            var sum = Convert.ToDouble(sumStr, RussianCi);

            var memo = csv.GetField<string>(9);
            var card = csv.GetField<string>(10);


            ret.Add(new Transaction("raiffeisen_3406", date, sum, $"{memo}. Номер карты:{card}", 0, transId, memo));
        }

        return ret;
    }
}