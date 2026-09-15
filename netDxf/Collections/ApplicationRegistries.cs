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
using System.Collections.Generic;
using System.Diagnostics;
using netDxf.Tables;

namespace netDxf.Collections
{
    /// <summary>
    /// Represents a collection of application registries.
    /// </summary>
    public sealed class ApplicationRegistries :
        TableObjects<ApplicationRegistry>
    {
        #region constructor

        internal ApplicationRegistries(DxfDocument document)
            : this(document, null)
        {
        }

        internal ApplicationRegistries(DxfDocument document, string handle)
            : base(document, DxfObjectCode.ApplicationIdTable, handle)
        {
        }

        #endregion

        #region override methods

        /// <summary>
        /// Adds an application registry to the list.
        /// </summary>
        /// <param name="appReg"><see cref="ApplicationRegistry">ApplicationRegistry</see> to add to the list.</param>
        /// <param name="assignHandle">Checks if the appReg parameter requires a handle.</param>
        /// <returns>
        /// If a an application registry already exists with the same name as the instance that is being added the method returns the existing application registry,
        /// if not it will return the new application registry.
        /// </returns>
        internal override ApplicationRegistry Add(ApplicationRegistry appReg, bool assignHandle)
        {
            if (appReg == null)
            {
                throw new ArgumentNullException(nameof(appReg));
            }

            if (this.List.TryGetValue(appReg.Name, out ApplicationRegistry add))
            {
                return add;
            }

            if (appReg.Owner != null && !ReferenceEquals(appReg.Owner, this))
                appReg = appReg.CloneStoredGraph();

            if (assignHandle || string.IsNullOrEmpty(appReg.Handle))
            {
                this.Owner.NumHandles = appReg.AssignHandle(this.Owner.NumHandles);
            }

            this.List.Add(appReg.Name, appReg);
            this.References.Add(appReg.Name, new DxfObjectReferences(true));

            appReg.Owner = this;


            Debug.Assert(!string.IsNullOrEmpty(appReg.Handle), "The application registry handle cannot be null or empty.");
            this.Owner.AddedObjects.Add(appReg.Handle, appReg);

            return appReg;
        }

        /// <summary>
        /// Removes an application registry.
        /// </summary>
        /// <param name="name"><see cref="ApplicationRegistry">ApplicationRegistry</see> name to remove from the document.</param>
        /// <returns>True if the application registry has been successfully removed, or false otherwise.</returns>
        /// <remarks>Reserved application registries or any other referenced by objects cannot be removed.</remarks>
        public override bool Remove(string name)
        {
            return this.Remove(this[name]);
        }

        /// <summary>
        /// Removes an application registry.
        /// </summary>
        /// <param name="item"><see cref="ApplicationRegistry">ApplicationRegistry</see> to remove from the document.</param>
        /// <returns>True if the application registry has been successfully removed, or false otherwise.</returns>
        /// <remarks>Reserved application registries or any other referenced by objects cannot be removed.</remarks>
        public override bool Remove(ApplicationRegistry item)
        {
            if (item == null)
            {
                return false;
            }

            if (!this.List.TryGetValue(item.Name, out ApplicationRegistry existing) || !ReferenceEquals(existing, item))
            {
                return false;
            }

            if (item.IsReserved)
            {
                return false;
            }

            if (this.HasReferences(item))
            {
                return false;
            }

            this.Owner.AddedObjects.Remove(item.Handle);
            this.References.Remove(item.Name);
            this.List.Remove(item.Name);

            item.Handle = null;
            item.Owner = null;


            return true;
        }

        #endregion

        /// <summary>Tests whether this exact application registry is registered in the collection.</summary>
        public new bool Contains(ApplicationRegistry item)
        {
            return item != null && this.List.TryGetValue(item.Name, out ApplicationRegistry existing) && ReferenceEquals(existing, item);
        }

        internal void ValidateRecordRename(ApplicationRegistry record, string newName)
        {
            if (!this.Contains(record)) throw new InvalidOperationException("The application registry has inconsistent table membership.");
            if (this.List.TryGetValue(newName, out ApplicationRegistry existing) && !ReferenceEquals(existing, record))
                throw new ArgumentException("There is already another application registry with the same name.", nameof(newName));
        }
        internal void CommitRecordRename(ApplicationRegistry record, string newName)
        {
            DxfObjectReferences references = this.References[record.Name];
            this.List.Remove(record.Name);
            this.References.Remove(record.Name);
            this.List.Add(newName, record);
            this.References.Add(newName, references);
        }
    }
}