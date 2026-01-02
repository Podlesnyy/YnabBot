using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Adp.Banks.Interfaces;
using OfficeOpenXml;

namespace Adp.Banks.AlfaBank;

public class AlfaBankExcel : IBank
{
    public bool IsItYour( string fileName ) => fileName.Contains( "Statement " ) || fileName.Contains( "statement " );

    public List< Transaction > Parse( MemoryStream stream )
    {
        var ret = new List< Transaction >();

        ExcelPackage.License.SetNonCommercialPersonal( "Ynab" );

        using var package = new ExcelPackage( stream );
        var worksheet = package.Workbook.Worksheets[ 0 ];
        var rowCount = worksheet.Dimension.Rows;

        for ( var row = 2; row < rowCount; row++ ) // Предполагается, что первая строка содержит заголовки
        {
            var operationDate =
                DateTime.ParseExact( worksheet.Cells[ row, 1 ].Text, "dd.MM.yyyy", CultureInfo.InvariantCulture );
            var accountNumber = worksheet.Cells[ row, 4 ].Text;
            //var cardName = worksheet.Cells[ row, 5 ].Text;
            var cardNumber = worksheet.Cells[ row, 6 ].Text;
            var operationDescription = worksheet.Cells[ row, 7 ].Text;
            var amount = double.Parse( worksheet.Cells[ row, 8 ].Text );
            //var currency = worksheet.Cells[ row, 9 ].Text;
            //var status = worksheet.Cells[ row, 10 ].Text;
            var category = worksheet.Cells[ row, 11 ].Text;
            var mcc = string.IsNullOrEmpty( worksheet.Cells[ row, 12 ].Text )
                          ? 0
                          : int.Parse( worksheet.Cells[ row, 12 ].Text );
            var type = worksheet.Cells[ row, 13 ].Text;
            //var Comment = worksheet.Cells[ row, 14 ].Text;
            var sum = type == "Пополнение" ? amount * -1 : amount;
            var memo = $"{operationDescription}_{category}_{mcc}_{type}_{cardNumber}";
            if ( memo.Contains( "Погашение ОД                                                          Дог." ) )
                continue;

            ret.Add( new Transaction( accountNumber, operationDate, sum, memo, mcc, null, category ) );
        }

        return ret;
    }
}