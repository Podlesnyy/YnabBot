using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Adp.Banks.Interfaces;
using CsvHelper;
using CsvHelper.Configuration;

namespace Adp.Banks.Citibank;

public class CitiBank : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");

    public bool IsItYour( string fileName ) => fileName.Contains( "ACCT_038" );

    public string FileEncoding => "utf-8";

    public List< Transaction > Parse( string fileContent )
    {
        var transofrm = fileContent.Replace( "\",\"", ";" ).Replace( "\"", string.Empty ).Replace( "'", string.Empty ).Replace( Encoding.UTF8.GetString( Encoding.UTF8.GetPreamble() ), string.Empty );
        var config = new CsvConfiguration( RussianCi ) { Delimiter = ";", HasHeaderRecord = false, BadDataFound = null };
        var csv = new CsvReader( new StringReader( transofrm ), config );
        var ret = new List< Transaction >();
        while ( csv.Read() )
        {
            var schet = csv.GetField< string >( 3 );
            var date = DateTime.ParseExact( csv.GetField( 0 )!, "dd/MM/yyyy", CultureInfo.InvariantCulture );

            var memo = csv.GetField< string >( 1 );
            var sum = -1 * Convert.ToDouble( csv.GetField< string >( 2 ), CultureInfo.InvariantCulture );

            ret.Add( new Transaction( schet, date, sum, memo, 0, null, null ) );
        }

        return ret;
    }
}