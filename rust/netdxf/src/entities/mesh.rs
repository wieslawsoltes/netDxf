use std::collections::HashMap;

use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::{EntityCommon, EntityObject};
use crate::entities::entity_type::EntityType;
use crate::entities::face3d::Face3D;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Represents an edge in a `Mesh` entity.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct MeshEdge {
    pub start_vertex_index: i32,
    pub end_vertex_index: i32,
    pub crease: f64,
}

/// Represents a DXF `MESH` (subdivision mesh) entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Mesh {
    pub common: EntityCommon,
    pub vertexes: Vec<Vector3>,
    pub faces: Vec<Vec<i32>>,
    pub edges: Vec<MeshEdge>,
    pub subdivision_level: u8,
}

impl Mesh {
    pub fn new(vertexes: Vec<Vector3>, faces: Vec<Vec<i32>>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Mesh, DxfObjectCode::MESH),
            vertexes,
            faces,
            edges: Vec::new(),
            subdivision_level: 0,
        }
    }

    pub fn insert_vertex(&mut self, index: usize, vertex: Vector3) {
        let idx = index.min(self.vertexes.len());
        self.vertexes.insert(idx, vertex);
        let idx_i32 = idx as i32;
        for face in &mut self.faces {
            for vi in face {
                if *vi >= idx_i32 {
                    *vi += 1;
                }
            }
        }
        for edge in &mut self.edges {
            if edge.start_vertex_index >= idx_i32 {
                edge.start_vertex_index += 1;
            }
            if edge.end_vertex_index >= idx_i32 {
                edge.end_vertex_index += 1;
            }
        }
    }

    pub fn move_vertex(&mut self, index: usize, delta: Vector3) -> bool {
        if let Some(v) = self.vertexes.get_mut(index) {
            *v += delta;
            true
        } else {
            false
        }
    }

    pub fn remove_vertex_at(&mut self, index: usize) -> Option<Vector3> {
        if index >= self.vertexes.len() {
            return None;
        }
        let removed = self.vertexes.remove(index);
        let idx_i32 = index as i32;
        self.faces.retain_mut(|face| {
            if face.contains(&idx_i32) {
                return false;
            }
            for vi in face.iter_mut() {
                if *vi > idx_i32 {
                    *vi -= 1;
                }
            }
            face.len() >= 3
        });
        self.edges.retain_mut(|edge| {
            if edge.start_vertex_index == idx_i32 || edge.end_vertex_index == idx_i32 {
                return false;
            }
            if edge.start_vertex_index > idx_i32 {
                edge.start_vertex_index -= 1;
            }
            if edge.end_vertex_index > idx_i32 {
                edge.end_vertex_index -= 1;
            }
            true
        });
        Some(removed)
    }

    pub fn insert_edge(&mut self, index: usize, edge: MeshEdge) {
        let idx = index.min(self.edges.len());
        self.edges.insert(idx, edge);
    }

    pub fn move_edge(&mut self, index: usize, delta: Vector3) -> bool {
        let Some(edge) = self.edges.get(index).copied() else {
            return false;
        };
        let a = edge.start_vertex_index as usize;
        let b = edge.end_vertex_index as usize;
        if a < self.vertexes.len() {
            self.vertexes[a] += delta;
        }
        if b < self.vertexes.len() && b != a {
            self.vertexes[b] += delta;
        }
        true
    }

    pub fn remove_edge_at(&mut self, index: usize) -> Option<MeshEdge> {
        if index < self.edges.len() {
            Some(self.edges.remove(index))
        } else {
            None
        }
    }

    pub fn insert_face(&mut self, index: usize, face: Vec<i32>) {
        let idx = index.min(self.faces.len());
        self.faces.insert(idx, face);
    }

    pub fn move_face(&mut self, index: usize, delta: Vector3) -> bool {
        let Some(face) = self.faces.get(index).cloned() else {
            return false;
        };
        let mut moved = Vec::new();
        for vi in face {
            let idx = vi as usize;
            if idx < self.vertexes.len() && !moved.contains(&idx) {
                self.vertexes[idx] += delta;
                moved.push(idx);
            }
        }
        true
    }

    pub fn remove_face_at(&mut self, index: usize) -> Option<Vec<i32>> {
        if index < self.faces.len() {
            Some(self.faces.remove(index))
        } else {
            None
        }
    }

    /// Sets or updates the crease sharpness for edge `index` (clamped to `>= 0.0`).
    pub fn blend_crease(&mut self, index: usize, crease: f64) -> bool {
        if let Some(edge) = self.edges.get_mut(index) {
            edge.crease = crease.max(0.0);
            true
        } else {
            false
        }
    }

    pub fn set_vertex(&mut self, index: usize, position: Vector3) -> bool {
        if let Some(v) = self.vertexes.get_mut(index) {
            *v = position;
            true
        } else {
            false
        }
    }

    pub fn set_vertexes(&mut self, vertexes: Vec<Vector3>) {
        self.vertexes = vertexes;
    }

    pub fn set_vertex_positions(&mut self, positions: &[Vector3]) -> bool {
        if positions.len() != self.vertexes.len() {
            return false;
        }
        self.vertexes.copy_from_slice(positions);
        true
    }

    pub fn set_edge_crease(&mut self, index: usize, crease: f64) -> bool {
        self.blend_crease(index, crease)
    }

    pub fn set_edge_vertex_indices(&mut self, index: usize, start_index: i32, end_index: i32) -> bool {
        if let Some(edge) = self.edges.get_mut(index) {
            edge.start_vertex_index = start_index;
            edge.end_vertex_index = end_index;
            true
        } else {
            false
        }
    }

    pub fn set_face_vertex_index(&mut self, face_index: usize, corner_index: usize, vertex_index: i32) -> bool {
        if let Some(face) = self.faces.get_mut(face_index) {
            if let Some(slot) = face.get_mut(corner_index) {
                *slot = vertex_index;
                return true;
            }
        }
        false
    }

    /// Explodes the unsubdivided control mesh into `Face3D` entities (fan-triangulating n-gons with >4 vertices).
    pub fn explode_control_mesh(&self) -> Vec<EntityObject> {
        emit_faces_as_face3d(&self.vertexes, &self.faces, &self.common)
    }

    /// Explodes the mesh into `Face3D` entities after applying `subdivision_level` Catmull-Clark steps.
    pub fn explode(&self) -> Vec<EntityObject> {
        if self.subdivision_level == 0 {
            return self.explode_control_mesh();
        }
        let mut verts = self.vertexes.clone();
        let mut faces = self.faces.clone();
        for _ in 0..self.subdivision_level.min(4) {
            (verts, faces) = catmull_clark_step(&verts, &faces);
        }
        emit_faces_as_face3d(&verts, &faces, &self.common)
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        for v in &mut self.vertexes {
            *v = transformation * *v + translation;
        }
    }

    pub fn bounding_rectangle(&self) -> Option<BoundingRectangle> {
        BoundingRectangle::from_points(self.vertexes.iter().map(|v| v.to_vector2()))
    }
}

fn emit_faces_as_face3d(
    verts: &[Vector3],
    faces: &[Vec<i32>],
    common: &EntityCommon,
) -> Vec<EntityObject> {
    let mut out = Vec::new();
    for face in faces {
        if face.len() < 3 {
            continue;
        }
        let pts: Vec<Vector3> = face
            .iter()
            .filter_map(|&idx| verts.get(idx as usize).copied())
            .collect();
        if pts.len() < 3 {
            continue;
        }
        if pts.len() == 3 {
            let mut f = Face3D::new(pts[0], pts[1], pts[2], pts[2]);
            f.common.layer = common.layer.clone();
            f.common.color = common.color;
            out.push(EntityObject::Face3D(Box::new(f)));
        } else if pts.len() == 4 {
            let mut f = Face3D::new(pts[0], pts[1], pts[2], pts[3]);
            f.common.layer = common.layer.clone();
            f.common.color = common.color;
            out.push(EntityObject::Face3D(Box::new(f)));
        } else {
            for i in 1..(pts.len() - 1) {
                let mut f = Face3D::new(pts[0], pts[i], pts[i + 1], pts[i + 1]);
                f.common.layer = common.layer.clone();
                f.common.color = common.color;
                out.push(EntityObject::Face3D(Box::new(f)));
            }
        }
    }
    out
}

fn catmull_clark_step(verts: &[Vector3], faces: &[Vec<i32>]) -> (Vec<Vector3>, Vec<Vec<i32>>) {
    let mut new_verts = verts.to_vec();
    let mut face_points = Vec::with_capacity(faces.len());
    let mut face_point_indices = Vec::with_capacity(faces.len());

    // 1. Face points
    for face in faces {
        let mut sum = Vector3::ZERO;
        let mut count = 0.0;
        for &vi in face {
            if let Some(&v) = verts.get(vi as usize) {
                sum += v;
                count += 1.0;
            }
        }
        let fp = if count > 0.0 { sum / count } else { Vector3::ZERO };
        face_points.push(fp);
        face_point_indices.push(new_verts.len() as i32);
        new_verts.push(fp);
    }

    // 2. Edge points & adjacency
    let mut edge_faces: HashMap<(i32, i32), Vec<usize>> = HashMap::new();
    for (fi, face) in faces.iter().enumerate() {
        let m = face.len();
        for i in 0..m {
            let a = face[i];
            let b = face[(i + 1) % m];
            let key = if a < b { (a, b) } else { (b, a) };
            edge_faces.entry(key).or_default().push(fi);
        }
    }
    let mut edge_point_index: HashMap<(i32, i32), i32> = HashMap::new();
    for (&(a, b), adj_faces) in &edge_faces {
        let va = verts.get(a as usize).copied().unwrap_or(Vector3::ZERO);
        let vb = verts.get(b as usize).copied().unwrap_or(Vector3::ZERO);
        let ep = if adj_faces.len() == 2 {
            (va + vb + face_points[adj_faces[0]] + face_points[adj_faces[1]]) * 0.25
        } else {
            (va + vb) * 0.5
        };
        edge_point_index.insert((a, b), new_verts.len() as i32);
        new_verts.push(ep);
    }

    // 3. Update original vertices
    let mut vert_faces: Vec<Vec<usize>> = vec![Vec::new(); verts.len()];
    for (fi, face) in faces.iter().enumerate() {
        for &vi in face {
            if let Some(slot) = vert_faces.get_mut(vi as usize) {
                slot.push(fi);
            }
        }
    }
    let mut vert_edges: Vec<Vec<(i32, i32)>> = vec![Vec::new(); verts.len()];
    for &(a, b) in edge_faces.keys() {
        if let Some(slot) = vert_edges.get_mut(a as usize) {
            slot.push((a, b));
        }
        if let Some(slot) = vert_edges.get_mut(b as usize) {
            slot.push((a, b));
        }
    }
    for (vi, &p) in verts.iter().enumerate() {
        let n = vert_faces[vi].len();
        let e_count = vert_edges[vi].len();
        if n >= 3 && n == e_count {
            let f_avg = vert_faces[vi]
                .iter()
                .fold(Vector3::ZERO, |acc, &fi| acc + face_points[fi])
                / n as f64;
            let r_avg = vert_edges[vi].iter().fold(Vector3::ZERO, |acc, &(a, b)| {
                let va = verts.get(a as usize).copied().unwrap_or(Vector3::ZERO);
                let vb = verts.get(b as usize).copied().unwrap_or(Vector3::ZERO);
                acc + (va + vb) * 0.5
            }) / e_count as f64;
            let nf = n as f64;
            new_verts[vi] = (f_avg + r_avg * 2.0 + p * (nf - 3.0)) / nf;
        }
    }

    // 4. Build subdivided quad faces
    let mut new_faces = Vec::new();
    for (fi, face) in faces.iter().enumerate() {
        let m = face.len();
        let fpi = face_point_indices[fi];
        for i in 0..m {
            let v_curr = face[i];
            let v_next = face[(i + 1) % m];
            let v_prev = face[(i + m - 1) % m];
            let k_next = if v_curr < v_next {
                (v_curr, v_next)
            } else {
                (v_next, v_curr)
            };
            let k_prev = if v_prev < v_curr {
                (v_prev, v_curr)
            } else {
                (v_curr, v_prev)
            };
            if let (Some(&ep_next), Some(&ep_prev)) =
                (edge_point_index.get(&k_next), edge_point_index.get(&k_prev))
            {
                new_faces.push(vec![v_curr, ep_next, fpi, ep_prev]);
            }
        }
    }

    (new_verts, new_faces)
}
