using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Adp.Banks.Interfaces;
using CsvHelper;
using CsvHelper.Configuration;

namespace Adp.Banks.BCC;

// ReSharper disable once UnusedType.Global
public class Raiffeisen( string id ) : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");

    // ReSharper disable once UnusedMember.Global
    public Raiffeisen() : this( "NeverBeSuchFile" )
    {
    }

    public bool IsItYour( string fileName ) => fileName.Contains( $"{id}_account_statement_" );

    public string FileEncoding => "windows-1251";

    public List< Transaction > Parse( string fileContent )
    {
        var ret = new List< Transaction >();
        var config = new CsvConfiguration( RussianCi ) { Delimiter = ";", HasHeaderRecord = true, BadDataFound = null };
        var csv = new CsvReader( new StringReader( fileContent ), config );

        csv.Read();
        while ( csv.Read() )
        {
            var raifSchet = $"raiffeisen_{id}";
            var date = csv.GetField< DateTime >( 0 );
            var memo = csv.GetField< string >( 1 );
            var sum = -1 * Convert.ToDouble( csv.GetField< string >( 5 ).Replace( " ", string.Empty ), CultureInfo.InvariantCulture );

            ret.Add( new Transaction( raifSchet, date, sum, memo, 0, null, null ) );
        }

        return ret;
    }
}