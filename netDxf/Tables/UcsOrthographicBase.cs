using System;
using netDxf.Collections;

namespace netDxf.Tables
{
    public partial class UCS
    {
        private short orthographicViewType;
        private UCS baseUcs;
        private bool baseUcsHandlePresent;
        /// <summary>Gets stored group 79: zero for an ordinary UCS, or 1–6 for Top, Bottom, Front, Back, Left and Right.</summary>
        /// <remarks>This is distinct from the group-71 orthographic origin overrides. No coordinate evaluation is performed.</remarks>
        public short OrthographicViewType { get { return this.orthographicViewType; } }
        /// <summary>Gets the group-346 base UCS object. Null uses WORLD for a nonzero orthographic view type.</summary>
        public UCS BaseUcs { get { return this.baseUcs; } }
        /// <summary>Atomically replaces the stored orthographic type and its optional base-object reference.</summary>
        /// <param name="viewType">Zero through six. Zero requires a null base and clears the relationship.</param>
        /// <param name="baseUcs">The base UCS, or null for WORLD. A registered owner requires a registered target in the same document.</param>
        /// <remarks>Detached clones retain actual target identities; call this method to map them before adding a clone to another document. This is a pointer relationship, without evaluation or ownership cascades.</remarks>
        public void SetOrthographicBase(short viewType, UCS baseUcs = null)
        { this.SetOrthographicBaseCore(viewType, baseUcs, baseUcs != null); }
        internal bool BaseUcsHandlePresent { get { return this.baseUcsHandlePresent; } }
        internal void SetLoadedOrthographicBase(short viewType, UCS baseUcs, bool present)
        { this.SetOrthographicBaseCore(viewType, baseUcs, present); }
        private void SetOrthographicBaseCore(short viewType, UCS baseUcs, bool present)
        {
            if (viewType < 0 || viewType > 6) throw new ArgumentOutOfRangeException(nameof(viewType));
            if (viewType == 0 && (baseUcs != null || present)) throw new ArgumentException("UCS group 346 requires a nonzero orthographic view type.", nameof(baseUcs));
            UcsReferences.Replace(this, this.baseUcs, baseUcs);
            this.orthographicViewType = viewType;
            this.baseUcs = baseUcs;
            this.baseUcsHandlePresent = present;
        }
        internal void ValidateOrthographicBase()
        {
            if (this.orthographicViewType < 0 || this.orthographicViewType > 6 || this.orthographicViewType == 0 && (this.baseUcs != null || this.baseUcsHandlePresent))
                throw new InvalidOperationException("Invalid stored UCS orthographic base relationship.");
        }
        private void CopyOrthographicBaseTo(UCS copy)
        {
            copy.orthographicViewType = this.orthographicViewType;
            copy.baseUcs = this.baseUcs;
            copy.baseUcsHandlePresent = this.baseUcsHandlePresent;
        }
    }
}
