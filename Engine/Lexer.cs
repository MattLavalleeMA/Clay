using System.Globalization;
using System.Collections.ObjectModel;

namespace Clay.Engine;

public sealed class ParseException(string message) : Exception(message);
public sealed class EvalException(string message) : Exception(message);

public enum Tk { Num, Ident, Sym, Op, End }

public readonly record struct Token(Tk Kind, string Text, double Num = 0);

public static class Lexer
{
    private const string CurrencySymbols = "$€£¥₹₩";
    private const string Operators = "+-*/^(),%=![]";

    public static IReadOnlyDictionary<string, string> Shortcuts { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "what", "" },
            { "is", "=" }
        });

    public static List<Token> Tokenize(string source)
    {
        var s = source.Replace('×', '*').Replace('÷', '/').Replace('−', '-').Replace('–', '-');
        var list = new List<Token>();
        int i = 0;

        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (IsDigit(c) || (c == '.' && i + 1 < s.Length && IsDigit(s[i + 1])))
            {
                list.Add(ReadNumber(s, ref i));
            }
            else if (IsIdentStart(c))
            {
                // After a divide, m/m/s reads as (m/m)/s so the unit isn't joined to the divisor
                list.Add(ReadIdent(s, ref i, list.Count > 0 && list[^1] is { Kind: Tk.Op, Text: "/" }));
            }
            else if (CurrencySymbols.IndexOf(c) >= 0)
            {
                list.Add(new Token(Tk.Sym, c.ToString()));
                i++;
            }
            else if (Operators.IndexOf(c) >= 0)
            {
                list.Add(new Token(Tk.Op, c.ToString()));
                i++;
            }
            else if (c == '·')
            {
                list.Add(new Token(Tk.Op, "*"));
                i++;
            }
            else
            {
                throw new ParseException($"Unexpected '{c}'");
            }
        }

        // Second pass: expand shortcuts
        var expanded = new List<Token>();

        foreach (var token in list)
        {
            if (token.Kind == Tk.Ident &&
                Shortcuts.TryGetValue(token.Text, out var replacement))
            {
                if (string.IsNullOrWhiteSpace(replacement))
                    continue; // Empty shortcut removes the token

                var replacementTokens = Tokenize(replacement);

                foreach (var t in replacementTokens)
                {
                    if (t.Kind != Tk.End)
                        expanded.Add(t);
                }
            }
            else
            {
                expanded.Add(token);
            }
        }

        expanded.Add(new Token(Tk.End, ""));
        return expanded;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';
    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '°';
    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '°' || Superscripts.IndexOf(c) >= 0;

    private const string Superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹⁻";



    private static Token ReadNumber(string s, ref int i)
    {
        int start = i;

        // 0x1F, 0b1010, and 0o17 literals
        if (s[i] == '0' && i + 2 < s.Length)
        {
            char p = char.ToLowerInvariant(s[i + 1]);
            if (p == 'x' && Uri.IsHexDigit(s[i + 2]))
                return ReadBase(s, ref i, start, 16, Uri.IsHexDigit);
            if (p == 'b' && (s[i + 2] == '0' || s[i + 2] == '1'))
                return ReadBase(s, ref i, start, 2, ch => ch == '0' || ch == '1');
            if (p == 'o' && s[i + 2] is >= '0' and <= '7')
                return ReadBase(s, ref i, start, 8, ch => ch is >= '0' and <= '7');
        }

        while (i < s.Length && (IsDigit(s[i]) || s[i] == '_')) i++;
        if (i + 1 < s.Length && s[i] == '.' && IsDigit(s[i + 1]))
        {
            i++;
            while (i < s.Length && IsDigit(s[i])) i++;
        }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            if (j < s.Length && (s[j] == '+' || s[j] == '-')) j++;
            if (j < s.Length && IsDigit(s[j]))
            {
                i = j;
                while (i < s.Length && IsDigit(s[i])) i++;
            }
        }

        var text = s[start..i];
        double.TryParse(text.Replace("_", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
        return new Token(Tk.Num, text, value);
    }

    private static Token ReadBase(string s, ref int i, int start, int radix, Func<char, bool> isDigit)
    {
        i += 2;
        double value = 0;
        while (i < s.Length && isDigit(s[i]))
        {
            value = value * radix + Uri.FromHex(s[i]);
            i++;
        }
        return new Token(Tk.Num, s[start..i], value);
    }

    private static Token ReadIdent(string s, ref int i, bool afterDivide)
    {
        int start = i++;
        while (i < s.Length && IsIdentPart(s[i])) i++;
        var word = s[start..i];
        if (afterDivide) return new Token(Tk.Ident, word);

        // Unit expressions such as km/h, m/s², kg·m/s², and m/s^2 (a power binds to the unit before it)
        int end = i;
        var expr = WithPowers(word, s, ref end);
        while (end + 1 < s.Length && (s[end] == '/' || s[end] == '·') && IsIdentStart(s[end + 1]))
        {
            int j = end + 1;
            while (j < s.Length && IsIdentPart(s[j])) j++;
            var factor = WithPowers(s[(end + 1)..j], s, ref j);
            if (!UnitTable.IsFactor(factor)) break;

            expr += s[end] + factor;
            end = j;

            // Keep scanning past a prefix that cancels (m/m/s) and settle on the last valid unit
            if (UnitTable.Parse(expr) != null)
            {
                word = expr;
                i = end;
            }
        }

        return new Token(Tk.Ident, word);
    }

    /// <summary>Applies every caret power at i to the factor, so s^2^2 and s²^2 are both s⁴.</summary>
    private static string WithPowers(string factor, string s, ref int i)
    {
        while (ReadPower(s, ref i) is { Length: > 0 } power) factor = WithPower(factor, power);
        return factor;
    }

    /// <summary>Appends a caret power as superscripts, multiplying any exponent the factor already has (s²^2 is s⁴).</summary>
    private static string WithPower(string factor, string power)
    {
        if (power.Length == 0) return factor;

        int split = factor.Length;
        while (split > 0 && Superscripts.IndexOf(factor[split - 1]) >= 0) split--;
        if (split == 0 || split == factor.Length) return factor + power;

        if (!int.TryParse(FromSuperscripts(factor[split..]), out int a) ||
            !int.TryParse(FromSuperscripts(power), out int b))
            return factor + power;

        return factor[..split] + ToSuperscripts(a * b);
    }

    private static string FromSuperscripts(string s) =>
        string.Concat(s.Select(c => c == '⁻' ? '-' : (char)('0' + Superscripts.IndexOf(c))));

    private static string ToSuperscripts(int n) =>
        string.Concat(n.ToString(CultureInfo.InvariantCulture).Select(c => c == '-' ? '⁻' : Superscripts[c - '0']));

    /// <summary>Reads "^2" or "^-1" at i as superscripts; returns "" and leaves i alone otherwise.</summary>
    private static string ReadPower(string s, ref int i)
    {
        if (i >= s.Length || s[i] != '^') return "";
        int j = i + 1;
        bool negative = j < s.Length && s[j] == '-';
        if (negative) j++;
        int digits = j;
        while (j < s.Length && IsDigit(s[j])) j++;
        if (j == digits) return "";

        var sup = (negative ? "⁻" : "") + string.Concat(s[digits..j].Select(c => Superscripts[c - '0']));
        i = j;
        return sup;
    }
}
