// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Linq;

namespace netDxf.Entities
{
    public partial class Insert
    {
        private EndSequence sequenceEnd;

        /// <summary>Gets the owned attribute-sequence terminator, or null when no sequence exists.</summary>
        /// <remarks>Its identity is registered with the drawing and survives repeated saves.
        /// An empty retained sequence is kept after attribute synchronization. Common database
        /// metadata and XData are editable through DxfObject; private SEQEND payloads are not modeled.</remarks>
        public DxfObject EndSequenceRecord { get { return this.SequenceEnd; } }

        internal EndSequence SequenceEnd
        {
            get
            {
                if (this.sequenceEnd == null && this.attributes.Count != 0)
                    this.EnsureSequenceEnd();
                return this.sequenceEnd;
            }
        }
        internal void EnsureSequenceEnd()
        {
            if (this.sequenceEnd == null) this.sequenceEnd = new EndSequence { Owner = this };
        }
        internal void RestoreSequenceEnd(EndSequence value)
        {
            this.sequenceEnd = value;
            if (value != null) value.Owner = this;
        }
        private void CheckSequenceEndClone()
        {
            EndSequence end = this.SequenceEnd;
            if (end == null) return;
            if (end.ExtensionDictionary != null || end.PersistentReactors.Count != 0 || end.XData.Values
                .SelectMany(data => data.XDataRecord).Any(tag => tag.Code == XDataCode.DatabaseHandle && (string)tag.Value != "0"))
                throw new NotSupportedException("Cloning a referenced INSERT terminator requires an explicit metadata graph mapping.");
        }
        private void CopySequenceEndTo(Insert target)
        {
            EndSequence source = this.SequenceEnd;
            if (source == null) return;
            var end = new EndSequence { Owner = target, StoredLayer = source.StoredLayer == null ? null : (netDxf.Tables.Layer)source.StoredLayer.Clone() };
            foreach (XData data in source.XData.Values) end.XData.Add((XData)data.Clone());
            target.sequenceEnd = end;
        }
    }
}
