using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSX
{
    [VolumeComponentMenu("PSX/Dithering")]
    public class Dithering : VolumeComponent, IPostProcessComponent
    {
        //PIXELATION
        //public TextureParameter ditherTexture;
        public IntParameter patternIndex = new IntParameter(0);
        public FloatParameter ditherThreshold = new FloatParameter(512);
        public FloatParameter ditherStrength = new FloatParameter(1);
        public FloatParameter ditherScale = new FloatParameter(2);
        
        
        //INTERFACE REQUIREMENT 
        public bool IsActive() => active;
        public bool IsTileCompatible() => false;
    }
}