using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlackHole
{
    public class GameSetup
    {
        public float c_light = 299_792_458.0f;
        public float G_newton = 6.67430e-11f;
        public float SagAMass = 8.54e36f;

        public int WindowWidth = 1600;
        public int WindowHeight = 1200;

        public GameSetup()
        {
            RS = 2.0f * G_newton * SagAMass / (c_light * c_light);
            LengthUnit = RS * 1e-3f;
            RS_scaled = RS / LengthUnit;
            AffineStep = 0.003f * RS_scaled;
            EscapeR = 40 * RS_scaled;

            IntegrationStepsMoving = IntegrationStepsStill;
        }

        public float LengthUnit;
        public float RS;
        public float RS_scaled;

        public int IntegrationStepsStill = 8000;
        public int IntegrationStepsMoving;
        public float EscapeR;
        public float AffineStep;

        public bool EnablePaths = false;
        public int PathStride = 4;

        public bool ShowDisk = true;
        public bool ShowBricks = true;
        public HorizonHandling HorizonHandling = HorizonHandling.Black;

    }

    public enum HorizonHandling
    {
        Black = 0, Reflective = 1, Transparent = 2
    }
}
