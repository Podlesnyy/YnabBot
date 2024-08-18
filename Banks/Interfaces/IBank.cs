using System.Collections.Generic;
using System.IO;

namespace Adp.Banks.Interfaces;

public interface IBank
{
    string FileEncoding => "windows-1251";
    bool IsItYour( string fileName );

    List< Transaction > Parse( string fileContent ) => null;

    List< Transaction > Parse( MemoryStream stream ) => null;
}