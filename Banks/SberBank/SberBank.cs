using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Adp.Banks.Interfaces;
using CsvHelper;

namespace Adp.Banks.SberBank
{
    public class SberBank : IBank
    {
        private static readonly CultureInfo RussianCi = new CultureInfo("ru");

        public bool IsItYour(string fileName)
        {
            return fileName.Replace("_", " ").Contains("Операции по карте ");
        }

        public string FileEncoding => "utf-8";

        public List<Transaction> Parse(string file)
        {
            //Тип карты; Номер карты; Дата совершения операции; Дата обработки операции; Код авторизации; Тип операции; Город совершения операции; Страна совершения операции; Описание; Валюта операции; Сумма в валюте операции; Сумма в валюте счета;
            //Основная; *3325; 06.11.2019; 06.11.2019; 216156; 1; Moscow; RUS; Зачисление зарплаты; ; ; 36683,54;
            var ret = new List<Transaction>();
            using var reader = new StreamReader(file, Encoding.GetEncoding("utf-8"));
            using var csv = new CsvReader(reader, RussianCi);
            csv.Configuration.Delimiter = ";";
            csv.Configuration.HasHeaderRecord = true;
            csv.Configuration.BadDataFound = null;
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
}