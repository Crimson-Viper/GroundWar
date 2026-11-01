// The "Team" parts of a soldier model (uniform, helmet), drawn for the whole battle in one MultiMesh.
// The model's own colour and texture are multiplied by the side's colour, which each soldier carries in
// his instance custom data (already converted to linear colour by SoldierCrowd). See docs/models.md.
shader_type spatial;
render_mode cull_back;

uniform vec4 albedo : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float roughness = 0.9;

varying vec3 team;

void vertex() {
	team = INSTANCE_CUSTOM.rgb;
}

void fragment() {
	vec4 t = texture(albedo_texture, UV);
	ALBEDO = albedo.rgb * t.rgb * team;
	ROUGHNESS = roughness;
}
