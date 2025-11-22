Shader "Custom/DayNightTransition"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _DayTex ("Day Texture", 2D) = "white" {}
        _NightTex ("Night Texture", 2D) = "white" {}
        _Transition ("Transition", Range(0, 1)) = 0
    }
    
    SubShader
    {
        Tags 
        { 
            "Queue"="Transparent" 
            "RenderType"="Transparent"
            "PreviewType"="Plane"
        }
        
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            
            sampler2D _DayTex;
            sampler2D _NightTex;
            float4 _DayTex_ST;
            float _Transition;
            
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _DayTex);
                o.color = v.color;
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 dayColor = tex2D(_DayTex, i.uv);
                fixed4 nightColor = tex2D(_NightTex, i.uv);
                
                // Lerp between day and night based on transition value
                fixed4 col = lerp(dayColor, nightColor, _Transition);
                
                // Apply vertex color tint
                col *= i.color;
                
                return col;
            }
            ENDCG
        }
    }
}