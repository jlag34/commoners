// Standard lighting for a textured unit whose albedo alpha marks team-coloured cloth:
// there the colour is multiplied by _TeamColor (the commoner's tabard, see UnitGear).
// In Resources so builds include it for Shader.Find.
Shader "Swarm/TeamTint"
{
    Properties
    {
        _MainTex ("Albedo (A = team mask)", 2D) = "white" {}
        _TeamColor ("Team Color", Color) = (1, 1, 1, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.2
        _Metallic ("Metallic", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _TeamColor;
        half _Glossiness;
        half _Metallic;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = c.rgb * lerp(fixed3(1, 1, 1), _TeamColor.rgb, c.a);
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
