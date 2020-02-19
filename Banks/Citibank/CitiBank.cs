using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Adp.Banks.Interfaces;
using CsvHelper;

namespace Adp.Banks.Citibank
{
    public class CitiBank : IBank
    {
        private static readonly CultureInfo RussianCi = new CultureInfo("ru");

        public bool IsItYour(string fileName)
        {
            return fileName.Contains("ACCT_038");
        }

        public string FileEncoding => "utf-8";

        public List<Transaction> Parse(string file)
        {
            //var transofrm = file.Replace("\",\"", ";").Replace("\"", string.Empty).Replace("'", string.Empty).Replace(Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble()), string.Empty);
            var ret = new List<Transaction>();
            using var reader = new StreamReader(file, Encoding.GetEncoding("utf-8"));
            using var csv = new CsvReader(reader, RussianCi);
            csv.Configuration.Delimiter = ";";
            csv.Configuration.CultureInfo = RussianCi;
            csv.Configuration.HasHeaderRecord = false;
            csv.Configuration.BadDataFound = null;
            while (csv.Read())
            {
                var schet = csv.GetField<string>(3);
                var date = DateTime.ParseExact(csv.GetField(0), "dd/MM/yyyy", CultureInfo.InvariantCulture);

                var memo = csv.GetField<string>(1);
                var sum = -1 * Convert.ToDouble(csv.GetField<string>(2), CultureInfo.InvariantCulture);

                ret.Add(new Transaction(schet, date, sum, memo, 0, null, null));
            }

            return ret;
        }
    }
}