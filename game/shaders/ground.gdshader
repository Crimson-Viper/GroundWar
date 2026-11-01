// Grey test-range ground with 10 m grid lines and heavier 50 m lines, drawn from world position.
shader_type spatial;

uniform vec3 base_color : source_color = vec3(0.42, 0.43, 0.39);
uniform vec3 line_color : source_color = vec3(0.30, 0.31, 0.28);
uniform vec2 map_size = vec2(300.0, 200.0);

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float grid_line(vec2 p, float spacing, float width) {
	vec2 g = abs(fract(p / spacing - 0.5) - 0.5) * spacing;
	vec2 fw = fwidth(p) * 0.75;
	vec2 l = 1.0 - smoothstep(vec2(width) - fw, vec2(width) + fw, g);
	return max(l.x, l.y);
}

void fragment() {
	vec2 p = world_pos.xz;
	float fine = grid_line(p, 10.0, 0.06);
	float bold = grid_line(p, 50.0, 0.18);
	// gentle noise so the ground doesn't look like plastic
	float n = fract(sin(dot(floor(p * 0.5), vec2(12.9898, 78.233))) * 43758.5453) * 0.015;
	vec3 c = base_color + n;
	c = mix(c, line_color, fine * 0.5);
	c = mix(c, line_color * 0.8, bold * 0.8);
	bool outside = p.x < 0.0 || p.y < 0.0 || p.x > map_size.x || p.y > map_size.y;
	if (outside) c *= 0.7;
	ALBEDO = c;
	ROUGHNESS = 0.95;
}
