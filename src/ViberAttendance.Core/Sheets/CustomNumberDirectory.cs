using System.Text;
using ViberAttendance.Core.Configuration;

namespace ViberAttendance.Core.Sheets;

public static class CustomNumberDirectory
{
    public static string Key(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    public static List<CustomNumber> Normalize(IEnumerable<CustomNumber>? items)
    {
        var order = new List<string>();
        var byKey = new Dictionary<string, CustomNumber>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items ?? [])
        {
            if (item is null)
            {
                continue;
            }

            var name = item.Name?.Trim() ?? string.Empty;
            var code = item.Code?.Trim() ?? string.Empty;
            var key = Key(name);
            if (key.Length == 0 || code.Length == 0)
            {
                continue;
            }

            if (!byKey.ContainsKey(key))
            {
                order.Add(key);
            }

            byKey[key] = new CustomNumber { Name = name, Code = code };
        }

        return order.Select(key => byKey[key]).ToList();
    }

    public static List<CustomNumber> Merge(IEnumerable<CustomNumber>? current, IEnumerable<CustomNumber>? incoming) =>
        Normalize((current ?? []).Concat(incoming ?? []));

    public static Dictionary<string, string> Index(IEnumerable<CustomNumber>? items)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Normalize(items))
        {
            index[Key(item.Name)] = item.Code;
        }

        return index;
    }

    public static string Find(IReadOnlyDictionary<string, string>? index, string? name) =>
        index is not null && index.TryGetValue(Key(name), out var code) ? code : string.Empty;

    public static List<CustomNumber> Parse(string? text)
    {
        var result = new List<CustomNumber>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (!TrySplit(raw.Trim(), out var name, out var code) || IsHeader(name, code))
            {
                continue;
            }

            result.Add(new CustomNumber { Name = name, Code = code });
        }

        return result;
    }

    private static bool TrySplit(string line, out string name, out string code)
    {
        name = string.Empty;
        code = string.Empty;
        if (line.Length == 0)
        {
            return false;
        }

        if (line.Contains('\t'))
        {
            var parts = line.Split('\t');
            name = parts[0].Trim();
            code = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        }
        else if (line.Contains(',') || line.Contains('，'))
        {
            var parts = line.Split([',', '，'], 2);
            name = parts[0].Trim();
            code = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        }
        else
        {
            var split = -1;
            for (var index = line.Length - 1; index >= 0; index--)
            {
                if (char.IsWhiteSpace(line[index]))
                {
                    split = index;
                    break;
                }
            }

            if (split <= 0)
            {
                return false;
            }

            name = line[..split].Trim();
            code = line[(split + 1)..].Trim();
        }

        return name.Length > 0 && code.Length > 0;
    }

    private static bool IsHeader(string name, string code)
    {
        var nameKey = Key(name);
        var codeKey = Key(code);
        return nameKey.Equals("姓名", StringComparison.OrdinalIgnoreCase)
            || nameKey.Equals("名字", StringComparison.OrdinalIgnoreCase)
            || nameKey.Equals("名称", StringComparison.OrdinalIgnoreCase)
            || nameKey.Equals("name", StringComparison.OrdinalIgnoreCase)
            ? codeKey.Equals("自定义号码", StringComparison.OrdinalIgnoreCase)
                || codeKey.Equals("号码", StringComparison.OrdinalIgnoreCase)
                || codeKey.Equals("编号", StringComparison.OrdinalIgnoreCase)
                || codeKey.Equals("编码", StringComparison.OrdinalIgnoreCase)
                || codeKey.Equals("code", StringComparison.OrdinalIgnoreCase)
                || codeKey.Equals("number", StringComparison.OrdinalIgnoreCase)
            : false;
    }
}
