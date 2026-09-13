#region netDxf library licensed under the MIT License
//
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
#endregion

using System;

namespace netDxf.IO
{
    // A bounded lexical state machine, not an object/ownership schema. Both raw decoding and
    // authored snapshots use it, so a context-qualified tag cannot bypass ordinary handle checks.
    internal sealed class DxfRawTagContext
    {
        private string section;
        private string table;
        private string record;
        private bool sectionNamePending;
        private bool tableNamePending;
        private int controlDepth;
        private bool xdata;

        internal bool Code5IsString
        {
            get
            {
                return Is(this.section, "TABLES") && Is(this.table, "DIMSTYLE") &&
                    Is(this.record, "DIMSTYLE") && this.controlDepth == 0 && !this.xdata;
            }
        }

        internal void Advance(DxfTag tag)
        {
            string value = tag.RawValue as string;
            if (tag.Code == 999) return;
            if (tag.Code == 0)
            {
                this.controlDepth = 0;
                this.xdata = false;
                this.record = value;
                this.tableNamePending = false;
                if (Is(value, "ENDSEC") || Is(value, "EOF"))
                {
                    this.section = null;
                    this.table = null;
                    this.sectionNamePending = false;
                }
                else if (this.section == null && Is(value, "SECTION"))
                {
                    this.sectionNamePending = true;
                }
                else if (Is(this.section, "TABLES"))
                {
                    if (Is(value, "TABLE"))
                    {
                        this.table = null;
                        this.tableNamePending = true;
                    }
                    else if (Is(value, "ENDTAB")) this.table = null;
                }
                return;
            }
            if (this.sectionNamePending)
            {
                if (tag.Code == 2) this.section = value;
                this.sectionNamePending = false;
                return;
            }
            if (tag.Code == 1001) this.xdata = true;
            if (this.xdata) return;
            if (tag.Code == 102)
            {
                if (value.StartsWith("{", StringComparison.Ordinal)) this.controlDepth++;
                else if (value == "}" && this.controlDepth > 0) this.controlDepth--;
                return;
            }
            if (tag.Code == 2 && this.tableNamePending && this.controlDepth == 0)
            {
                this.table = value;
                this.tableNamePending = false;
            }
        }

        private static bool Is(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
