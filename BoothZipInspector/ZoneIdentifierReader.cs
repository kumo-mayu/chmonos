using BoothZipInspector.Models;

namespace BoothZipInspector;

/// <summary>
/// Zone.Identifier(INI形式)のテキスト内容を解析する純粋ロジック。
/// ファイルアクセスに依存しないため単体テストしやすい。
/// </summary>
public static class ZoneIdentifierParser
{
    public static ZoneIdentifierInfo Parse(string content)
    {
        string? zoneId = null;
        string? referrerUrl = null;
        string? hostUrl = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('[') || line.StartsWith(';'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (key.Equals("ZoneId", StringComparison.OrdinalIgnoreCase))
            {
                zoneId = value;
            }
            else if (key.Equals("ReferrerUrl", StringComparison.OrdinalIgnoreCase))
            {
                referrerUrl = value;
            }
            else if (key.Equals("HostUrl", StringComparison.OrdinalIgnoreCase))
            {
                hostUrl = value;
            }
        }

        var itemId = BoothUrlExtractor.TryExtractItemId(referrerUrl)
            ?? BoothUrlExtractor.TryExtractItemId(hostUrl);

        return new ZoneIdentifierInfo
        {
            Found = true,
            ZoneId = zoneId,
            ReferrerUrl = referrerUrl,
            HostUrl = hostUrl,
            BoothItemId = itemId,
        };
    }
}

/// <summary>
/// NTFS代替データストリーム "ファイルパス:Zone.Identifier" を実際に読み取る。
/// NTFS以外や、ストリームが存在しない場合はFoundがfalseのZoneIdentifierInfoを返す。
/// </summary>
public static class ZoneIdentifierReader
{
    public static ZoneIdentifierInfo Read(string filePath)
    {
        var adsPath = filePath + ":Zone.Identifier";

        try
        {
            using var stream = new FileStream(adsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            return ZoneIdentifierParser.Parse(content);
        }
        catch (FileNotFoundException)
        {
            return ZoneIdentifierInfo.NotFound();
        }
        catch (DirectoryNotFoundException)
        {
            return ZoneIdentifierInfo.NotFound();
        }
        catch (IOException)
        {
            return ZoneIdentifierInfo.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return ZoneIdentifierInfo.NotFound();
        }
        catch (NotSupportedException)
        {
            return ZoneIdentifierInfo.NotFound();
        }
    }
}
