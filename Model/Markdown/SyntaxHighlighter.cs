using System;
using System.Collections.Generic;

namespace ToastFish.Model.Markdown
{
    /// <summary>高亮片段的类型。Plain 是普通文本，渲染时用正文色。</summary>
    public enum TokenKind
    {
        Plain,
        Keyword,
        String,
        Comment,
        Number,
        Function,
        Type
    }

    /// <summary>一段带类型的高亮片段，渲染器按 Kind 取色。</summary>
    public struct CodeToken
    {
        public readonly string Text;
        public readonly TokenKind Kind;

        public CodeToken(string text, TokenKind kind)
        {
            Text = text;
            Kind = kind;
        }
    }

    /// <summary>
    /// 轻量语法高亮器。不做完整的词法分析：先按语言族切出注释和字符串，再在余下的文本里认出
    /// 关键字、内置类型、数字，以及「标识符 + (」形式的函数名，够译文里那些示例代码用。
    /// 认不出的语言一律整段当纯文本返回——宁可不上色，也不要把普通文字错染成关键字。
    /// </summary>
    public static class SyntaxHighlighter
    {
        /// <summary>一个语言族的词法规则。字段为 null 表示该语言没有这种结构。</summary>
        private sealed class Syntax
        {
            public readonly string LineComment;
            public readonly string BlockOpen;
            public readonly string BlockClose;

            /// <summary>引号种类，已按长度降序排好，长引号（三引号）先匹配。</summary>
            public readonly string[] Quotes;

            public readonly HashSet<string> Keywords;
            public readonly HashSet<string> Types;

            public Syntax(string lineComment, string blockOpen, string blockClose,
                string[] quotes, HashSet<string> keywords, HashSet<string> types)
            {
                LineComment = lineComment;
                BlockOpen = blockOpen;
                BlockClose = blockClose;
                Quotes = quotes;
                if (Quotes != null)
                    Array.Sort(Quotes, (a, b) => b.Length.CompareTo(a.Length));
                Keywords = keywords;
                Types = types;
            }
        }

        private static readonly Syntax CStyle = new Syntax(
            "//", "/*", "*/",
            new[] { "\"", "'", "`" },
            Set("abstract as async await base break case catch checked class const continue "
                + "default defer delete do else enum event explicit export extends extern false "
                + "final finally for foreach func function go goto if implements implicit import in "
                + "instanceof interface is let lock namespace new nil null operator package private "
                + "protected public readonly ref return sealed sizeof static struct super switch "
                + "this throw throws try typedef typeof unchecked using var virtual volatile while yield"),
            Set("bool byte char decimal double dynamic float int long object sbyte short string "
                + "uint ulong ushort void"));

        private static readonly Syntax PythonStyle = new Syntax(
            "#", null, null,
            new[] { "\"\"\"", "'''", "\"", "'" },
            Set("and as assert async await break class continue def del elif else except False "
                + "finally for from global if import in is lambda None nonlocal not or pass raise "
                + "return True try while with yield"),
            null);

        private static readonly Syntax HashStyle = new Syntax(
            "#", null, null,
            new[] { "\"", "'" },
            Set("if then else elif fi for while do done case esac function in return export local "
                + "echo source exit set unset readonly declare true false yes no null"),
            null);

        private static readonly Syntax SqlStyle = new Syntax(
            "--", "/*", "*/",
            new[] { "'", "\"" },
            SetIgnoreCase("select from where insert into values update set delete create table drop "
                + "alter add index view join inner left right outer full on group by order having "
                + "limit offset union all distinct as and or not null is in between like exists case "
                + "when then else end primary key foreign references default unique check constraint "
                + "database if count sum avg min max asc desc"),
            SetIgnoreCase("int integer bigint smallint tinyint varchar nvarchar char text date "
                + "datetime timestamp boolean bool decimal numeric float double real blob"));

        private static readonly Syntax JsonStyle = new Syntax(
            null, null, null,
            new[] { "\"" },
            Set("true false null"),
            null);

        private static readonly Syntax MarkupStyle = new Syntax(
            null, "<!--", "-->",
            new[] { "\"", "'" },
            null,
            null);

        /// <summary>
        /// 把代码切成带类型的高亮片段。language 是围栏上的语言标识（```python 里的 python），
        /// 空或不认识时整段返回一个 Plain，调用方按普通文本渲染即可。
        /// </summary>
        public static IReadOnlyList<CodeToken> Highlight(string code, string language)
        {
            var tokens = new List<CodeToken>();
            if (string.IsNullOrEmpty(code))
                return tokens;

            Syntax syntax = Resolve(language);
            if (syntax == null)
            {
                tokens.Add(new CodeToken(code, TokenKind.Plain));
                return tokens;
            }

            int i = 0;
            int n = code.Length;
            while (i < n)
            {
                if (syntax.LineComment != null && Match(code, i, syntax.LineComment))
                {
                    int end = code.IndexOf('\n', i);
                    if (end < 0)
                        end = n;
                    tokens.Add(new CodeToken(code.Substring(i, end - i), TokenKind.Comment));
                    i = end;
                    continue;
                }

                if (syntax.BlockOpen != null && Match(code, i, syntax.BlockOpen))
                {
                    int close = code.IndexOf(syntax.BlockClose, i + syntax.BlockOpen.Length,
                        StringComparison.Ordinal);
                    int end = close < 0 ? n : close + syntax.BlockClose.Length;
                    tokens.Add(new CodeToken(code.Substring(i, end - i), TokenKind.Comment));
                    i = end;
                    continue;
                }

                string quote = MatchQuote(syntax, code, i);
                if (quote != null)
                {
                    int end = ScanString(code, i, quote);
                    tokens.Add(new CodeToken(code.Substring(i, end - i), TokenKind.String));
                    i = end;
                    continue;
                }

                char c = code[i];
                if (IsIdentStart(c))
                {
                    int start = i;
                    i++;
                    while (i < n && IsIdentPart(code[i]))
                        i++;
                    string word = code.Substring(start, i - start);
                    TokenKind kind = Classify(word, syntax);
                    // 关键字/类型后面也可能跟括号（if(...)），只有普通标识符才算函数名
                    if (kind == TokenKind.Plain && NextIsOpenParen(code, i))
                        kind = TokenKind.Function;
                    tokens.Add(new CodeToken(word, kind));
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i;
                    i++;
                    while (i < n && IsNumberPart(code, i, start))
                        i++;
                    tokens.Add(new CodeToken(code.Substring(start, i - start), TokenKind.Number));
                    continue;
                }

                tokens.Add(new CodeToken(code[i].ToString(), TokenKind.Plain));
                i++;
            }

            return Merge(tokens);
        }

        /// <summary>相邻的同类型片段并成一个，少生成一堆 Run。</summary>
        private static List<CodeToken> Merge(List<CodeToken> tokens)
        {
            var merged = new List<CodeToken>(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                CodeToken token = tokens[i];
                int last = merged.Count - 1;
                if (last >= 0 && merged[last].Kind == token.Kind)
                    merged[last] = new CodeToken(merged[last].Text + token.Text, token.Kind);
                else
                    merged.Add(token);
            }
            return merged;
        }

        /// <summary>语言标识映射到词法规则。只认第一个词，```python title=xx 这种带参数的也认。</summary>
        private static Syntax Resolve(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
                return null;

            string name = language.Trim().ToLowerInvariant();
            int stop = name.IndexOfAny(new[] { ' ', '\t', '{', ';' });
            if (stop >= 0)
                name = name.Substring(0, stop);

            switch (name)
            {
                case "c":
                case "h":
                case "cpp":
                case "c++":
                case "cc":
                case "hpp":
                case "cs":
                case "csharp":
                case "java":
                case "js":
                case "javascript":
                case "jsx":
                case "ts":
                case "typescript":
                case "tsx":
                case "go":
                case "golang":
                case "rust":
                case "rs":
                case "php":
                case "swift":
                case "kt":
                case "kotlin":
                case "scala":
                case "dart":
                case "groovy":
                case "objc":
                case "objective-c":
                    return CStyle;

                case "py":
                case "python":
                case "python2":
                case "python3":
                    return PythonStyle;

                case "sh":
                case "bash":
                case "shell":
                case "zsh":
                case "console":
                case "yaml":
                case "yml":
                case "toml":
                case "ini":
                case "conf":
                case "powershell":
                case "ps1":
                case "bat":
                case "cmd":
                case "dockerfile":
                case "makefile":
                case "ruby":
                case "rb":
                case "perl":
                case "pl":
                    return HashStyle;

                case "sql":
                case "mysql":
                case "postgresql":
                case "postgres":
                case "sqlite":
                case "tsql":
                    return SqlStyle;

                case "json":
                case "jsonc":
                    return JsonStyle;

                case "html":
                case "xml":
                case "xaml":
                case "svg":
                case "vue":
                    return MarkupStyle;

                default:
                    return null;
            }
        }

        private static TokenKind Classify(string word, Syntax syntax)
        {
            if (syntax.Types != null && syntax.Types.Contains(word))
                return TokenKind.Type;
            if (syntax.Keywords != null && syntax.Keywords.Contains(word))
                return TokenKind.Keyword;
            return TokenKind.Plain;
        }

        /// <summary>从 i 处开始匹配引号，返回引号本身；不在引号起点返回 null。</summary>
        private static string MatchQuote(Syntax syntax, string code, int i)
        {
            if (syntax.Quotes == null)
                return null;
            for (int q = 0; q < syntax.Quotes.Length; q++)
            {
                if (Match(code, i, syntax.Quotes[q]))
                    return syntax.Quotes[q];
            }
            return null;
        }

        /// <summary>
        /// 从引号起点扫到闭合引号，返回结束位置（含闭合引号）。单字符引号不跨行，扫到行尾就收；
        /// 三引号和反引号（JS 模板串）可以跨行。反斜杠转义跳过下一个字符。
        /// </summary>
        private static int ScanString(string code, int start, string quote)
        {
            bool multiline = quote.Length >= 3 || quote == "`";
            int i = start + quote.Length;
            while (i < code.Length)
            {
                char c = code[i];
                if (c == '\\' && i + 1 < code.Length)
                {
                    i += 2;
                    continue;
                }
                if (Match(code, i, quote))
                    return i + quote.Length;
                if (c == '\n' && !multiline)
                    return i;
                i++;
            }
            return code.Length;
        }

        private static bool Match(string code, int i, string token)
        {
            return i + token.Length <= code.Length
                && string.CompareOrdinal(code, i, token, 0, token.Length) == 0;
        }

        /// <summary>标识符后面（跳过空格）紧跟左括号，当成函数名。</summary>
        private static bool NextIsOpenParen(string code, int i)
        {
            while (i < code.Length && (code[i] == ' ' || code[i] == '\t'))
                i++;
            return i < code.Length && code[i] == '(';
        }

        private static bool IsIdentStart(char c)
        {
            return char.IsLetter(c) || c == '_' || c == '$';
        }

        private static bool IsIdentPart(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '$';
        }

        /// <summary>
        /// 数字里可以带小数点、下划线分隔符、十六进制字母；e/E 后面的正负号也算数字的一部分
        /// （1e-3），别的地方的 +- 不算，免得把 1+2 吃成一个数。
        /// </summary>
        private static bool IsNumberPart(string code, int i, int start)
        {
            char c = code[i];
            if (char.IsLetterOrDigit(c) || c == '.' || c == '_')
                return true;
            return (c == '+' || c == '-') && i > start
                && (code[i - 1] == 'e' || code[i - 1] == 'E');
        }

        private static HashSet<string> Set(string words)
        {
            return new HashSet<string>(
                words.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
        }

        private static HashSet<string> SetIgnoreCase(string words)
        {
            return new HashSet<string>(
                words.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
