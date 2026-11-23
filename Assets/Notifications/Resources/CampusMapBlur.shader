Shader "Hidden/PixelCampus/MapBlur"
{
    Properties { _MainTex ("Map", 2D) = "white" {} }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _BlurStep;

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float2 step = _BlurStep.xy;
                return tex2D(_MainTex, uv) * 0.40
                    + (tex2D(_MainTex, uv - step * 1.35) + tex2D(_MainTex, uv + step * 1.35)) * 0.24
                    + (tex2D(_MainTex, uv - step * 3.20) + tex2D(_MainTex, uv + step * 3.20)) * 0.06;
            }
            ENDCG
        }
    }
}
