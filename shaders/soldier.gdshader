// Per-instance procedural animation for the M0 performance test.
// Stands in for vertex-animation textures (VAT): real VAT adds one or two
// texture reads per vertex, which is cheap next to everything else here.
//
// INSTANCE_CUSTOM: x = anim phase, y = state (0 idle,1 walk,2 attack,3 dead),
//                  z = time in state (s), w = team (0/1)
// UV.x = body part id, UV.y = 1 for team cloth. COLOR = base colour.
shader_type spatial;
render_mode cull_back;

uniform vec3 team_a : source_color = vec3(0.16, 0.30, 0.72); // Calderran blue
uniform vec3 team_b : source_color = vec3(0.70, 0.16, 0.12); // Valdoran red (placeholder)

varying vec3 v_albedo;

vec3 rot_x(vec3 p, vec3 pivot, float a) {
	vec3 q = p - pivot;
	float c = cos(a), s = sin(a);
	return vec3(q.x, q.y * c - q.z * s, q.y * s + q.z * c) + pivot;
}

void vertex() {
	vec4 cd = INSTANCE_CUSTOM;
	float phase = cd.x;
	int state = int(cd.y + 0.5);
	float st = cd.z;
	int part = int(UV.x + 0.5);

	vec3 v = VERTEX;
	vec3 n = NORMAL;
	float walk = (state == 1) ? 1.0 : 0.0;
	float swing = sin(phase) * 0.55 * walk;

	if (part == 2) { v = rot_x(v, vec3(0.0, 0.88, 0.0), swing); n = rot_x(n, vec3(0.0), swing); }
	if (part == 3) { v = rot_x(v, vec3(0.0, 0.88, 0.0), -swing); n = rot_x(n, vec3(0.0), -swing); }
	if (part == 4) {
		if (state == 2) {
			float t = clamp(st / 0.45, 0.0, 1.0);
			v.z -= sin(t * PI) * 0.55; // spear thrust
		} else {
			v = rot_x(v, vec3(0.0, 1.55, 0.0), -swing * 0.25);
		}
	}
	if (part == 5) { v = rot_x(v, vec3(0.0, 1.55, 0.0), swing * 0.15); }

	// body bob while walking
	if (part != 2 && part != 3) v.y += abs(sin(phase)) * 0.05 * walk;

	// death: topple backwards around the feet
	if (state == 3) {
		float t = clamp(st / 0.8, 0.0, 1.0);
		float a = t * t * 1.5;
		v = rot_x(v, vec3(0.0), a);
		n = rot_x(n, vec3(0.0), a);
	}

	VERTEX = v;
	NORMAL = n;
	vec3 team = cd.w < 0.5 ? team_a : team_b;
	v_albedo = UV.y > 0.5 ? team * COLOR.rgb : COLOR.rgb;
}

void fragment() {
	ALBEDO = v_albedo;
	ROUGHNESS = 0.75;
}
