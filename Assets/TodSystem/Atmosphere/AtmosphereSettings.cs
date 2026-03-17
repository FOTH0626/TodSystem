using System;
using UnityEngine;
using UnityEngine.Serialization;


[Serializable, CreateAssetMenu(fileName = "Atmosphere", menuName = "AtmosphereSettings")]
public class AtmosphereSettings : ScriptableObject
{
    public float seaLevel = 0.0f;
    public float planetRadius = 6360000.0f;
    public float atmosphereHeight = 60000.0f;
    public float sunLightIntensity = 31.4f;
    public Color sunLightColor = Color.white;
    public float sunDiskAngle = 1.0f;
    public float rayleighScatteringScale = 1.0f;
    public float rayleighScatteringScalarHeight = 8000.0f;
    public float mieScatteringScale = 1.0f;
    public float mieAnisotropy = 0.8f;
    public float mieScatteringScalarHeight = 1200.0f;
    public float ozoneAbsorptionScale = 1.0f;
    public float ozoneLevelCenterHeight = 25000.0f;
    public float ozoneLevelWidth = 15000.0f;
    public float aerialPerspectiveDistance = 32000.0f;
}