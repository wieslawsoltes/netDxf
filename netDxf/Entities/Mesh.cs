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
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>
    /// Represents a mesh <see cref="EntityObject">entity</see>.
    /// </summary>
    /// <remarks>
    /// Use this entity to overcome the limitations of the PolyfaceMesh, but, keep in mind that this entity was first introduced in AutoCad 2010.<br/>
    /// The maximum number of faces a mesh can have is 16000000 (16 millions).
    /// </remarks>
    public partial class Mesh :
        EntityObject
    {
        #region private fields

        private const int MaxFaces = 16000000;
        private readonly List<Vector3> vertexes;
        private readonly List<int[]> faces;
        private readonly List<MeshEdge> edges;
        private byte subdivisionLevel;
        private bool blendCrease;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <c>Mesh</c> class.
        /// </summary>
        /// <param name="vertexes">Mesh vertex list.</param>
        /// <param name="faces">Mesh faces list.</param>
        public Mesh(IEnumerable<Vector3> vertexes, IEnumerable<int[]> faces)
            : this(vertexes, faces, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Mesh</c> class.
        /// </summary>
        /// <param name="vertexes">Mesh vertex list.</param>
        /// <param name="faces">Mesh faces list.</param>
        /// <param name="edges">Mesh edges list, this parameter is only really useful when it is required to assign creases values to edges.</param>
        public Mesh(IEnumerable<Vector3> vertexes, IEnumerable<int[]> faces, IEnumerable<MeshEdge> edges)
            : base(EntityType.Mesh, DxfObjectCode.Mesh)
        {
            if (vertexes == null)
            {
                throw new ArgumentNullException(nameof(vertexes));
            }
            this.vertexes = new List<Vector3>(vertexes);
            if (faces == null)
            {
                throw new ArgumentNullException(nameof(faces));
            }
            this.faces = new List<int[]>(faces);
            if (this.faces.Count > MaxFaces)
            {
                throw new ArgumentOutOfRangeException(nameof(faces), this.faces.Count, string.Format("The maximum number of faces in a mesh is {0}", MaxFaces));
            }
            this.edges = edges == null ? new List<MeshEdge>() : new List<MeshEdge>(edges);
            this.subdivisionLevel = 0;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets the mesh vertexes list.
        /// </summary>
        public List<Vector3> Vertexes
        {
            get { return this.vertexes; }
        }

        /// <summary>
        /// Gets the mesh faces list.
        /// </summary>
        public List<int[]> Faces
        {
            get { return this.faces; }
        }

        /// <summary>
        /// Gets the mesh edges list.
        /// </summary>
        public List<MeshEdge> Edges
        {
            get { return this.edges; }
        }

        /// <summary>Gets or sets the blend-crease flag, DXF group 72.</summary>
        /// <remarks>False is the default. A changed value clears stale common proxy graphics;
        /// assigning the same value preserves them. This stores subdivision metadata, not a smoothed mesh.</remarks>
        public bool BlendCrease
        {
            get { return this.blendCrease; }
            set
            {
                if (this.blendCrease == value) return;
                this.blendCrease = value;
                this.ClearProxyGraphics();
            }
        }

        /// <summary>
        /// Gets or sets the mesh subdivision level.
        /// </summary>
        /// <remarks>
        /// The valid range is from 0 to 255. The recommended range is 0-5 to prevent creating extremely dense meshes.
        /// Changed values clear stale common proxy graphics; this does not evaluate subdivision or generate replacement graphics.
        /// </remarks>
        public byte SubdivisionLevel
        {
            get { return this.subdivisionLevel; }
            set
            {
                if (this.subdivisionLevel == value) return;
                this.subdivisionLevel = value;
                this.ClearProxyGraphics();
            }
        }

        #endregion

        #region overrides

        /// <summary>
        /// Moves, scales, and/or rotates the current entity given a 3x3 transformation matrix and a translation vector.
        /// </summary>
        /// <param name="transformation">Transformation matrix.</param>
        /// <param name="translation">Translation vector.</param>
        /// <remarks>Matrix3 adopts the convention of using column vectors to represent a transformation matrix.</remarks>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            var prepared = VertexAffineTransform.Prepare(this.vertexes, base.Normal, transformation, translation);
            if (!prepared.Changed) return;
            // Bypass virtual accessors and finish validation before publishing.
            base.Normal = prepared.Normal;
            for (int i = 0; i < prepared.Points.Length; i++) this.vertexes[i] = prepared.Points[i];
            this.ClearProxyGraphics();
        }

        /// <summary>Applies a finite affine transform; projective input rejects before mutation.</summary>
        /// <param name="transformation">An affine four-by-four transformation matrix.</param>
        public override void TransformBy(Matrix4 transformation)
        {
            InfiniteLineTransform.CheckAffine(transformation);
            base.TransformBy(transformation);
        }

        /// <summary>
        /// Creates a new Mesh that is a copy of the current instance.
        /// </summary>
        /// <returns>A new Mesh that is a copy of this instance.</returns>
        public override object Clone()
        {
            List<Vector3> copyVertexes = new List<Vector3>(this.vertexes.Count);
            List<int[]> copyFaces = new List<int[]>(this.faces.Count);
            List<MeshEdge> copyEdges = null;

            copyVertexes.AddRange(this.vertexes);
            foreach (int[] face in this.faces)
            {
                int[] copyFace = new int[face.Length];
                face.CopyTo(copyFace, 0);
                copyFaces.Add(copyFace);
            }
            if (this.edges != null)
            {
                copyEdges = new List<MeshEdge>(this.edges.Count);
                foreach (MeshEdge meshEdge in this.edges)
                {
                    copyEdges.Add((MeshEdge) meshEdge.Clone());
                }
            }

            Mesh entity = new Mesh(copyVertexes, copyFaces, copyEdges)
            {
                //EntityObject properties
                Layer = (Layer) this.Layer.Clone(),
                Linetype = (Linetype) this.Linetype.Clone(),
                Color = (AciColor) this.Color.Clone(),
                Lineweight = this.Lineweight,
                Transparency = (Transparency) this.Transparency.Clone(),
                LinetypeScale = this.LinetypeScale,
                Normal = this.Normal,
                IsVisible = this.IsVisible,
                //Mesh properties
                SubdivisionLevel = this.subdivisionLevel,
                BlendCrease = this.BlendCrease
            };

            foreach (XData data in this.XData.Values)
                entity.XData.Add((XData) data.Clone());

            this.CopyCommonDataTo(entity);
            return entity;
        }

        #endregion
    }
}
