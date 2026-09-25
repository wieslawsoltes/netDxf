// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<Tuple<EndSequence, string>> insertSequenceOwners = new List<Tuple<EndSequence, string>>();

        private EndSequence ReadInsertSequenceEnd()
        {
            SourceRecordIdentity source = this.CurrentSourceRecord;
            var end = new EndSequence();
            string owner = null;
            bool common = true;
            int groupDepth = 0;
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                short code = this.chunk.Code;
                if (code == 102)
                {
                    string control = this.chunk.ReadString();
                    if (control.StartsWith("{", StringComparison.Ordinal)) groupDepth++;
                    else if (control == "}" && groupDepth > 0) groupDepth--;
                }
                else if (groupDepth == 0)
                {
                    if (code == 100 || code == 1001) common = false;
                    if (common && (code == 5 || code == 330))
                    {
                        string value = this.chunk.ReadHex();
                        ulong number = ulong.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                        if (number == 0) throw new FormatException("A declared INSERT terminator identity/owner must be nonzero.");
                        value = number.ToString("X", CultureInfo.InvariantCulture);
                        if (code == 5)
                        {
                            if (end.Handle != null) throw new FormatException("Duplicate INSERT terminator identity.");
                            end.Handle = value;
                        }
                        else
                        {
                            if (owner != null) throw new FormatException("Duplicate INSERT terminator owner.");
                            owner = value;
                        }
                    }
                    else if (code == 8) end.StoredLayer = this.GetLayer(this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString()));
                    else if (code == 1001)
                    {
                        string name = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                        end.XData.Add(this.ReadXDataRecord(this.GetApplicationRegistry(name)));
                        continue;
                    }
                }
                this.chunk.Next();
            }
            if (groupDepth != 0) throw new FormatException("Unterminated INSERT terminator control group.");
            if (end.Handle != null)
            {
                if (source == null || source.Handle != ulong.Parse(end.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture))
                    throw new FormatException("INSERT terminator identity must belong to its physical common header.");
                // Reserve source identity even when the file has an undersized HANDSEED.
                if (long.TryParse(end.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long number)
                    && number >= this.doc.NumHandles && number < long.MaxValue) this.doc.NumHandles = number + 1;
                this.RecordSourceObject(end, source);
            }
            this.insertSequenceOwners.Add(Tuple.Create(end, owner));
            return end;
        }
        private void ResolveInsertSequenceOwners()
        {
            foreach (var pair in this.insertSequenceOwners)
                if (pair.Item2 != null && !ReferenceEquals(this.GetObjectBySourceHandle(pair.Item2), pair.Item1.Owner))
                    throw new FormatException("SEQEND must be owned by its actual INSERT, not another entity or BLOCK_RECORD.");
        }
    }
}
