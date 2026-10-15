sampler2D scene : register(s0);
float4 bounds : register(c0); // panel origin + size in scene UVs
float4 shape : register(c1); // width, height, corner radius, edge band
float4 optics : register(c2); // refraction pixels, blur pixels, tint amount, dark
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 p = (uv-.5)*shape.xy;
    float2 q = abs(p)-(shape.xy*.5-shape.z);
    float2 outside = max(q,0);
    float dist = length(outside)+min(max(q.x,q.y),0)-shape.z;
    float2 n = outside / max(length(outside),.001);
    n = (q.x>q.y ? float2(1,0) : float2(0,1))*(1-step(.001,length(outside))) + n*step(.001,length(outside));
    n *= sign(p);
    float edge = saturate(1+dist/shape.w);
    float2 refracted = uv - n*(optics.x*edge*edge)/shape.xy;
    float2 center = bounds.xy+refracted*bounds.zw;
    float2 blur = optics.y*bounds.zw/shape.xy;
    float4 c = tex2D(scene,center)*.20;
    c += (tex2D(scene,center+float2(blur.x,0))+tex2D(scene,center-float2(blur.x,0))
        +tex2D(scene,center+float2(0,blur.y))+tex2D(scene,center-float2(0,blur.y)))*.12;
    c += (tex2D(scene,center+blur)+tex2D(scene,center-blur)
        +tex2D(scene,center+float2(blur.x,-blur.y))+tex2D(scene,center+float2(-blur.x,blur.y)))*.08;
    float2 dispersion = n*(optics.x*.055*edge)*bounds.zw/shape.xy;
    c.r = lerp(c.r,tex2D(scene,center+dispersion).r,edge*.35);
    c.b = lerp(c.b,tex2D(scene,center-dispersion).b,edge*.35);
    c.rgb = lerp(c.rgb,lerp(float3(.97,.98,1),float3(.065,.08,.11),optics.w),optics.z);
    float rim = saturate(1-abs(dist+1.0)/1.4);
    float shine = saturate(dot(n,normalize(float2(-.6,-.8))))*.8+.15;
    c.rgb = lerp(c.rgb,float3(1,1,1),rim*shine*.65);
    c.a = saturate(-dist+.5);
    c.rgb *= c.a;
    return c;
}
