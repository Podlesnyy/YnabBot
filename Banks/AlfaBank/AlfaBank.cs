using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;
using CsvHelper;

namespace Adp.Banks.AlfaBank
{
    public class AlfaBank : IBank
    {
        private static readonly CultureInfo RussianCi = new CultureInfo("ru");

        public bool IsItYour(string fileName)
        {
            return fileName.Contains("movementList");
        }

        public string FileEncoding => "windows-1251";

        public List<Transaction> Parse(string fileContent)
        {
            var ret = new List<Transaction>();
            var csv = new CsvReader(new StringReader(fileContent));
            csv.Configuration.CultureInfo = RussianCi;
            csv.Configuration.Delimiter = ";";
            csv.Configuration.HasHeaderRecord = true;
            csv.Configuration.BadDataFound = null;
            csv.Read();
            while (csv.Read())
            {
                //  Счёт кредитной карты; 40817810206230046528; RUR; 13.11.19; CBPP15; Выплата Cash Back Подлесный Андрей Дмитриевич за 01.10.19 - 31.10.19 по World -MasterCard Credit,  в соотв. Приказ №952 01.12.16, Осн.Расчёт; 548,57; 0;
                var idField = csv.GetField<string>(4);
                if (idField == "CRRR#U1704091501")
                    continue;

                var memo = csv.GetField<string>(5);
                var regexSecondDate = new Regex(@"(\d{2}\.\d{2}\.\d{2}) (\d{2}\.\d{2}\.\d{2})");
                var dateField = idField?.Contains("CRD_") == true ? regexSecondDate.Match(memo).Groups[2].Value : csv.GetField<string>(3);
                var date = DateTime.Parse(dateField, RussianCi);

                var schet = csv.GetField<string>(1);
                var id = idField == "HOLD" ? null : $"{schet.Substring(schet.Length - 4, 4)}_{idField}_{date:yyyyMMdd}";
                var rMcc = Regex.Match(memo, @".*MCC(\d*)");
                var mcc = rMcc.Groups.Count == 2 ? Convert.ToInt32(rMcc.Groups[1].Value) : 0;
                var sum = csv.GetField<double>(7) - csv.GetField<double>(6);

                ret.Add(new Transaction(schet, date, sum, memo, mcc, id, null));
            }

            return ret;
        }
    }
}