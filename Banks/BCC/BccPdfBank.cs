using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;
using Aspose.Pdf;
using Aspose.Pdf.Text;

namespace Adp.Banks.BCC;

// ReSharper disable once UnusedType.Global
public sealed partial class BccPdfBank : IBank
{
    public bool IsItYour( string fileName ) => fileName.Contains( "pkg_w_mb_main" );

    public string FileEncoding => "utf-8";

    public List< Transaction > Parse( MemoryStream fileContent )
    {
        return new List<Transaction>();
    }
}