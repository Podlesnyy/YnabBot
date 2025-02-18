using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;

namespace Adp.Banks.BCC;

public sealed partial class BccTxtBank : IBank
{
    private string account;

    public bool IsItYour(string fileName) => fileName.Contains("bcc1") && fileName.Contains(".txt");
    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(string fileContent)
    {
        GetAccount( fileContent );
        var transactionsText = ExtractTransactionsText( fileContent );
        var transactionsString = ExtractTransactionsString( transactionsText );

        return ConvertToTransactions( transactionsString );
    }

    private List< Transaction > ConvertToTransactions( List< string > transactionsString )
    {
        var regex = RegexConvertToTransaction();

        var ret = new List< Transaction >();
        foreach ( var transString in transactionsString )
        {
            var match = regex.Match(transString);

            if ( !match.Success )
                throw new Exception( $"Bad transaction {transString}" );

            var date = DateTime.ParseExact(match.Groups[1].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture);
            var amount = -1 * double.Parse(match.Groups[3].Value.Trim().Replace(" ", "").Replace(",", "."), CultureInfo.InvariantCulture);
            var description = match.Groups[2].Value.Trim();

            ret.Add( new Transaction(account, date, amount, description, 0, null, null) );
        }

        return ret;
    }

    private static List<string> ExtractTransactionsString( string transactionsText )
    {
        var matches = RegexTransactionLine().Matches(transactionsText);
        var ret = new List< string >();

        foreach (Match match in matches)
            ret.Add(match.Value.Trim().Replace("\r\n", " "));

        return ret;
    }

    private static string ExtractTransactionsText(string input)
    {
        var match = RegexTransaction().Match(input);
        if ( !match.Success )
            throw new Exception( "Cant extract transactions" );

        var startIndex = match.Index + match.Length;
        return input[ startIndex.. ].Trim();

    }

    private void GetAccount( string fileContent )
    {
        var match = RegexAccount().Match(fileContent);

        if ( match.Success )
            account = match.Groups[ 1 ].Value;
        else
            throw new Exception( "Cant find account in file" );

    }

    [GeneratedRegex(@"Выписка\r\nпо счету\r\n(KZ\d+)")]
    private static partial Regex RegexAccount();
    [GeneratedRegex(@"Дата Описание операции Сумма в (\w{3}) Комиссия, \1")]
    private static partial Regex RegexTransaction();
    [GeneratedRegex(@"(\d{2}\.\d{2}\.\d{4})([\s\S]*?0,00)(?=\r\n\d{2}\.\d{2}\.\d{4}|$)", RegexOptions.Singleline)]
    private static partial Regex RegexTransactionLine();
    [GeneratedRegex(@"^(\d{2}\.\d{2}\.\d{4})\s+(.+?)\s+([-\d\s,]+)\s+[-\d\s,]+$")]
    private static partial Regex RegexConvertToTransaction();
}