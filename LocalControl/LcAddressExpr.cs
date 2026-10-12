namespace EssSimulator.LocalControl
{
    /// <summary>
    /// LC 点表地址表达式：整数算术与组序号 <c>n</c>（从 1 计）。
    /// 含 <c>n</c> 的为组级，按组复制；纯数字为单元级，只出现一次。
    /// </summary>
    internal static class LcAddressExpr
    {
        public static LcCompiledExpr Compile(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("地址表达式为空");

            var parser = new Parser(text);
            var node = parser.ParseExpression();
            parser.ExpectEnd();
            return new LcCompiledExpr(node);
        }

        public static int Evaluate(string text, int n) => Compile(text).Evaluate(n);

        public static bool IsGroupScoped(string text) => Compile(text).DependsOnN;

        private sealed class Parser
        {
            private readonly string _text;
            private int _i;

            public Parser(string text)
            {
                _text = text;
                _i = 0;
                SkipWs();
            }

            public ExprNode ParseExpression()
            {
                var left = ParseTerm();
                while (true)
                {
                    SkipWs();
                    if (Match('+'))
                    {
                        SkipWs();
                        left = new BinaryNode(BinaryOp.Add, left, ParseTerm());
                        continue;
                    }
                    if (Match('-'))
                    {
                        SkipWs();
                        left = new BinaryNode(BinaryOp.Sub, left, ParseTerm());
                        continue;
                    }
                    return left;
                }
            }

            private ExprNode ParseTerm()
            {
                var left = ParseUnary();
                while (true)
                {
                    SkipWs();
                    if (Match('*'))
                    {
                        SkipWs();
                        left = new BinaryNode(BinaryOp.Mul, left, ParseUnary());
                        continue;
                    }
                    if (Match('/'))
                    {
                        SkipWs();
                        left = new BinaryNode(BinaryOp.Div, left, ParseUnary());
                        continue;
                    }
                    return left;
                }
            }

            private ExprNode ParseUnary()
            {
                SkipWs();
                if (Match('-'))
                {
                    SkipWs();
                    return new NegNode(ParseUnary());
                }
                return ParsePrimary();
            }

            private ExprNode ParsePrimary()
            {
                SkipWs();
                if (Match('('))
                {
                    var inner = ParseExpression();
                    SkipWs();
                    if (!Match(')'))
                        throw new FormatException($"地址表达式缺少右括号: {_text}");
                    return inner;
                }

                if (Peek() == 'n' || Peek() == 'N')
                {
                    if (_i + 1 < _text.Length && char.IsLetterOrDigit(_text[_i + 1]))
                        throw new FormatException($"地址表达式含未知标识符: {_text}");
                    _i++;
                    return NNode.Instance;
                }

                if (char.IsDigit(Peek()))
                {
                    int start = _i;
                    while (_i < _text.Length && char.IsDigit(_text[_i]))
                        _i++;
                    return new LitNode(int.Parse(_text.AsSpan(start, _i - start)));
                }

                throw new FormatException($"地址表达式无法解析: {_text}");
            }

            public void ExpectEnd()
            {
                SkipWs();
                if (_i < _text.Length)
                    throw new FormatException($"地址表达式有多余字符: {_text}");
            }

            private char Peek() => _i < _text.Length ? _text[_i] : '\0';

            private bool Match(char c)
            {
                if (Peek() != c)
                    return false;
                _i++;
                return true;
            }

            private void SkipWs()
            {
                while (_i < _text.Length && char.IsWhiteSpace(_text[_i]))
                    _i++;
            }
        }

        internal abstract class ExprNode
        {
            public abstract bool DependsOnN { get; }
            public abstract int Eval(int n);
        }

        private sealed class LitNode : ExprNode
        {
            private readonly int _value;
            public LitNode(int value) => _value = value;
            public override bool DependsOnN => false;
            public override int Eval(int n) => _value;
        }

        private sealed class NNode : ExprNode
        {
            public static readonly NNode Instance = new();
            public override bool DependsOnN => true;
            public override int Eval(int n) => n;
        }

        private enum BinaryOp { Add, Sub, Mul, Div }

        private sealed class BinaryNode : ExprNode
        {
            private readonly BinaryOp _op;
            private readonly ExprNode _left;
            private readonly ExprNode _right;

            public BinaryNode(BinaryOp op, ExprNode left, ExprNode right)
            {
                _op = op;
                _left = left;
                _right = right;
            }

            public override bool DependsOnN => _left.DependsOnN || _right.DependsOnN;

            public override int Eval(int n)
            {
                int a = _left.Eval(n);
                int b = _right.Eval(n);
                return _op switch
                {
                    BinaryOp.Add => a + b,
                    BinaryOp.Sub => a - b,
                    BinaryOp.Mul => a * b,
                    BinaryOp.Div => b == 0
                        ? throw new DivideByZeroException("地址表达式除数为 0")
                        : a / b,
                    _ => throw new InvalidOperationException()
                };
            }
        }

        private sealed class NegNode : ExprNode
        {
            private readonly ExprNode _inner;
            public NegNode(ExprNode inner) => _inner = inner;
            public override bool DependsOnN => _inner.DependsOnN;
            public override int Eval(int n) => checked(-_inner.Eval(n));
        }
    }

    internal sealed class LcCompiledExpr
    {
        private readonly LcAddressExpr.ExprNode _root;

        internal LcCompiledExpr(LcAddressExpr.ExprNode root)
        {
            _root = root;
            DependsOnN = root.DependsOnN;
        }

        public bool DependsOnN { get; }

        public int Evaluate(int n) => _root.Eval(n);
    }

    /// <summary>ParamName 模板：<c>param{4 + 28*(n-1)}</c> 中花括号内为地址表达式。</summary>
    internal static class LcParamNameExpr
    {
        public static string Evaluate(string template, int n)
        {
            if (string.IsNullOrWhiteSpace(template))
                throw new FormatException("ParamName 为空");

            return ReplaceBraces(template, expr => LcAddressExpr.Evaluate(expr, n).ToString());
        }

        public static bool IsGroupScoped(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
                return false;

            bool scoped = false;
            ReplaceBraces(template, expr =>
            {
                if (LcAddressExpr.IsGroupScoped(expr))
                    scoped = true;
                return "0";
            });
            return scoped;
        }

        private static string ReplaceBraces(string template, Func<string, string> replaceExpr)
        {
            var result = new System.Text.StringBuilder();
            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf('{', i);
                if (open < 0)
                {
                    result.Append(template.AsSpan(i));
                    break;
                }

                result.Append(template.AsSpan(i, open - i));
                int close = template.IndexOf('}', open + 1);
                if (close < 0)
                    throw new FormatException($"ParamName 缺少右花括号: {template}");

                var inner = template.Substring(open + 1, close - open - 1);
                result.Append(replaceExpr(inner));
                i = close + 1;
            }
            return result.ToString();
        }
    }
}
