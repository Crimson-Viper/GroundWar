// Floating bar over each soldier: top part = morale (colour = morale state), bottom strip = suppression.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

instance uniform float morale = 1.0;
instance uniform vec4 bar_color : source_color = vec4(0.3, 0.9, 0.3, 1.0);
instance uniform float suppression = 0.0;

void vertex() {
	// Always face the camera, keep the node's scale.
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * length(MODEL_MATRIX[0].xyz),
		INV_VIEW_MATRIX[1] * length(MODEL_MATRIX[1].xyz), INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec3 c;
	if (UV.y < 0.62) {
		c = UV.x < morale ? bar_color.rgb : vec3(0.08);
	} else {
		c = UV.x < suppression ? vec3(1.0, 0.82, 0.2) : vec3(0.16);
	}
	// thin dark border
	if (UV.x < 0.02 || UV.x > 0.98 || UV.y < 0.06 || UV.y > 0.94) c = vec3(0.02);
	ALBEDO = c;
}
