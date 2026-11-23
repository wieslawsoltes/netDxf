pub mod helpers;

// Math, color, transparency, and XData
mod arbitrary_axis_review_tests;
mod g_vector_extremes_tests;
mod matrix_identity_review_tests;
mod transparency_stored_tests;
mod true_color_wire_review_tests;
mod vector4_component_review_tests;
mod x_data_clone_tests;

// Header, tables, blocks, and objects
mod custom_header_unicode_tests;
mod geo_data_tests;
mod header_comment_tests;
mod header_probe_tests;
mod sun_tests;
mod ucs_base_tests;

// Entities, transforms, dimensions, and splines
mod arc_angle_fidelity_tests;
mod entity_affine_tests;
mod face3d_edge_visibility_tests;
mod hatch_and_dimension_tests;
mod m_text_unicode_chunk_tests;
mod spline_degree_elevation_tests;
mod spline_knot_insertion_tests;
mod spline_reversal_tests;
mod spline_split_tests;
mod spline_trim_tests;

// ACIS SAT, table formulas, and fields
mod acis_sat_tests;
mod standard_field_evaluator_tests;
mod table_calculation_tests;

// ASCII and binary codecs
mod binary_chunk_tests;
mod binary_sentinel_tests;
mod strict_binary_value_tests;
mod strict_text_value_tests;
mod text_hex_tests;
