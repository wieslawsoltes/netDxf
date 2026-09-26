// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Tables;

namespace NetDxf.Qualification
{
    internal static class TableAverageCases
    {
        internal sealed class Case
        {
            internal readonly string Id;
            internal readonly ulong[] Inputs;
            internal readonly ulong Expected;
            internal Case(string id, ulong expected, params ulong[] inputs)
            { Id = id; Expected = (expected & 0x7fffffffffffffffUL) == 0 ? 0 : expected; Inputs = inputs; }
        }
        internal static ulong Bits(double value) { return unchecked((ulong)BitConverter.DoubleToInt64Bits(value)); }
        internal static double Value(ulong bits) { return BitConverter.Int64BitsToDouble(unchecked((long)bits)); }
        internal static string Hex(ulong bits) { return bits.ToString("x16", CultureInfo.InvariantCulture); }
        internal static IEnumerable<Case> All()
        {
            for (int exponent = 0; exponent < 2047; exponent++)
            {
                ulong lower = ((ulong)exponent << 52) | (((ulong)exponent * 0x1f123bb5UL) & ~1UL);
                for (int sign = 0; sign < 2; sign++)
                {
                    ulong mask = (ulong)sign << 63, a = lower | mask;
                    string id = "exponent/" + exponent + "/" + sign + "/";
                    yield return new Case(id + "repeat", a, a, a);
                    yield return new Case(id + "even", a, a, (lower + 1) | mask);
                    yield return new Case(id + "odd", (lower + 2) | mask, (lower + 1) | mask, (lower + 2) | mask);
                    yield return new Case(id + "cancel", Bits(sign == 0 ? 1d / 3 : -1d / 3),
                        a, Bits(sign == 0 ? 1d : -1d), a ^ 0x8000000000000000UL);
                }
            }
            const ulong max = 0x7fefffffffffffffUL, neg = 0x8000000000000000UL;
            yield return new Case("edge/0", max, max, max);
            yield return new Case("edge/1", max | neg, max | neg, max | neg);
            yield return new Case("edge/2", Bits(double.MaxValue / 3), max, max, max | neg);
            yield return new Case("edge/3", 0, 0, 1);
            yield return new Case("edge/4", 2, 1, 2);
            yield return new Case("edge/5", 0x10000000000000UL, 0xfffffffffffffUL, 0x10000000000000UL);
            yield return new Case("edge/6", 0, max, 1, max | neg);
            yield return new Case("edge/7", 1, max, 3, max | neg);
            yield return new Case("edge/8", 0, 0, neg);
            yield return new Case("edge/9", 2, 3, 3, 0);
            yield return new Case("edge/10", neg | 2, neg | 1, neg | 2);
            yield return new Case("edge/11", max, max);
        }
        internal static ulong[] Evaluate(Case item)
        {
            double[] values = item.Inputs.Select(Value).ToArray();
            string range = "A1:A" + values.Length.ToString(CultureInfo.InvariantCulture);
            int calls = 0;
            double resolved = DxfTableFormula.Parse("=AVERAGE(" + range + ")")
                .Evaluate(values.Length, 1, address => { calls++; return values[address.Row]; });
            if (calls != values.Length) throw new InvalidOperationException("Average resolver inventory");
            calls = 0;
            double duplicate = DxfTableFormula.Parse("=AVERAGE(" + range + "," + range + ")")
                .Evaluate(values.Length, 1, address => { calls++; return values[address.Row]; });
            if (calls != values.Length) throw new InvalidOperationException("Average resolver memoization");
            Func<IEnumerable<double>, double> scalar = input => DxfTableFormula.ParseScalar("=AVERAGE("
                + string.Join(",", input.Select(v => v.ToString("G17", CultureInfo.InvariantCulture))) + ")").EvaluateScalar();
            return new[] { Bits(resolved), Bits(duplicate), Bits(scalar(values)), Bits(scalar(Enumerable.Reverse(values))) };
        }
        internal static void VerifyAll(Action<Case, ulong[]>? observe = null)
        {
            foreach (Case item in All())
            {
                ulong[] results = Evaluate(item);
                if (observe != null) observe(item, results);
                if (results.Any(value => value != item.Expected))
                    throw new InvalidOperationException("Incorrect AVERAGE bits: " + item.Id + " expected=" + Hex(item.Expected)
                        + " actual=" + string.Join(",", results.Select(Hex)));
            }
        }
        internal static void VerifyPolicies()
        {
            object?[] cells = { double.MaxValue, null, "ignored", double.MaxValue };
            var formula = DxfTableFormula.Parse("=AVERAGE(A1:A4)");
            if (formula.Evaluate(4, 1, a => cells[a.Row]!) != double.MaxValue)
                throw new InvalidOperationException("AVERAGE coerced empty/text data");
            if (DxfTableFormula.Parse("=AVERAGE(A1:A2)").Evaluate(2, 1,
                a => a.Row == 0 ? int.MinValue : int.MaxValue) != -0.5)
                throw new InvalidOperationException("Integer mean lost exactness");
            foreach (object bad in new object[] { true, new object(), double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                bool rejected = false;
                try { formula.Evaluate(4, 1, a => a.Row == 3 ? bad : (object)double.MaxValue); }
                catch (Exception error) when (error is ArithmeticException || error is NotSupportedException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("AVERAGE admitted invalid operand");
            }
            bool empty = false;
            try { formula.Evaluate(4, 1, a => null!); } catch (InvalidOperationException) { empty = true; }
            if (!empty) throw new InvalidOperationException("Empty AVERAGE was accepted");
            // The change is confined to AVERAGE; SUM and binary-expression overflow still reject.
            foreach (string expression in new[] { "=SUM(1e308,1e308)", "=AVERAGE(1e308*2,1)" })
            {
                bool rejected = false;
                try { DxfTableFormula.ParseScalar(expression).EvaluateScalar(); } catch (ArithmeticException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Unchanged arithmetic overflow policy was relaxed");
            }
            var recursive = DxfTableFormula.Parse("=AVERAGE(A1:A2)");
            double nested = recursive.Evaluate(2, 1, a => recursive.Evaluate(2, 1, b => double.MaxValue));
            if (nested != double.MaxValue) throw new InvalidOperationException("Reentrant AVERAGE reused mutable accumulator");
            var before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
                if (DxfTableFormula.ParseScalar("=AVERAGE(1.5,2.5)").EvaluateScalar() != 2)
                    throw new InvalidOperationException("AVERAGE used current-culture syntax");
            }
            finally { CultureInfo.CurrentCulture = before; }
        }
        internal static void VerifyInstalled() { VerifyAll(); VerifyPolicies(); }
    }
}
