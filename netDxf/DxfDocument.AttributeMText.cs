// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf.Entities;
namespace netDxf
{
    public partial class DxfDocument
    {
        internal void ReplaceAttributeMTextStyle(DxfObject host, AttributeMText oldText, AttributeMText newText)
        {
            if (newText != null) newText.Style = this.textStyles.Add(newText.Style);
            if (oldText != null) this.textStyles.References[oldText.Style.Name].Remove(host);
            if (newText != null) this.textStyles.References[newText.Style.Name].Add(host);
        }
        private void RegisterAttributeMText(DxfObject host, AttributeTextState state, bool assignHandle)
        {
            if (state?.Content == null) return;
            state.Content.Style = this.textStyles.Add(state.Content.Style, assignHandle);
            this.textStyles.References[state.Content.Style.Name].Add(host);
        }
        private void UnregisterAttributeMText(DxfObject host, AttributeTextState state)
        {
            if (state?.Content != null) this.textStyles.References[state.Content.Style.Name].Remove(host);
        }
    }
}
