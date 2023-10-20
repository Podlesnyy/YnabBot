﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adp.Banks.Interfaces;

namespace Adp.Banks.BCC;

public class BccBank : IBank
{
    private readonly string fileNamePart;
    private readonly string ynabAccount;

    public BccBank()
    {
        fileNamePart = "NeverBeSuchFile";
    }
    protected BccBank(string fileNamePart, string ynabAccount)
    {
        this.fileNamePart = fileNamePart;
        this.ynabAccount = ynabAccount;
    }
    private static readonly CultureInfo RussianCi = new("ru");

    public bool IsItYour(string fileName) => fileName.Contains(fileNamePart);
    public string FileEncoding => "utf-8";
    public List<Transaction> Parse(string fileContent)
    {
        var ret = new List<Transaction>();
        var list = fileContent.Split("statementlogo").Skip(1).ToList();
        foreach (var t in list)
        {
            var transList = t.Split(Environment.NewLine);
            var memo = transList[1];
            var date = DateTime.Parse(transList[2], RussianCi);
            var sumStr = ClearTransactionString( transList[3] );
            var sum = -1 * Convert.ToDouble(sumStr, CultureInfo.InvariantCulture);
            if (transList.Length > 5)
            {
                var cashbackSumStr = ClearTransactionString(transList[4]).Replace("Кешбэк:", string.Empty);
                var cashbackSum = -1 * Convert.ToDouble(cashbackSumStr, CultureInfo.InvariantCulture);
                ret.Add(new Transaction(ynabAccount, date, cashbackSum, memo, 0, null, "Cashback"));
            }

            ret.Add(new Transaction(ynabAccount, date, sum, memo, 0, null, null));
        }

        return ret;
    }

    private string ClearTransactionString(string trans)
    {
        var ret = RemoveSymbol(trans, " ");
        ret = RemoveSymbol(ret, "₸");
        ret = RemoveSymbol(ret, "$");
        ret = RemoveSymbol(ret, "€");
        ret = RemoveSymbol(ret, "₽");
        return ret;
    }

    private string RemoveSymbol(string from, string symbol)
    {
        return from.Replace(symbol, string.Empty);
    }

}

public class BccTenge : BccBank
{
    public BccTenge() : base("bccirontenge", "bccirontenge")
    {
    }
}

public class BccKartaKarta : BccBank
{
    public BccKartaKarta() : base("bcckartakarta", "bcckartakarta")
    {
    }
}

public class BccUsd : BccBank
{
    public BccUsd() : base("bccironusd", "bccironusd")
    {
    }
}

public class BccEuro : BccBank
{
    public BccEuro() : base("bccironeuro", "bccironeuro")
    {
    }
}

public class BccRub : BccBank
{
    public BccRub() : base("bccironrub", "bccironrub")
    {
    }
}