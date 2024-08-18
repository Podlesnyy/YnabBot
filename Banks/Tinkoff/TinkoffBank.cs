using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Adp.Banks.Interfaces;

// ReSharper disable LoopCanBeConvertedToQuery

namespace Adp.Banks.Tinkoff;

public class TinkoffBank : IBank
{
    public bool IsItYour( string fileName ) => fileName.Contains( "operations" );

    public string FileEncoding => "utf-8";

    public List< Transaction > Parse( string fileContent )
    {
        var ret = new List< Transaction >();
        var doc = new XmlDocument();
        doc.LoadXml( fileContent );

        // Извлечение всех ACCTID узлов
        var acctidNodes = doc.GetElementsByTagName( "ACCTID" );

        foreach ( XmlNode acctidNode in acctidNodes )
        {
            var acctid = acctidNode.InnerText;

            var accountNode = acctidNode.ParentNode.ParentNode;
            var transactions = accountNode.SelectNodes( ".//STMTTRN" );

            foreach ( XmlNode node in transactions )
            {
                var dateTime = DateTime.ParseExact( node.SelectSingleNode( "DTPOSTED" )?.InnerText[ ..14 ]!, "yyyyMMddHHmmss", null );
                var memo = node.SelectSingleNode( "MEMO" )?.InnerText;
                var id = node.SelectSingleNode( "FITID" )?.InnerText;
                var sum = double.Parse( node.SelectSingleNode( "TRNAMT" )?.InnerText!, CultureInfo.InvariantCulture ) * -1;
                var payee = node.SelectSingleNode( "NAME" )?.InnerText;

                ret.Add( new Transaction( acctid, dateTime, sum, memo, 0, id, payee ) );
            }
        }

        return ret;
    }
}