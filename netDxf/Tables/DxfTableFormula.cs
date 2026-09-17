// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace netDxf.Tables
{
    /// <summary>A bounded, immutable arithmetic table formula. No script, reflection or external field evaluation is performed.</summary>
    /// <remarks>
    /// Supports invariant numbers, A1 references, rectangular ranges in SUM/AVERAGE/COUNT/MIN/MAX,
    /// unary signs, parentheses, +, -, *, / and right-associative ^. Exponentiation binds before
    /// unary minus. Aggregates, including COUNT, ignore empty/text referenced cells.
    /// COUNT counts numeric results, so referenced formulas are evaluated and cycles still reject. Empty SUM is zero;
    /// empty AVERAGE/MIN/MAX reject. Strings are never parsed as numbers or formulas.
    /// </remarks>
    public sealed class DxfTableFormula
    {
        /// <summary>Maximum formula length, including the leading equals sign.</summary>
        public const int MaximumLength = 4096;
        /// <summary>Maximum syntactic and formula dependency depth.</summary>
        public const int MaximumDepth = 128;
        /// <summary>Maximum sum of expression depths across concurrently evaluated formula dependencies.</summary>
        /// <remarks>This conservative stack budget is shared by the whole calculation, separately from the dependency-count and operation limits.</remarks>
        public const int MaximumEvaluationDepth = 1024;
        private readonly Node root;
        private bool scalarOnly;
        private DxfTableFormula(string expression, Node root) { this.Expression = expression; this.root = root; }
        /// <summary>Gets the unmodified expression.</summary>
        public string Expression { get; }
        /// <summary>Compiles a complete formula starting with equals. Unknown functions and trailing input reject.</summary>
        public static DxfTableFormula Parse(string expression)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (expression.Length < 2 || expression.Length > MaximumLength || expression[0] != '=')
                throw new FormatException("A bounded table formula must start with equals.");
            var parser = new Parser(expression);
            Node root = parser.Expression();
            parser.End();
            return new DxfTableFormula(expression, root);
        }
        /// <summary>Evaluates a compiled formula against explicitly supplied, bounded table cells.</summary>
        /// <remarks>The resolver may return null, string, int or finite double. Calls are memoized per address for this evaluation.</remarks>
        public double Evaluate(int rowCount, int columnCount, Func<DxfTableCellAddress, object> resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            return this.Evaluate(new Evaluation(rowCount, columnCount, resolver));
        }
        /// <summary>Compiles numeric arithmetic and literal aggregates without cell/range references.</summary>
        /// <remarks>Shares table-formula syntax, arithmetic and budgets, but never invokes a cell resolver.</remarks>
        public static DxfTableFormula ParseScalar(string expression)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (expression.Length < 2 || expression.Length > MaximumLength || expression[0] != '=')
                throw new FormatException("A bounded scalar formula must start with equals.");
            var parser = new Parser(expression, false);
            Node node = parser.Expression(); parser.End();
            return new DxfTableFormula(expression, node) { scalarOnly = true };
        }
        /// <summary>Evaluates a formula compiled by ParseScalar without implicit cell access.</summary>
        public double EvaluateScalar()
        {
            if (!this.scalarOnly) throw new InvalidOperationException("Compile with ParseScalar before scalar evaluation.");
            return this.Evaluate(new Evaluation(1, 1, _ => { throw new InvalidOperationException("A scalar formula cannot read cells."); }));
        }
        internal double Evaluate(Evaluation evaluation)
        {
            evaluation.EnterExpression(this.root.Depth);
            try { return Number(this.root.Evaluate(evaluation)); }
            finally { evaluation.LeaveExpression(this.root.Depth); }
        }
        internal static double Number(object value)
        {
            if (value is int integer) return integer;
            if (value is double number) return Finite(number);
            throw new InvalidOperationException("An arithmetic operand must be an int or finite double; empty and text cells cannot be coerced.");
        }
        internal static double Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArithmeticException("Table formula result is not finite.");
            return value == 0 ? 0 : value;
        }
        internal sealed class Evaluation
        {
            private readonly Func<DxfTableCellAddress, object> resolver;
            private readonly Dictionary<DxfTableCellAddress, object> cache = new Dictionary<DxfTableCellAddress, object>();
            private int remaining = 1000000;
            private int expressionDepth;
            internal void EnterExpression(int depth)
            {
                if (depth > MaximumEvaluationDepth - this.expressionDepth)
                    throw new InvalidOperationException("Combined expression and dependency depth exceeds the evaluation stack budget.");
                this.expressionDepth += depth;
            }
            internal void LeaveExpression(int depth) { this.expressionDepth -= depth; }
            internal Evaluation(int rows, int columns, Func<DxfTableCellAddress, object> resolver)
            {
                if (rows < 1 || columns < 1 || (long)rows * columns > 1000000) throw new ArgumentOutOfRangeException(nameof(rows), "A calculation is limited to one million cells.");
                this.Rows = rows; this.Columns = columns; this.resolver = resolver;
            }
            internal int Rows { get; }
            internal int Columns { get; }
            internal void Step() { if (--this.remaining < 0) throw new InvalidOperationException("Table calculation exceeded its one-million-operation budget."); }
            internal void Check(DxfTableCellAddress address)
            {
                if (address.Row >= this.Rows || address.Column >= this.Columns) throw new ArgumentOutOfRangeException(nameof(address), "Table formula reference is outside the grid: " + address);
            }
            internal object Read(DxfTableCellAddress address)
            {
                this.Step(); this.Check(address);
                if (!this.cache.TryGetValue(address, out object value))
                {
                    value = this.resolver(address);
                    if (value != null && !(value is string) && !(value is int) && !(value is double)) throw new NotSupportedException("A formula cell has an unsupported scalar type.");
                    if (value is double number) Finite(number);
                    this.cache.Add(address, value);
                }
                return value;
            }
        }
        private abstract class Node
        {
            protected Node(int depth = 1)
            {
                if (depth > MaximumDepth) throw new FormatException("Table formula expression depth exceeds its limit.");
                this.Depth = depth;
            }
            internal int Depth { get; }
            internal abstract object Evaluate(Evaluation context);
        }
        private sealed class Literal : Node
        {
            private readonly double value;
            internal Literal(double value) { this.value = value; }
            internal override object Evaluate(Evaluation context) { context.Step(); return this.value; }
        }
        private sealed class Reference : Node
        {
            internal Reference(DxfTableCellAddress address) { this.Address = address; }
            internal DxfTableCellAddress Address { get; }
            internal override object Evaluate(Evaluation context) { return context.Read(this.Address); }
        }
        private sealed class Range : Node
        {
            internal Range(DxfTableCellAddress first, DxfTableCellAddress last)
            {
                this.First = new DxfTableCellAddress(Math.Min(first.Row, last.Row), Math.Min(first.Column, last.Column));
                this.Last = new DxfTableCellAddress(Math.Max(first.Row, last.Row), Math.Max(first.Column, last.Column));
            }
            internal DxfTableCellAddress First { get; }
            internal DxfTableCellAddress Last { get; }
            internal override object Evaluate(Evaluation context) { throw new FormatException("A range is only valid as an aggregate argument."); }
        }
        private sealed class Unary : Node
        {
            private readonly char operation; private readonly Node value;
            internal Unary(char operation, Node value) : base(value.Depth + 1) { this.operation = operation; this.value = value; }
            internal override object Evaluate(Evaluation context)
            { context.Step(); double number = Number(this.value.Evaluate(context)); return this.operation == '-' ? -number : number; }
        }
        private sealed class Binary : Node
        {
            private readonly char operation; private readonly Node left, right;
            internal Binary(char operation, Node left, Node right) : base(Math.Max(left.Depth, right.Depth) + 1)
            { this.operation = operation; this.left = left; this.right = right; }
            internal override object Evaluate(Evaluation context)
            {
                context.Step(); double a = Number(this.left.Evaluate(context)), b = Number(this.right.Evaluate(context));
                switch (this.operation)
                {
                    case '+': return Finite(a + b);
                    case '-': return Finite(a - b);
                    case '*': return Finite(a * b);
                    case '/': if (b == 0) throw new DivideByZeroException("Table formula divisor is zero."); return Finite(a / b);
                    default: return Finite(Math.Pow(a, b));
                }
            }
        }
        private sealed class Aggregate : Node
        {
            private readonly string name; private readonly Node[] arguments;
            internal Aggregate(string name, Node[] arguments) : base(arguments.Max(n => n.Depth) + 1) { this.name = name; this.arguments = arguments; }
            internal override object Evaluate(Evaluation context)
            {
                context.Step(); double sum = 0, correction = 0, extreme = 0; int count = 0;
                foreach (Node argument in this.arguments)
                {
                    if (argument is Range range)
                    {
                        context.Check(range.First); context.Check(range.Last);
                        for (int r = range.First.Row; r <= range.Last.Row; r++)
                        for (int c = range.First.Column; c <= range.Last.Column; c++)
                            this.Accumulate(context.Read(new DxfTableCellAddress(r, c)), true, ref sum, ref correction, ref extreme, ref count);
                    }
                    else this.Accumulate(argument.Evaluate(context), argument is Reference, ref sum, ref correction, ref extreme, ref count);
                }
                if (this.name == "COUNT") return (double)count;
                if (this.name == "SUM") return Finite(sum + correction);
                if (count == 0) throw new InvalidOperationException("An empty aggregate has no numeric result: " + this.name);
                return this.name == "AVERAGE" ? Finite((sum + correction) / count) : extreme;
            }
            private void Accumulate(object value, bool referenced, ref double sum, ref double correction, ref double extreme, ref int count)
            {
                if (referenced && (value == null || value is string)) return;
                double number = Number(value);
                if (this.name == "COUNT") { count++; return; }
                if (count++ == 0) extreme = number;
                else extreme = this.name == "MIN" ? Math.Min(extreme, number) : Math.Max(extreme, number);
                if (this.name != "SUM" && this.name != "AVERAGE") return;
                // Neumaier compensated accumulation reduces cancellation in numeric ranges.
                double next = Finite(sum + number);
                correction = Finite(correction + (Math.Abs(sum) >= Math.Abs(number) ? (sum - next) + number : (number - next) + sum));
                sum = next;
            }
        }
        private sealed class Parser
        {
            private readonly string text; private int index = 1, depth;
            private readonly bool allowReferences;
            internal Parser(string text, bool allowReferences = true) { this.text = text; this.allowReferences = allowReferences; }
            internal void End() { this.Space(); if (this.index != this.text.Length) throw new FormatException("Unexpected formula input at " + this.index); }
            private void Space() { while (this.index < this.text.Length && (this.text[this.index] == ' ' || this.text[this.index] == '\t')) this.index++; }
            private bool Take(char value)
            { this.Space(); if (this.index == this.text.Length || this.text[this.index] != value) return false; this.index++; return true; }
            private void Require(char value) { if (!this.Take(value)) throw new FormatException("Expected '" + value + "' at " + this.index); }
            internal Node Expression()
            {
                if (++this.depth > MaximumDepth) throw new FormatException("Table formula nesting exceeds its limit.");
                try
                {
                    Node node = this.Product();
                    while (true)
                    {
                        if (this.Take('+')) node = new Binary('+', node, this.Product());
                        else if (this.Take('-')) node = new Binary('-', node, this.Product());
                        else return node;
                    }
                }
                finally { this.depth--; }
            }
            private Node Product()
            {
                Node node = this.Signed();
                while (true)
                {
                    if (this.Take('*')) node = new Binary('*', node, this.Signed());
                    else if (this.Take('/')) node = new Binary('/', node, this.Signed());
                    else return node;
                }
            }
            private Node Signed()
            {
                if (++this.depth > MaximumDepth) throw new FormatException("Table formula nesting exceeds its limit.");
                try
                {
                    if (this.Take('+')) return new Unary('+', this.Signed());
                    if (this.Take('-')) return new Unary('-', this.Signed());
                    Node node = this.Primary();
                    return this.Take('^') ? new Binary('^', node, this.Signed()) : node;
                }
                finally { this.depth--; }
            }
            private Node Primary()
            {
                if (this.Take('(')) { Node node = this.Expression(); this.Require(')'); return node; }
                this.Space(); int start = this.index;
                if (start == this.text.Length) throw new FormatException("Missing formula operand.");
                char first = this.text[start];
                if (first == '.' || first >= '0' && first <= '9')
                {
                    while (this.index < this.text.Length && char.IsDigit(this.text[this.index])) this.index++;
                    if (this.index < this.text.Length && this.text[this.index] == '.')
                    { this.index++; while (this.index < this.text.Length && char.IsDigit(this.text[this.index])) this.index++; }
                    if (this.index < this.text.Length && (this.text[this.index] == 'e' || this.text[this.index] == 'E'))
                    {
                        this.index++;
                        if (this.index < this.text.Length && (this.text[this.index] == '+' || this.text[this.index] == '-')) this.index++;
                        while (this.index < this.text.Length && char.IsDigit(this.text[this.index])) this.index++;
                    }
                    if (!double.TryParse(this.text.Substring(start, this.index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsNaN(number) || double.IsInfinity(number))
                        throw new FormatException("Invalid finite formula number.");
                    return new Literal(number);
                }
                while (this.index < this.text.Length && (this.text[this.index] == '$' || char.IsLetterOrDigit(this.text[this.index]))) this.index++;
                if (start == this.index) throw new FormatException("Invalid formula token at " + start);
                string token = this.text.Substring(start, this.index - start);
                if (this.Take('('))
                {
                    string name = token.ToUpperInvariant();
                    if (name != "SUM" && name != "AVERAGE" && name != "COUNT" && name != "MIN" && name != "MAX") throw new NotSupportedException("Unknown table formula function: " + token);
                    var arguments = new List<Node>();
                    do
                    {
                        Node node = this.Expression();
                        if (this.Take(':'))
                        {
                            if (!(node is Reference reference)) throw new FormatException("A range must start with a cell address.");
                            Node end = this.Primary();
                            if (!(end is Reference last)) throw new FormatException("A range must end with a cell address.");
                            node = new Range(reference.Address, last.Address);
                        }
                        arguments.Add(node);
                    } while (this.Take(','));
                    this.Require(')');
                    return new Aggregate(name, arguments.ToArray());
                }
                if (!this.allowReferences) throw new NotSupportedException("Scalar formulas cannot contain cell references.");
                return new Reference(DxfTableCellAddress.Parse(token));
            }
        }
    }
}
