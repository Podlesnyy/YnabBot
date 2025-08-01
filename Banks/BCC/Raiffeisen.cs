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
    private static readonly CultureInfo RussianCi = new( "ru" );

    // ReSharper disable once UnusedMember.Global
    public Raiffeisen() : this( "NeverBeSuchFile" )
    {
    }

    public bool IsItYour( string fileName ) => fileName.Contains( $"{id}_account_statement_" );

    public string FileEncoding => "utf-8";

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
            var transId = csv.GetField< string >( 2 );
            if ( transId == string.Empty )
                transId = null;

            var postup = csv.GetField< string >( 3 );
            var rasxod = csv.GetField< string >( 4 );
            var sumStr = postup == string.Empty ? rasxod : $"-{postup}";
            var sum = Convert.ToDouble( sumStr, RussianCi );

            var memo = csv.GetField< string >( 6 );
            var card = csv.GetField< string >( 7 );


            ret.Add( new Transaction( raifSchet, date, sum, $"{memo}. Номер карты:{card}", 0, transId, memo ) );
        }

        return ret;
    }
}