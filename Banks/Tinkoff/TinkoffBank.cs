using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Adp.Banks.Interfaces;
using CsvHelper;

namespace Adp.Banks.Tinkoff
{
    public class TinkoffBank : IBank
    {
        private static readonly CultureInfo RussianCi = new CultureInfo("ru");

        public bool IsItYour(string fileName)
        {
            return fileName.Contains("operations");
        }

        public List<Transaction> Parse(string file)
        {
            var ret = new List<Transaction>();
            using var reader = new StreamReader(file, Encoding.GetEncoding("windows-1251"));
            using var csv = new CsvReader(reader, RussianCi);
            csv.Configuration.Delimiter = ";";
            csv.Configuration.HasHeaderRecord = true;
            csv.Configuration.BadDataFound = null;
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
}