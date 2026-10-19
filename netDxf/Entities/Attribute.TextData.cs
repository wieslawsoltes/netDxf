// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf.Entities
{
    public partial class Attribute
    {
        /// <summary>Returns independent stored text data, or null when absent.</summary>
        /// <param name="cloneStyles">True clones embedded styles; false retains their canonical resource references.</param>
        public AttributeTextData GetTextData(bool cloneStyles = true) => AttributeTextData.FromState(this.TextState, cloneStyles);

        /// <summary>Validates and copies exact stored text data; null removes it.</summary>
        /// <remarks>Retains fallback text, geometry and proxy graphics. Editors changing visible content should clear
        /// stale proxy graphics explicitly. Resource adoption follows the owning document's normal style rules.</remarks>
        public void SetTextData(AttributeTextData data)
        {
            AttributeTextState next = data?.ToState(false);
            DxfDocument document = this.Owner?.Owner?.Record.Owner?.Owner;
            if (document != null) document.ReplaceAttributeMTextStyle(this, this.TextState?.Content, next?.Content);
            this.TextState = next;
        }
    }

    public partial class AttributeDefinition
    {
        /// <summary>Returns independent stored text data, or null when absent.</summary>
        /// <param name="cloneStyles">True clones embedded styles; false retains their canonical resource references.</param>
        public AttributeTextData GetTextData(bool cloneStyles = true) => AttributeTextData.FromState(this.TextState, cloneStyles);

        /// <summary>Validates and copies exact stored text data; null removes it without changing fallback or proxy data.</summary>
        public void SetTextData(AttributeTextData data)
        {
            AttributeTextState next = data?.ToState(true);
            DxfDocument document = this.Owner?.Record.Owner?.Owner;
            if (document != null) document.ReplaceAttributeMTextStyle(this, this.TextState?.Content, next?.Content);
            this.TextState = next;
        }
    }
}
