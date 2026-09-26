using System.Text;

namespace Integrations.Infrastructure;

/// <summary>Small RFC 4180 reader (quoted fields, escaped quotes, embedded newlines) with size limits.</summary>
internal static class Csv
{
    public const int MaxRows = 200_000;

    public static IEnumerable<string[]> Read(TextReader reader, char separator = ',')
    {
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        var rows = 0;
        int c;
        while ((c = reader.Read()) != -1)
        {
            var ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        field.Append('"');
                        reader.Read();
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == separator)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }

                row.Add(field.ToString());
                field.Clear();
                if (row.Count > 1 || row[0].Length > 0)
                {
                    if (++rows > MaxRows)
                    {
                        throw new InvalidDataException("CSV has too many rows.");
                    }

                    yield return row.ToArray();
                }

                row.Clear();
            }
            else
            {
                field.Append(ch);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row.ToArray();
        }
    }
}
