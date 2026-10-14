using System.Globalization;
using System.Text;

namespace Middt.Framework.Expressions.Portable;

/// <summary>Syntax error of a portable expression (<see cref="Position"/> is the 0-based character offset).</summary>
public sealed class PortableExpressionException(string message, int position)
    : Exception($"{message} (at {position.ToString(CultureInfo.InvariantCulture)})")
{
    public int Position { get; } = position;
}

internal enum TokenKind
{
    Number,
    String,
    Identifier,
    Operator,
    End,
}

internal readonly record struct Token(TokenKind Kind, string Text, int Position, double Number = 0);

/// <summary>Tokenizer of the portable expression language (see <see cref="PortableExpression"/>).</summary>
internal static class PortableLexer
{
    private static readonly string[] Operators =
        ["??", "&&", "||", "==", "!=", "<=", ">=", "<", ">", "+", "-", "*", "/", "%", "!", "?", ":", ".", ",", "(", ")", "[", "]"];

    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.'))
                {
                    i++;
                }

                var literal = text[start..i];
                if (!double.TryParse(literal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    throw new PortableExpressionException($"Invalid number '{literal}'", start);
                }

                tokens.Add(new Token(TokenKind.Number, literal, start, number));
                continue;
            }

            if (c is '"' or '\'')
            {
                tokens.Add(ReadString(text, ref i));
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Identifier, text[start..i], start));
                continue;
            }

            var op = Operators.FirstOrDefault(o => string.CompareOrdinal(text, i, o, 0, o.Length) == 0);
            if (op is null)
            {
                throw new PortableExpressionException($"Unexpected character '{c}'", i);
            }

            tokens.Add(new Token(TokenKind.Operator, op, i));
            i += op.Length;
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, text.Length));
        return tokens;
    }

    private static Token ReadString(string text, ref int i)
    {
        var quote = text[i];
        var start = i++;
        var sb = new StringBuilder();
        while (i < text.Length && text[i] != quote)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                sb.Append(text[i] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    var other => other, // \" \' \\
                });
            }
            else
            {
                sb.Append(text[i]);
            }

            i++;
        }

        if (i >= text.Length)
        {
            throw new PortableExpressionException("Unterminated string", start);
        }

        i++;
        return new Token(TokenKind.String, sb.ToString(), start);
    }
}

internal abstract record Node(int Position);

internal sealed record LiteralNode(object? Value, int Position) : Node(Position);

internal sealed record VariableNode(string Name, int Position) : Node(Position);

internal sealed record MemberNode(Node Target, string Name, int Position) : Node(Position);

internal sealed record IndexNode(Node Target, Node Index, int Position) : Node(Position);

internal sealed record CallNode(Node Target, string Method, IReadOnlyList<Node> Arguments, int Position) : Node(Position);

internal sealed record UnaryNode(string Operator, Node Operand, int Position) : Node(Position);

internal sealed record BinaryNode(string Operator, Node Left, Node Right, int Position) : Node(Position);

internal sealed record ConditionalNode(Node Condition, Node WhenTrue, Node WhenFalse, int Position) : Node(Position);

/// <summary>Recursive descent parser; rejects unknown variables and methods so typos fail at validation time.</summary>
internal sealed class PortableParser
{
    private const int MaxDepth = 64;

    private readonly List<Token> tokens;
    private readonly IReadOnlySet<string> variables;
    private int index;
    private int depth;

    private PortableParser(string text, IReadOnlySet<string> variables)
    {
        tokens = PortableLexer.Tokenize(text);
        this.variables = variables;
    }

    public static Node Parse(string text, IReadOnlySet<string> variables)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new PortableExpressionException("Expression is empty", 0);
        }

        var parser = new PortableParser(text, variables);
        var node = parser.Expression();
        if (parser.Current.Kind != TokenKind.End)
        {
            throw new PortableExpressionException($"Unexpected '{parser.Current.Text}'", parser.Current.Position);
        }

        return node;
    }

    private Token Current => tokens[index];

    private bool IsOperator(string op) => Current.Kind == TokenKind.Operator && Current.Text == op;

    private bool Accept(string op)
    {
        if (!IsOperator(op))
        {
            return false;
        }

        index++;
        return true;
    }

    private void Expect(string op)
    {
        if (!Accept(op))
        {
            throw new PortableExpressionException($"Expected '{op}'", Current.Position);
        }
    }

    private Node Expression()
    {
        if (++depth > MaxDepth)
        {
            throw new PortableExpressionException("Expression is nested too deeply", Current.Position);
        }

        try
        {
            var condition = Binary(0);
            if (IsOperator("?"))
            {
                var position = Current.Position;
                index++;
                var whenTrue = Expression();
                Expect(":");
                var whenFalse = Expression();
                return new ConditionalNode(condition, whenTrue, whenFalse, position);
            }

            return condition;
        }
        finally
        {
            depth--;
        }
    }

    // Binary operators by precedence (lowest first).
    private static readonly string[][] Levels =
    [
        ["??"],
        ["||"],
        ["&&"],
        ["==", "!="],
        ["<", "<=", ">", ">="],
        ["+", "-"],
        ["*", "/", "%"],
    ];

    private Node Binary(int level)
    {
        if (level == Levels.Length)
        {
            return Unary();
        }

        var left = Binary(level + 1);
        while (Current.Kind == TokenKind.Operator && Levels[level].Contains(Current.Text))
        {
            var op = Current;
            index++;
            left = new BinaryNode(op.Text, left, Binary(level + 1), op.Position);
        }

        return left;
    }

    private Node Unary()
    {
        if (IsOperator("!") || IsOperator("-"))
        {
            var op = Current;
            index++;
            if (++depth > MaxDepth)
            {
                throw new PortableExpressionException("Expression is nested too deeply", op.Position);
            }

            try
            {
                return new UnaryNode(op.Text, Unary(), op.Position);
            }
            finally
            {
                depth--;
            }
        }

        return Postfix(Primary());
    }

    private Node Postfix(Node node)
    {
        while (true)
        {
            if (IsOperator("."))
            {
                index++;
                if (Current.Kind != TokenKind.Identifier)
                {
                    throw new PortableExpressionException("Expected a member name", Current.Position);
                }

                var name = Current;
                index++;
                if (IsOperator("("))
                {
                    if (!PortableEvaluator.Methods.Contains(name.Text))
                    {
                        throw new PortableExpressionException($"Unknown method '{name.Text}'", name.Position);
                    }

                    node = new CallNode(node, name.Text, Arguments(), name.Position);
                }
                else
                {
                    node = new MemberNode(node, name.Text, name.Position);
                }
            }
            else if (IsOperator("["))
            {
                var position = Current.Position;
                index++;
                var key = Expression();
                Expect("]");
                node = new IndexNode(node, key, position);
            }
            else
            {
                return node;
            }
        }
    }

    private List<Node> Arguments()
    {
        Expect("(");
        var arguments = new List<Node>();
        if (!Accept(")"))
        {
            do
            {
                arguments.Add(Expression());
            }
            while (Accept(","));
            Expect(")");
        }

        return arguments;
    }

    private Node Primary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                index++;
                return new LiteralNode(token.Number, token.Position);
            case TokenKind.String:
                index++;
                return new LiteralNode(token.Text, token.Position);
            case TokenKind.Identifier:
                index++;
                switch (token.Text)
                {
                    case "true":
                        return new LiteralNode(true, token.Position);
                    case "false":
                        return new LiteralNode(false, token.Position);
                    case "null":
                        return new LiteralNode(null, token.Position);
                    case "Math":
                        return new VariableNode("Math", token.Position);
                }

                return variables.Contains(token.Text)
                    ? new VariableNode(token.Text, token.Position)
                    : throw new PortableExpressionException($"Unknown variable '{token.Text}'", token.Position);
            case TokenKind.Operator when token.Text == "(":
                index++;
                var inner = Expression();
                Expect(")");
                return inner;
            case TokenKind.End:
                throw new PortableExpressionException("Unexpected end of expression", token.Position);
            default:
                throw new PortableExpressionException($"Unexpected '{token.Text}'", token.Position);
        }
    }
}
