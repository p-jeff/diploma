Shader "Custom/HandDepthOccluder"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" }
        ColorMask 0       // don't write color
        ZWrite On         // DO write depth
        Pass { }
    }
}