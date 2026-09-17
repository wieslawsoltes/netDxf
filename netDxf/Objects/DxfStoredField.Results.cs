// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    public sealed partial class DxfStoredField
    {
        /// <summary>Maximum FIELD payload size admitted by explicit result editing.</summary>
        public const int MaximumResultPayloadTags = 1048576;
        /// <summary>Gets the immutable evaluation/cache projection, or null for unqualified value/framing variants.</summary>
        public DxfFieldEvaluationSnapshot Evaluation { get; private set; }

        internal sealed class PreparedResult
        {
            internal IReadOnlyList<DxfTag> Payload;
            internal DxfFieldEvaluationSnapshot Evaluation;
        }
        internal PreparedResult PrepareResult(DxfFieldResultEdit request)
        {
            if (!ReferenceEquals(request.Original, this.Evaluation) || !ReferenceEquals(request.Original.SourcePayload, this.Payload))
                throw new ArgumentException("FIELD result edits require the current evaluation snapshot.", nameof(request));
            this.ValidateSource(this.source);
            var original = this.Evaluation; var result = request.Result;
            if (result.RetainsCachedValue && !ReferenceEquals(result.RetainedSnapshot, original))
                throw new ArgumentException("Retained FIELD results must use this field's current snapshot.", nameof(request));
            // Every completed attempt is Evaluated and no longer Modified. Explicit values
            // establish cache/display presence; retained failures preserve the original presence bits.
            int state = (original.StoredState & ~4) | 8;
            if (!result.RetainsCachedValue) state |= 16 | 32;
            this.CheckResultRecordBudget(this.Payload.Count);
            bool sameValue = SameValue(original.Value, result.Value);
            bool sameDisplay = original.FormattedText == result.FormattedText;
            bool sameValueDisplay = !original.StoredValueFlags.HasValue || original.ValueDisplayText == result.ValueDisplayText;
            if (sameValue && sameDisplay && sameValueDisplay && original.StoredState == state && original.StoredStatus == (int)result.Status &&
                original.StoredErrorCode == result.ErrorCode && original.ErrorMessage == result.ErrorMessage) return null;

            var tags = new List<DxfTag>(this.Payload.Count);
            tags.AddRange(this.Payload.Take(original.CacheStart));
            Replace(tags, original.StateIndex, state);
            Replace(tags, original.StatusIndex, (int)result.Status); Replace(tags, original.ErrorIndex, result.ErrorCode);
            if (original.ErrorMessage != result.ErrorMessage)
                tags[original.MessageIndex] = new DxfTag(300, this.EncodeResultText(result.ErrorMessage));
            if (sameValue)
                tags.AddRange(this.Payload.Skip(original.CacheStart).Take(original.ScalarEnd - original.CacheStart));
            else
            {
                if (original.StoredValueFlags.HasValue) tags.Add(this.Payload[original.CacheStart]);
                int kind = result.Value == null ? 0 : result.Value is int ? 1 : result.Value is double ? 2 : 4;
                tags.Add(new DxfTag(90, kind));
                if (kind == 0) tags.Add(new DxfTag(91, 0));
                else tags.Add(new DxfTag(kind == 1 ? (short)91 : kind == 2 ? (short)140 : (short)1,
                    kind == 4 ? (object)this.EncodeResultText((string)result.Value) : result.Value));
            }
            for (int i = original.ScalarEnd; i < original.CacheEnd; i++)
                tags.Add(i == original.ValueDisplayIndex && !sameValueDisplay ? new DxfTag(302, this.EncodeResultText(result.ValueDisplayText)) : this.Payload[i]);
            if (sameDisplay) tags.AddRange(this.Payload.Skip(original.TextStart));
            else
            {
                string encoded = this.EncodeResultText(result.FormattedText);
                int start = 0;
                do
                {
                    int length = Math.Min(250, encoded.Length - start);
                    if (length > 0 && start + length < encoded.Length && char.IsHighSurrogate(encoded[start + length - 1])) length--;
                    tags.Add(new DxfTag(start == 0 ? (short)301 : (short)9, encoded.Substring(start, length)));
                    start += length;
                } while (start < encoded.Length);
                tags.Add(new DxfTag(98, result.FormattedText.Length));
            }
            this.CheckResultRecordBudget(tags.Count);
            var packet = tags.AsReadOnly();
            var projected = DxfFieldEvaluationSnapshot.TryRead(this, packet);
            if (projected == null) throw new InvalidOperationException("The replacement FIELD cache failed projection qualification.");
            // Cached scalar results cannot introduce/remove any object or child-field handle.
            var before = this.Payload.Where(IsSemantic).ToArray(); var after = packet.Where(IsSemantic).ToArray();
            if (!before.SequenceEqual(after)) throw new InvalidOperationException("FIELD result replacement changed dependency tags.");
            return new PreparedResult { Payload = packet, Evaluation = projected };
        }
        private static void Replace(List<DxfTag> tags, int index, int value)
        { if (!Equals(tags[index].Value, value)) tags[index] = new DxfTag(tags[index].Code, value); }
        internal static bool SameValue(object first, object second)
        {
            if (first is double a && second is double b) return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
            return Equals(first, second);
        }
        internal void PublishResult(PreparedResult result)
        {
            if (result == null) return;
            this.Payload = result.Payload; this.Evaluation = result.Evaluation;
        }
        private void CheckResultRecordBudget(int payloadCount)
        {
            long count = payloadCount + 2L + (this.ExtensionDictionary == null ? 0 : 3);
            int reactors = this.PersistentReactors.Select(item => item.Handle).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (reactors != 0) count += reactors + 2L;
            foreach (XData data in this.XData.Values)
            {
                count++;
                foreach (XDataRecord record in data.XDataRecord)
                    count += record.Code == XDataCode.BinaryData ? Math.Max(1L, (((byte[])record.Value).LongLength + 126L) / 127L) : 1L;
            }
            if (count > MaximumResultPayloadTags) throw new InvalidOperationException("FIELD result and metadata exceed the record tag limit.");
        }
        private string EncodeResultText(string text)
        {
            var output = new StringBuilder();
            foreach (char value in text)
            {
                bool escape = value == '\\' || this.SourceVersion < DxfVersion.AutoCad2007 && value > 127;
                if (output.Length > DxfStoredTableContent.MaximumEditedStringLength - (escape ? 7 : 1))
                    throw new ArgumentOutOfRangeException(nameof(text), "Encoded FIELD result exceeds its limit.");
                if (escape) output.Append("\\U+").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
                else output.Append(value);
            }
            return output.ToString();
        }
        internal static string DecodeResultText(string text)
        {
            if (text.Length > DxfStoredTableContent.MaximumEditedStringLength) throw new ArgumentOutOfRangeException(nameof(text));
            var output = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char value = text[i];
                if (value == '\\' && i + 6 < text.Length && (text[i + 1] == 'U' || text[i + 1] == 'u') && text[i + 2] == '+' &&
                    int.TryParse(text.Substring(i + 3, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int decoded))
                { value = (char)decoded; i += 6; }
                output.Append(value);
            }
            string result = output.ToString(); DxfStoredTableContent.CheckEditableText(result, nameof(text)); return result;
        }
    }
}
