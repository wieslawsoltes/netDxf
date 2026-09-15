// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private bool WriteStoredSunStudyPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfStoredSunStudy study)) return false;
            foreach (DxfTag tag in study.Payload) this.WriteDatabaseTag(tag, false);
            return true;
        }
    }
}
