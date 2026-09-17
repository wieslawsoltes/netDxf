// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using netDxf.Blocks;
using netDxf.Entities;
using DxfAttribute = netDxf.Entities.Attribute;

namespace netDxf.Objects
{
    public sealed partial class DxfObjectDatabase
    {
        /// <summary>Applies explicit FIELD results and their successful roots' literal text to qualified text hosts atomically.</summary>
        /// <returns>The number of changed FIELD payloads, not the number of changed host strings.</returns>
        /// <remarks>Every selected root must occupy its actual host's ACAD_FIELD/TEXT dictionary slot.
        /// TEXT, standalone MTEXT, ATTRIB and ATTDEF are supported. Failed root outcomes retain host text.
        /// Existing ApplyFieldResults remains cache-only. No resources are created and no handles allocated.</remarks>
        public int ApplyFieldResultsAndUpdateTextHosts(IEnumerable<DxfFieldResultEdit> results)
        { return this.ApplyFieldResultsCore(results, true); }

        /// <summary>Evaluates complete FIELD roots and updates their successful text hosts in the same transaction.</summary>
        /// <returns>The number of changed FIELD payloads. A corrected host may change even when this value is zero.</returns>
        /// <remarks>Host association and state are captured before callbacks and revalidated before publication.
        /// All field packets and host strings are prepared before any state is published. Failed root results
        /// retain host strings, including explicit failed fallbacks; hosts are changed only for Success.
        /// MTEXT is escaped as literal text; TEXT/ATTRIB/ATTDEF require single-line literal text. Native percent
        /// controls, Unicode-escape prefixes, FIELD delimiters and column-associated MTEXT reject. Changed hosts lose stale proxy graphics;
        /// changed ATTRIB hosts also clear their owning INSERT proxy. Geometry, FIELD code, private checksums,
        /// attribute instances derived from ATTDEF and other native caches are not regenerated. No native
        /// evaluator runs implicitly. Independent callback mutations are not rolled back; single-threaded API.</remarks>
        public int EvaluateFieldTreesAndUpdateTextHosts(IEnumerable<DxfStoredField> roots,
            Func<DxfFieldEvaluationInput, DxfFieldResult> evaluator, int evaluationContext = 32)
        { return this.EvaluateFieldTreesCore(roots, evaluator, evaluationContext, true); }

        private sealed class FieldTextHostState
        {
            internal DxfStoredField Root;
            internal DxfDictionary Fields, Extension;
            internal DxfObject Host, Owner;
            internal string Value;
            internal CommonEntityData Data, ParentData;
            internal byte[] Proxy, ParentProxy;
        }

        private sealed class FieldTextHostUpdate
        {
            internal FieldTextHostState State;
            internal string Value;
            internal void Publish()
            {
                // These nonvirtual setters only assign the already prepared immutable string.
                if (this.State.Host is Text text) text.Value = this.Value;
                else if (this.State.Host is MText mtext) mtext.Value = this.Value;
                else if (this.State.Host is DxfAttribute attribute) attribute.Value = this.Value;
                else ((AttributeDefinition)this.State.Host).Value = this.Value;
                this.State.Data.ProxyGraphics = null;
                if (this.State.ParentData != null) this.State.ParentData.ProxyGraphics = null;
            }
        }

        private List<FieldTextHostState> CaptureFieldTextHosts(IEnumerable<DxfStoredField> roots)
        {
            var result = new List<FieldTextHostState>();
            var seen = new HashSet<DxfObject>();
            var columnTexts = this.FieldColumnTexts();
            long characters = 0;
            foreach (var root in roots)
            {
                this.CheckFieldResultTarget(root);
                var fields = root.Owner as DxfDictionary;
                var extension = fields?.Owner as DxfDictionary;
                var host = extension?.Owner;
                if (fields == null || extension == null || host == null ||
                    !ReferenceEquals(host.ExtensionDictionary, extension) ||
                    !IsFieldHostLink(extension, "ACAD_FIELD", fields) || !IsFieldHostLink(fields, "TEXT", root))
                    throw new NotSupportedException("The FIELD root must occupy its host's actual ACAD_FIELD/TEXT ownership slots.");
                if (!seen.Add(host)) throw new ArgumentException("Two roots cannot publish the same host string.", nameof(roots));
                this.ValidateFieldTextHost(host, columnTexts);
                CommonEntityData data = FieldHostData(host);
                var parent = host is DxfAttribute attr ? attr.Owner.CommonData : null;
                string value = FieldHostValue(host);
                characters += value?.Length ?? 0;
                if (characters > MaximumFieldResultCharacters)
                    throw new ArgumentException("Combined source host text exceeds the FIELD transaction limit.", nameof(roots));
                result.Add(new FieldTextHostState { Root = root, Fields = fields, Extension = extension,
                    Host = host, Owner = host.Owner, Value = value, Data = data, ParentData = parent,
                    Proxy = data.ProxyGraphics, ParentProxy = parent?.ProxyGraphics });
            }
            return result;
        }

        private List<FieldTextHostUpdate> PrepareFieldTextHosts(List<FieldTextHostState> states, List<DxfFieldResultEdit> edits)
        {
            var columnTexts = this.FieldColumnTexts();
            var byField = edits.ToDictionary(edit => edit.Original.Field, edit => edit.Result);
            var updates = new List<FieldTextHostUpdate>();
            long characters = 0;
            foreach (var state in states)
            {
                this.ValidateFieldTextHost(state.Host, columnTexts);
                if (!ReferenceEquals(state.Root.Owner, state.Fields) || !ReferenceEquals(state.Fields.Owner, state.Extension) ||
                    !ReferenceEquals(state.Extension.Owner, state.Host) || !ReferenceEquals(state.Host.ExtensionDictionary, state.Extension) ||
                    !IsFieldHostLink(state.Extension, "ACAD_FIELD", state.Fields) || !IsFieldHostLink(state.Fields, "TEXT", state.Root) ||
                    !ReferenceEquals(state.Host.Owner, state.Owner) || FieldHostValue(state.Host) != state.Value ||
                    !ReferenceEquals(state.Data.ProxyGraphics, state.Proxy) ||
                    state.ParentData != null && !ReferenceEquals(state.ParentData.ProxyGraphics, state.ParentProxy))
                    throw new InvalidOperationException("A FIELD text host changed during evaluator callbacks.");
                if (!byField.TryGetValue(state.Root, out DxfFieldResult result))
                    throw new InvalidOperationException("A selected text host has no root FIELD result.");
                if (result.Status != DxfFieldResultStatus.Success) continue;
                string value = FieldHostLiteral(result.FormattedText, state.Host is MText);
                characters += value.Length;
                if (characters > MaximumFieldResultCharacters)
                    throw new ArgumentException("Combined escaped host text exceeds the FIELD transaction limit.", nameof(edits));
                if (value != state.Value) updates.Add(new FieldTextHostUpdate { State = state, Value = value });
            }
            return updates;
        }

        private static bool IsFieldHostLink(DxfDictionary owner, string name, DxfObject target)
        {
            // Actual entries, not DictionaryWithDefault fallback results or same-name guesses.
            return owner.Entries.Any(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)
                && entry.IsHardOwner && ReferenceEquals(entry.Target, target));
        }

        private HashSet<MText> FieldColumnTexts()
        {
            var result = new HashSet<MText>();
            foreach (var text in this.Document.Blocks.SelectMany(block => block.Entities).OfType<MText>())
            {
                if (text.Columns == null) continue;
                result.Add(text);
                foreach (var linked in text.Columns.LinkedColumns) if (linked != null) result.Add(linked);
            }
            return result;
        }

        private void ValidateFieldTextHost(DxfObject host, HashSet<MText> columnTexts)
        {
            if (!this.IsRegistered(host))
                throw new InvalidOperationException("The FIELD text host must remain registered in the source document.");
            if (host is Text || host is MText)
            {
                var entity = (EntityObject)host;
                if (entity.Owner == null || !entity.Owner.Entities.Contains(entity))
                    throw new InvalidOperationException("The FIELD text host is not in its registered block.");
                if (host is MText mtext && (mtext.Columns != null || columnTexts.Contains(mtext)))
                    throw new NotSupportedException("Column-associated MTEXT requires a separate complete reflow transaction.");
            }
            else if (host is DxfAttribute attribute)
            {
                if (attribute.Owner == null || !attribute.Owner.Attributes.Contains(attribute) ||
                    !ReferenceEquals(this.Document.GetObjectByHandle(attribute.Owner.Handle), attribute.Owner))
                    throw new InvalidOperationException("The attribute must retain its registered INSERT owner.");
            }
            else if (host is AttributeDefinition definition)
            {
                if (definition.Owner == null || !definition.Owner.AttributeDefinitions.ContainsValue(definition))
                    throw new InvalidOperationException("The attribute definition is no longer in its source block.");
            }
            else throw new NotSupportedException("Only TEXT, standalone MTEXT, ATTRIB and ATTDEF FIELD hosts are qualified.");
        }

        private static CommonEntityData FieldHostData(DxfObject host)
        {
            if (host is EntityObject entity) return entity.CommonData;
            if (host is DxfAttribute attribute) return attribute.CommonData;
            return ((AttributeDefinition)host).CommonData;
        }
        private static string FieldHostValue(DxfObject host)
        {
            if (host is Text text) return text.Value;
            if (host is MText mtext) return mtext.Value;
            if (host is DxfAttribute attribute) return attribute.Value;
            return ((AttributeDefinition)host).Value;
        }
        private static string FieldHostLiteral(string text, bool multiline)
        {
            DxfStoredTableContent.CheckEditableText(text, nameof(text));
            if (text.IndexOf("%%", StringComparison.Ordinal) >= 0 || text.IndexOf("%<", StringComparison.Ordinal) >= 0 ||
                text.IndexOf(">%", StringComparison.Ordinal) >= 0 || text.IndexOf("\\U+", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new NotSupportedException("Native symbol, Unicode-escape and FIELD control sequences are not literal host text.");
            var result = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char value = text[i];
                if (char.IsControl(value) && (!multiline || value != '\r' && value != '\n'))
                    throw new NotSupportedException("The host cannot represent the supplied literal control character.");
                string piece;
                if (multiline && (value == '\r' || value == '\n'))
                {
                    if (value == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    piece = "\\P";
                }
                else piece = multiline && (value == '\\' || value == '{' || value == '}') ? "\\" + value : value.ToString();
                if (piece.Length > DxfStoredTableContent.MaximumEditedStringLength - result.Length)
                    throw new ArgumentOutOfRangeException(nameof(text), "Escaped host text exceeds its limit.");
                result.Append(piece);
            }
            return result.ToString();
        }
    }
}
