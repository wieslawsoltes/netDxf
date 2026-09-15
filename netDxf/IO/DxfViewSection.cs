// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Entities;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<Tuple<View, string>> loadedViewSections = new List<Tuple<View, string>>();

        private void ResolveViewSections()
        {
            foreach (var pending in this.loadedViewSections)
            {
                View view = pending.Item1;
                if (!ReferenceEquals(this.GetObjectBySourceHandle(view.Handle), view))
                    throw new InvalidDataException("A VIEW live-section referrer must retain its exact physical source identity.");
                Section section = null;
                if (pending.Item2 != "0")
                {
                    section = this.GetObjectBySourceHandle(pending.Item2) as Section;
                    if (section == null) throw new InvalidDataException("VIEW group 334 must resolve to an actual source SECTION or SECTIONOBJECT.");
                }
                try { view.LiveSection = section; }
                catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is NotSupportedException)
                { throw new InvalidDataException("Invalid VIEW live-section relationship.", error); }
            }
        }
    }
}
