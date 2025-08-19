#version 430
layout(local_size_x = 16, local_size_y = 16) in;

layout(binding = 0, rgba32f) writeonly uniform image2D outImage;

// Camera UBO (std140, binding=1)
layout(std140, binding = 1) uniform Camera {
    vec3 camPos;     float _pad0;
    vec3 camRight;   float _pad1;
    vec3 camUp;      float _pad2;
    vec3 camForward; float _pad3;
    float tanHalfFov;
    float aspect;
    int   moving;
    int   _pad4;
} cam;

// Disk UBO (std140, binding=2)
layout(std140, binding = 2) uniform Disk {
    float disk_r1;   // meters
    float disk_r2;   // meters    
};

// geodesic tracing buffer. select up to MAX_PATHS pixels to record
const int MAX_PATHS = 5;
const int MAX_PATHS_POINTS = 5000;
layout(std430, binding = 4) buffer PathsSSBO {
    // flat array of positions for all paths (RS units)
    vec4 pathPoints[MAX_PATHS * MAX_PATHS_POINTS]; 
    int  pathPointCounts[MAX_PATHS];        // how many pathPoints written per path
};

layout(binding = 5) uniform sampler2D uEnvTexture;  
uniform float uEnvIntensity;                 // e.g., 1.0–2.0

// geodesic tracing params
uniform int   uNumPaths;
uniform ivec2 uPathPix[MAX_PATHS]; // pixel coords to trace (in compute texture space)
uniform int   uPathStride;         // record every N integration steps (e.g., 20)

// Uniforms from host
uniform float uScaledRS;       // The Schwa radius scaled by user for num. stability. The length unit for all compute.
float uRs = uScaledRS; //debug
uniform int   uSteps;          // e.g. 3000..8000 when still
uniform float uDLambdaBase;    // base step in RS units, e.g. 2e-4
uniform float uEscapeR;        // escape radius in RS, e.g. 2000

// Guards
const float EPS_R   = 1e-19;
const float EPS_SIN = 1e-19;

// State:
// r, ct=cos(theta), st=sin(theta), cp=cos(phi), sp=sin(phi),
// rp=d r/dl, tp=d theta/dl, pp=d phi/dl, E
struct State {
    float r, ct, st, cp, sp;
    float rp, tp, pp;
    float E;
};

bool isBadF(float f){ return isnan(f) || isinf(f); }
bool isBadV(vec3 v){ return any(isnan(v)) || any(isinf(v)); }

float applyEpsilon(float f, float eps)
{
	if (sign(f) == 0.0) return eps;
    if (abs(f) < eps) return eps;// * sign(f);
	return f;
}

vec3 toCartesian(float r, float st, float ct, float cp, float sp)
{	
    return vec3(r * st * cp, r * st * sp, r * ct);
}

void renorm(inout float a, inout float b)
{
    float L = max(sqrt(a*a + b*b), 1e-12);
    a /= L; b /= L;
}

float localDLambda(float r)
{
    // adaptive length step
    float scale = 1.0;
    float rs = r / uRs;
    if (rs < 1.5)
        scale = 0.02;
    if (rs < 1.8)
        scale = 0.04;
    if (rs < 3.0)
        scale = 1;
    else
        scale = 4.0;
    return uDLambdaBase * scale;
}

// Geodesic RHS in Schwarzschild with RS=1
// y  = (r, ct, st, cp, sp, rp, tp, pp)
// y' = (rp, ct', st', cp', sp', rpp, tpp, ppp)
// where ct' = -st*tp, st' = ct*tp, cp' = -sp*pp, sp' = cp*pp
void evalRHS(in State s, out vec3 d1, out vec3 d2)
{
    float r = s.r; 
    float st = s.st;
    float ct = s.ct;

    float f =  1.0 - uRs / applyEpsilon(r, EPS_R);
    float dt_dlambda = s.E / f;

    float dr = s.rp;
    float dth = s.tp;
    float dph = s.pp;

    // First derivatives of (r,theta,phi) part:
    // We do not return theta/phi directly; k1 for angles is handled in integrator via ct',st',cp',sp'
    d1 = vec3(dr, dth, dph);

    // Second derivatives
    d2.x = - (uRs / (2.0 * r * r)) * f * dt_dlambda * dt_dlambda
        + (uRs / (2.0 * r * r * f)) * dr * dr
         + r * (dth*dth + st*st * dph*dph);

    d2.y = -2.0*dr*dth/r + st*ct*dph*dph;

    d2.z = -2.0 * dr * dph / r - 2.0 * ct / applyEpsilon(st, EPS_SIN) * dth * dph;

}

// Classic RK4 but advancing ct,st,cp,sp instead of theta/phi directly
void rk4(inout State s, float h)
{
    float halfh = 0.5 * h;
    // k1
    vec3 k1a, k1b;
    evalRHS(s, k1a, k1b);
    float k1_ct = -s.st * s.tp;
    float k1_st =  s.ct * s.tp;
    float k1_cp = -s.sp * s.pp;
    float k1_sp =  s.cp * s.pp;

    // s2
    State s2 = s;
    s2.r += halfh * k1a.x;
    s2.ct += halfh * k1_ct;
    s2.st += halfh * k1_st;
    s2.cp += halfh * k1_cp;
    s2.sp += halfh * k1_sp;
    s2.rp += halfh *k1b.x;
    s2.tp += halfh *k1b.y;
    s2.pp += halfh *k1b.z;

    vec3 k2a, k2b;
    evalRHS(s2, k2a, k2b);
    float k2_ct = -s2.st * s2.tp;
    float k2_st =  s2.ct * s2.tp;
    float k2_cp = -s2.sp * s2.pp;
    float k2_sp =  s2.cp * s2.pp;

    // s3
    State s3 = s;
    s3.r += halfh *k2a.x;
    s3.ct += halfh * k2_ct;
    s3.st += halfh * k2_st;
    s3.cp += halfh * k2_cp;
    s3.sp += halfh * k2_sp;
    s3.rp += halfh *k2b.x;
    s3.tp += halfh *k2b.y;
    s3.pp += halfh *k2b.z;

    vec3 k3a, k3b;
    evalRHS(s3, k3a, k3b);
    float k3_ct = -s3.st * s3.tp;
    float k3_st =  s3.ct * s3.tp;
    float k3_cp = -s3.sp * s3.pp;
    float k3_sp =  s3.cp * s3.pp;

    // s4
    State s4 = s;
    s4.r  += h*k3a.x;
    s4.ct += h*k3_ct; s4.st += h*k3_st;
    s4.cp += h*k3_cp; s4.sp += h*k3_sp;
    s4.rp += h*k3b.x;
    s4.tp += h*k3b.y;
    s4.pp += h*k3b.z;

    vec3 k4a, k4b;
    evalRHS(s4, k4a, k4b);
    float k4_ct = -s4.st * s4.tp;
    float k4_st =  s4.ct * s4.tp;
    float k4_cp = -s4.sp * s4.pp;
    float k4_sp =  s4.cp * s4.pp;

    // combine
    float sixth = h / 6.0;
    s.r += sixth *(k1a.x + 2.0*k2a.x + 2.0*k3a.x + k4a.x);
    s.ct += sixth *(k1_ct + 2.0*k2_ct + 2.0*k3_ct + k4_ct);
    s.st += sixth *(k1_st + 2.0*k2_st + 2.0*k3_st + k4_st);
    s.cp += sixth *(k1_cp + 2.0*k2_cp + 2.0*k3_cp + k4_cp);
    s.sp += sixth *(k1_sp + 2.0*k2_sp + 2.0*k3_sp + k4_sp);
    s.rp += sixth *(k1b.x + 2.0*k2b.x + 2.0*k3b.x + k4b.x);
    s.tp += sixth *(k1b.y + 2.0*k2b.y + 2.0*k3b.y + k4b.y);
    s.pp += sixth *(k1b.z + 2.0*k2b.z + 2.0*k3b.z + k4b.z);

    // renormalize sine/cosine pairs to kill drift and avoid branch issues
    //renorm(s.ct, s.st);
    //renorm(s.cp, s.sp);
}

// Build initial state from posRS and dir
State initState(vec3 posRS, vec3 dir)
{
    State s;

    // spherical from position 
	s.r = length(posRS);
    s.ct = clamp(posRS.z / s.r, -1.0, 1.0);
	s.st = sqrt(max(1.0 - s.ct * s.ct, 0.0));

    float rxy = max(length(posRS.xy), 1e-12);
    s.cp = posRS.x / rxy;
    s.sp = posRS.y / rxy;

    // derivatives
    float dx = dir.x, dy = dir.y, dz = dir.z;
    s.rp =  s.st*s.cp*dx + s.st*s.sp*dy + s.ct*dz;

    // theta dot and phi dot use st,ct,cp,sp directly
    s.tp = (s.ct * s.cp * dx + s.ct * s.sp * dy - s.st * dz) / applyEpsilon(s.r, EPS_R);
    s.pp = (-s.sp * dx + s.cp * dy) / applyEpsilon(s.r * s.st, EPS_SIN);

    // conserved E
    float f = 1.0 - uRs / applyEpsilon(s.r, EPS_R);
    float dt_dlambda = sqrt(max((s.rp * s.rp) / f + s.r * s.r * (s.tp * s.tp + s.st * s.st * s.pp * s.pp), 0.0));
    s.E = f * dt_dlambda;

    return s;
}


bool crossesDisk2(vec3 a, vec3 b, float r1, float r2)
{
    float ya = (abs(a.y) < 1e-5) ? 0.0 : a.y;
    float yb = (abs(b.y) < 1e-5) ? 0.0 : b.y;
    if (ya * yb >= 0.02) return false;
    float r = length(b.xz);
    return (r >= r1 && r <= r2);
}

// quick and smooth for thin disk
bool crossesDisk(vec3 a, vec3 b, float r1, float r2)
{
    // Early out if segment is nearly parallel to plane y=0
    float ya = a.y, yb = b.y;
    float denom = ya - yb;
    if (abs(denom) < EPS_R) return false;

	// Must straddle the plane (strictly)
	if (ya * yb > 0.0) return false;

    // Solve for t in [0,1]: a.y + t*(b.y - a.y) = 0
    float t = ya / (ya - yb);
	// Reject grazing hits right at the endpathPoints to avoid seam flicker
    if (t <= EPS_R || t >= 1.0 - EPS_R) return false;

    // Intersection point on the segment
    vec3 p = mix(a, b, t);
    float r = length(p.xz);


    return (r >= r1 + EPS_R) && (r <= r2 - EPS_R);
}

// Hash function for pseudo-random numbers in [0,1)
float rand(vec3 p) {
    return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
}

void main()
{
    // runs for each compute pixel in parallel.

    ivec2 pix = ivec2(gl_GlobalInvocationID.xy);
    ivec2 sz = imageSize(outImage);
    if (pix.x >= sz.x || pix.y >= sz.y) return;

    // check if we need to sample this ray for path visualization
    int pathId = -1;
    // use trace points from host
    for (int i = 0; i < uNumPaths; ++i) {
        if (pix == uPathPix[i]) { pathId = i; break; }
    }
    //pathId = 0;

    // camera ray
    float u = (2.0 * (pix.x + 0.5) / float(sz.x) - 1.0) * cam.aspect * cam.tanHalfFov;
    float v = (1.0 - 2.0 * (pix.y + 0.5) / float(sz.y)) * cam.tanHalfFov;	

	vec3 rgt = cam.camRight;
	vec3 upp = cam.camUp;
	vec3 rd = normalize(u * rgt + v * upp + cam.camForward);

    State s = initState(cam.camPos, rd);
    vec3 prevPos = toCartesian(s.r, s.st, s.ct, s.cp, s.sp);

    bool hitBH = false;
    bool hitDisk = false;
    bool hitBrick = false;
    bool rayEscaped = false;
    bool rayDiverged = false;

    vec3 textureSample = vec3(0.0);

    vec4 currentPathPoints[MAX_PATHS_POINTS];

    // TRACE RAY
    float lengthIntegerated = 0;
    float stepsIntegerated = 0;
    float curvatureIntegerated = 0;
    for (int i = 0; i < uSteps; ++i)
    {
        // Integrare - advance along the geodesic
        float h = localDLambda(s.r);
        rk4(s, h);
        stepsIntegerated++;
        lengthIntegerated += h;
        vec3 curPos = toCartesian(s.r, s.st, s.ct, s.cp, s.sp);

        curvatureIntegerated += abs(s.tp) + abs(s.pp);

        // Sample the path
        if (pathId >= 0) {
            // record every Nth step to keep buffers small
            if ((i % uPathStride) == 0) {
                int idx = pathPointCounts[pathId]; // current count
                if (idx < MAX_PATHS_POINTS) {
                    pathPoints[pathId * MAX_PATHS_POINTS + idx] = vec4(curPos, 1.0);
                    pathPointCounts[pathId] = idx + 1;
                }
            }
        }

        //
        // HIT TESTS
        //

        if (isBadF(s.r) || isBadV(curPos)) {
            rayDiverged = true;
            break;
        }      

        if (disk_r1 > 0 && crossesDisk(prevPos, curPos, disk_r1, disk_r2)) {
            hitDisk = true;
            //break;
            //don't break so it's translucent 
        }
        prevPos = curPos;

        if (
            //abs(s.r) < EPS_R
            abs(s.r) < uRs
        ) {
            hitBH = true;
            break;
        }
        // Bricks
        //if ((abs(s.r - 7 * uRs) < 0.1 * uRs)
        //    && mod(abs(s.ct - 0), 0.3) < 0.05
        //    && mod(abs(s.cp - 1), 0.35) < 0.06)
        //{
        //    hitBrick = true;
        //    //break;
        //}

        // sample environment texture
        vec3 dirCurPos = normalize(curPos);

        float u = atan(dirCurPos.z, dirCurPos.x) * (0.15915494309189535) + 0.5; // 1/(2pi)
        float v = acos(clamp(dirCurPos.y, -1.0, 1.0)) * 0.3183098861837907; // 1/pi

        float uEnvTiles = 2; // how many repeats across U and V
        vec2 uv = vec2(u, v) * uEnvTiles;
        uv = fract(uv); // wrap into [0,1) : for Tiling

        // level of detail
        vec3 env = textureLod(uEnvTexture, uv, cam.moving == 1 ? 0.5 : 0.0).rgb;
        textureSample = env;

        if (s.r > uEscapeR) { 
            rayEscaped = true;
            break;
        }


    } // END tracing


    //  Reference rings at RS (and 1.5*RS) (screen-space)
    if (false) {
		// camera distance from origin in RS units
		float R0_RS = length(cam.camPos);

		// angular radii (Euclidean reference)
		float alpha1 = asin(clamp(uRs / max(R0_RS, 1e-6), 0.0, 1.0));        // r = RS
		float alpha2 = asin(clamp(1.5 * uRs / max(R0_RS, 1e-6), 0.0, 1.0));  // r = 1.5 RS
        float alpha3 = asin(clamp(sqrt(27)/2 * uRs / max(R0_RS, 1e-6), 0.0, 1.0));  // r = 1.5 RS


		// pixel-size angular band (approx): vertical FOV / image height
		float vFov = 2.0 * atan(cam.tanHalfFov);
		ivec2 sz = imageSize(outImage);
		float band = 0.5 * (vFov / float(max(sz.y, 1))); // ~1 pixels thick

		// current pixel's angle from the forward axis
		float ang = acos(clamp(dot(rd, cam.camForward), -1.0, 1.0));

        // draw the rings on top of whatever we computed
        if (abs(ang - alpha1) < band) {
            imageStore(outImage, pix, vec4(1.0, 0.0, 0.0, 0.7)); // at RS
            return;
        }
        if (abs(ang - alpha2) < band) {
            imageStore(outImage, pix, vec4(0.0, 0.0, 1.0, 0.7)); //  1.5 RS
            return;
        }
        if (abs(ang - alpha3) < band) {
            imageStore(outImage, pix, vec4(0.8, 0.0, 0.8, 0.7)); // 2.6 RS
            return;
        }

    }


    // Output colors

    vec4 brickColor = vec4(0.3, 0.5, 1.0, 0.55);
    vec4 bhColor = vec4(0, 0, 0, 0.3);
    vec4 escapedColor = vec4(0, 0, 0, 1);
    vec4 divergedColor = vec4(0, 0, 0, 1);

    vec4 outCol = vec4(textureSample, 1.0);

    float r = length(toCartesian(s.r, s.st, s.ct, s.cp, s.sp).xz) / max(disk_r2 * 2.8, 1e-8);

    r = 1.0;
    vec4 diskColor = vec4(clamp(r, 0.4, 1.0), clamp(r, 0.4, 1.0)/1.5, 0.0, 0.25);

    if (hitDisk) {
        outCol += diskColor * 0.5;
    }
    //if (hitBrick) {
    //    outCol += diskColor *0.3;
    //}
    if (rayEscaped) {
        outCol += outCol * 0.1;
    }
    if (hitBH) {
        outCol += outCol * 0.2 ;
    }    
    if (rayDiverged) {
        outCol += outCol * 0.1;
    }
    
    
    imageStore(outImage, pix, outCol);

    return;


    // Camera-space reference circle (should stay a circle at any yaw)
    if (false) {
    float r_uv = length(vec2(u, v));
    float r_ref = 0.6 * cam.tanHalfFov;        // 60% of half-FOV
    float band = 1.5 * (2.0 * atan(cam.tanHalfFov) / float(max(imageSize(outImage).y, 1)));
        if (abs(r_uv - r_ref) < band) {
            imageStore(outImage, pix, vec4(1.0, 0.0, 1.0, 1.0)); // magenta ring
            return;
        }
    }
}
