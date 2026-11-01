// Soft round smoke puff on a camera-facing quad. Colour and fade come from the MultiMesh instance colour.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

varying vec4 tint;

void vertex() {
	tint = COLOR;
	// Face the camera, keeping the instance's scale.
	float s = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * s, INV_VIEW_MATRIX[1] * s, INV_VIEW_MATRIX[2] * s, MODEL_MATRIX[3]);
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float d = dot(p, p);
	// Lumpy edge so puffs don't look like discs.
	float lump = 0.12 * sin(atan(p.y, p.x) * 5.0 + tint.r * 40.0);
	float a = smoothstep(1.0 + lump, 0.15, d);
	ALBEDO = tint.rgb * (0.85 + 0.15 * (1.0 - p.y));
	ALPHA = a * tint.a;
}
