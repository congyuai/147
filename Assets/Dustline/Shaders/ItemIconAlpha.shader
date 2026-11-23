Shader "Hidden/Dustline/Item Icon Alpha"
{
    Properties { _MainTex ("Black", 2D) = "black" {} _WhiteTex ("White", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _WhiteTex;
            half4 frag(v2f_img input) : SV_Target
            {
                half3 dark = tex2D(_MainTex, input.uv).rgb;
                half3 light = tex2D(_WhiteTex, input.uv).rgb;
                half alpha = saturate(1.0h - dot(light - dark, half3(0.3333h, 0.3333h, 0.3333h)));
                return alpha > 0.005h ? half4(saturate(dark / alpha), alpha) : half4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
