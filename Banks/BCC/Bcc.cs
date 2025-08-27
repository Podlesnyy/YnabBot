using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adp.Banks.Interfaces;

namespace Adp.Banks.BCC;

public class BccBank : IBank
{
    private static readonly CultureInfo RussianCi = new( "ru" );

    private readonly string fileNamePart;
    private readonly string ynabAccount;

    // ReSharper disable once UnusedMember.Global
    // Need for autofac
    public BccBank()
    {
        fileNamePart = "NeverBeSuchFile";
    }

    protected BccBank( string fileNamePart, string ynabAccount )
    {
        this.fileNamePart = fileNamePart;
        this.ynabAccount = ynabAccount;
    }

    public bool IsItYour( string fileName ) => fileName.Contains( fileNamePart );

    public string FileEncoding => "utf-8";

    public List< Transaction > Parse( string fileContent ) =>
        ( from t in fileContent.Split( "statementlogo" ).Skip( 1 )
          select t.Split( Environment.NewLine )
          into transList
          let memo = transList[ 1 ].Replace( "\r", string.Empty ).Replace( "\n", string.Empty )
          let date =
              DateTime.Parse( transList[ 2 ], RussianCi )
          let sumStr = ClearTransactionString( transList[ 3 ] )
          let sum = -1 * Convert.ToDouble( sumStr, CultureInfo.InvariantCulture )
          select new Transaction( ynabAccount, date, sum, memo, 0, null, null ) ).ToList();

    private static string ClearTransactionString( string trans )
    {
        var ret = RemoveSymbol( trans, " " );
        ret = RemoveSymbol( ret, "₸" );
        ret = RemoveSymbol( ret, "$" );
        ret = RemoveSymbol( ret, "€" );
        ret = RemoveSymbol( ret, "₽" );
        return ret;
    }

    private static string RemoveSymbol( string from, string symbol ) => from.Replace( symbol, string.Empty );
}