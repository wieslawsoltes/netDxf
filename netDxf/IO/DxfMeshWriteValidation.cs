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
using netDxf.Blocks;
using netDxf.Entities;

namespace netDxf.IO
{
    internal partial class DxfWriter
    {
        private void ValidateMeshOutput()
        {
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                {
                    Mesh mesh = entity as Mesh;
                    if (mesh != null) ValidateMeshOutput(mesh, block.Name);
                }
        }

        private static void ValidateMeshOutput(Mesh mesh, string blockName)
        {
            // Check list sizes before walking any indices. A caller can share
            // one large face array thousands of times without allocating a
            // correspondingly large mesh; its serialized size still overflows.
            long faceListSize = mesh.Faces.Count;
            if (mesh.Faces.Count > 16000000)
                throw InvalidMeshOutput(blockName, 93, "face count exceeds the typed model limit of 16000000");
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                int[] face = mesh.Faces[i];
                if (face == null || face.Length < 3)
                    throw InvalidMeshOutput(blockName, 93, "face " + i + " must contain at least three vertex indices");
                faceListSize += face.Length;
                if (faceListSize > int.MaxValue)
                    throw InvalidMeshOutput(blockName, 93, "serialized face-list size exceeds Int32.MaxValue");
            }

            for (int i = 0; i < mesh.Vertexes.Count; i++)
            {
                Vector3 vertex = mesh.Vertexes[i];
                if (!IsFiniteMeshOutput(vertex.X) || !IsFiniteMeshOutput(vertex.Y) || !IsFiniteMeshOutput(vertex.Z))
                    throw InvalidMeshOutput(blockName, 10, "vertex " + i + " must have finite 10/20/30 coordinates");
            }
            for (int i = 0; i < mesh.Faces.Count; i++)
                foreach (int index in mesh.Faces[i])
                    if (index < 0 || index >= mesh.Vertexes.Count)
                        throw InvalidMeshOutput(blockName, 90, "face " + i + " references a missing vertex");

            for (int i = 0; i < mesh.Edges.Count; i++)
            {
                MeshEdge edge = mesh.Edges[i];
                if (edge == null)
                    throw InvalidMeshOutput(blockName, 94, "edge " + i + " is null");
                if (edge.StartVertexIndex < 0 || edge.StartVertexIndex >= mesh.Vertexes.Count ||
                    edge.EndVertexIndex < 0 || edge.EndVertexIndex >= mesh.Vertexes.Count)
                    throw InvalidMeshOutput(blockName, 90, "edge " + i + " references a missing vertex");
                if (!IsFiniteMeshOutput(edge.Crease))
                    throw InvalidMeshOutput(blockName, 140, "edge " + i + " must have a finite crease value");
            }
        }

        private static bool IsFiniteMeshOutput(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static InvalidOperationException InvalidMeshOutput(string blockName, int group, string message)
        {
            return new InvalidOperationException("MESH output in block '" + blockName + "', group " + group + ": " + message + ".");
        }
    }
}
