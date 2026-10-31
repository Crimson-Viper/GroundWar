// Floating bar over each soldier: top part = morale (colour = morale state), bottom strip = suppression.
// Drawn as one MultiMesh for the whole battle: each bar's colour is the instance colour,
// and its custom data holds (morale 0-1, suppression 0-1, unused, unused).
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

varying vec4 bar_color;
varying vec2 levels;

vec3 to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void vertex() {
	bar_color = COLOR;
	levels = INSTANCE_CUSTOM.xy;
	// Always face the camera, keep the instance's scale.
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * length(MODEL_MATRIX[0].xyz),
		INV_VIEW_MATRIX[1] * length(MODEL_MATRIX[1].xyz), INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec3 c;
	if (UV.y < 0.62) {
		c = UV.x < levels.x ? to_linear(bar_color.rgb) : vec3(0.08);
	} else {
		c = UV.x < levels.y ? vec3(1.0, 0.82, 0.2) : vec3(0.16);
	}
	// thin dark border
	if (UV.x < 0.02 || UV.x > 0.98 || UV.y < 0.06 || UV.y > 0.94) c = vec3(0.02);
	ALBEDO = c;
}
