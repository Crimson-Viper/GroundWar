// The ground with hills on it. The colour of each patch (grass, wheat, woodland floor) comes in per vertex
// from MapView.BuildTerrain; this adds a little grain, faint 50 m grid lines for judging distance, and darkens
// everything outside the map's edge.
shader_type spatial;

uniform vec3 line_color : source_color = vec3(0.24, 0.27, 0.2);
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
	float bold = grid_line(p, 50.0, 0.14);
	float n = fract(sin(dot(floor(p * 0.7), vec2(12.9898, 78.233))) * 43758.5453);
	float n2 = fract(sin(dot(floor(p * 0.11), vec2(39.3468, 11.135))) * 24634.6345);
	// Vertex colours arrive as sRGB values; the renderer works in linear light.
	vec3 c = pow(COLOR.rgb, vec3(2.2)) * (0.93 + 0.09 * n + 0.06 * n2);
	c = mix(c, line_color, bold * 0.35);
	bool outside = p.x < 0.0 || p.y < 0.0 || p.x > map_size.x || p.y > map_size.y;
	if (outside) c *= 0.7;
	ALBEDO = c;
	ROUGHNESS = 0.95;
}
