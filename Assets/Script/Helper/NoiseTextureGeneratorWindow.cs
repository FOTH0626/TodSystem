#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TodSystem.Helper
{
    public class NoiseTextureGeneratorWindow : EditorWindow
    {
        private enum NoiseDimension
        {
            Noise2D,
            Noise3D
        }

        private enum Noise3DExportMode
        {
            Texture3DAsset,
            Unwrapped2D
        }

        public enum NoiseType
        {
            Perlin,
            Worley,
            OneMinusWorley,
            Simplex,
            PerlinOneMinusWorley,
            SimplexOneMinusWorley,
            Curl,
            FbmWorley,
            FbmOneMinusWorley,
            SimplexFbmOneMinusWorley,
            FbmSimplexOneMinusFbmWorley
        }

        private enum OutputMode
        {
            SingleChannel,
            RGBA
        }

        private struct NoiseProvider
        {
            public readonly Func<float, float, float> Sample2D;
            public readonly Func<float, float, float, float> Sample3D;

            public NoiseProvider(
                Func<float, float, float> sample2D,
                Func<float, float, float, float> sample3D)
            {
                Sample2D = sample2D;
                Sample3D = sample3D;
            }
        }

        [Serializable]
        private struct FbmWorleySettings
        {
            public int Octaves;
            public float BaseFrequencyScale;
            public float WorleyFrequencyScale;
            public float Lacunarity;
            public float Gain;
        }

        // Add new noise variants by extending NoiseType and registering samplers here.
        private static readonly Dictionary<NoiseType, NoiseProvider> NoiseProviders = new Dictionary<NoiseType, NoiseProvider>
        {
            { NoiseType.Perlin, new NoiseProvider(SamplePerlin2D, SamplePerlin3D) },
            { NoiseType.Worley, new NoiseProvider(SampleWorley2D, SampleWorley3D) },
            { NoiseType.OneMinusWorley, new NoiseProvider(SampleOneMinusWorley2D, SampleOneMinusWorley3D) },
            { NoiseType.Simplex, new NoiseProvider(SampleSimplex2D, SampleSimplex3D) },
            { NoiseType.PerlinOneMinusWorley, new NoiseProvider(SamplePerlinOneMinusWorley2D, SamplePerlinOneMinusWorley3D) },
            { NoiseType.SimplexOneMinusWorley, new NoiseProvider(SampleSimplexOneMinusWorley2D, SampleSimplexOneMinusWorley3D) },
            { NoiseType.Curl, new NoiseProvider(SampleCurl2D, SampleCurl3D) }
        };

        private NoiseDimension _noiseDimension = NoiseDimension.Noise2D;
        private Noise3DExportMode _noise3DExportMode = Noise3DExportMode.Unwrapped2D;
        private OutputMode _outputMode = OutputMode.SingleChannel;

        private int _width = 256;
        private int _height = 256;
        private int _depth = 32;
        private float _frequency = 8f;
        private Vector3 _offset = Vector3.zero;
        private FbmWorleySettings _singleFbmSettings = new FbmWorleySettings
        {
            Octaves = 5,
            BaseFrequencyScale = 1f,
            WorleyFrequencyScale = 1f,
            Lacunarity = 2f,
            Gain = 0.5f
        };
        private FbmWorleySettings _redFbmSettings = new FbmWorleySettings
        {
            Octaves = 5,
            BaseFrequencyScale = 1f,
            WorleyFrequencyScale = 1f,
            Lacunarity = 2f,
            Gain = 0.5f
        };
        private FbmWorleySettings _greenFbmSettings = new FbmWorleySettings
        {
            Octaves = 5,
            BaseFrequencyScale = 1f,
            WorleyFrequencyScale = 1f,
            Lacunarity = 2f,
            Gain = 0.5f
        };
        private FbmWorleySettings _blueFbmSettings = new FbmWorleySettings
        {
            Octaves = 5,
            BaseFrequencyScale = 1f,
            WorleyFrequencyScale = 1f,
            Lacunarity = 2f,
            Gain = 0.5f
        };
        private FbmWorleySettings _alphaFbmSettings = new FbmWorleySettings
        {
            Octaves = 5,
            BaseFrequencyScale = 1f,
            WorleyFrequencyScale = 1f,
            Lacunarity = 2f,
            Gain = 0.5f
        };

        private NoiseType _singleChannelNoiseType = NoiseType.Perlin;
        private NoiseType _redNoiseType = NoiseType.Perlin;
        private NoiseType _greenNoiseType = NoiseType.Perlin;
        private NoiseType _blueNoiseType = NoiseType.Perlin;
        private NoiseType _alphaNoiseType = NoiseType.Perlin;

        private string _outputDirectory = "Assets/Texture";
        private string _fileName = "NoiseTexture";

        [MenuItem("Tools/Noise Texture Generator")]
        private static void OpenWindow()
        {
            GetWindow<NoiseTextureGeneratorWindow>("Noise Texture Generator");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Noise Settings", EditorStyles.boldLabel);
            _noiseDimension = (NoiseDimension)EditorGUILayout.EnumPopup("Noise Dimension", _noiseDimension);
            _width = Mathf.Max(1, EditorGUILayout.IntField("Width", _width));
            _height = Mathf.Max(1, EditorGUILayout.IntField("Height", _height));
            if (_noiseDimension == NoiseDimension.Noise3D)
            {
                _depth = Mathf.Max(1, EditorGUILayout.IntField("Depth", _depth));
            }
            _frequency = Mathf.Max(0.0001f, EditorGUILayout.FloatField("Frequency", _frequency));
            _offset = EditorGUILayout.Vector3Field("Offset", _offset);

            if (_noiseDimension == NoiseDimension.Noise3D)
            {
                _noise3DExportMode = (Noise3DExportMode)EditorGUILayout.EnumPopup("3D Export", _noise3DExportMode);
                EditorGUILayout.HelpBox("3D mode generates full volume noise. Offset.z shifts the whole volume depth.", MessageType.Info);
                if (_noise3DExportMode == Noise3DExportMode.Unwrapped2D)
                {
                    EditorGUILayout.HelpBox("Unwrapped 2D size is (Width * Depth, Height).", MessageType.None);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);
            _outputMode = (OutputMode)EditorGUILayout.EnumPopup("Output Mode", _outputMode);

            if (_outputMode == OutputMode.SingleChannel)
            {
                _singleChannelNoiseType = (NoiseType)EditorGUILayout.EnumPopup("Noise Type", _singleChannelNoiseType);
                if (RequiresFbmSettings(_singleChannelNoiseType))
                {
                    DrawFbmSettings("Single Channel", ref _singleFbmSettings);
                }
            }
            else
            {
                _redNoiseType = (NoiseType)EditorGUILayout.EnumPopup("R Channel", _redNoiseType);
                if (RequiresFbmSettings(_redNoiseType))
                {
                    DrawFbmSettings("R Channel", ref _redFbmSettings);
                }

                _greenNoiseType = (NoiseType)EditorGUILayout.EnumPopup("G Channel", _greenNoiseType);
                if (RequiresFbmSettings(_greenNoiseType))
                {
                    DrawFbmSettings("G Channel", ref _greenFbmSettings);
                }

                _blueNoiseType = (NoiseType)EditorGUILayout.EnumPopup("B Channel", _blueNoiseType);
                if (RequiresFbmSettings(_blueNoiseType))
                {
                    DrawFbmSettings("B Channel", ref _blueFbmSettings);
                }

                _alphaNoiseType = (NoiseType)EditorGUILayout.EnumPopup("A Channel", _alphaNoiseType);
                if (RequiresFbmSettings(_alphaNoiseType))
                {
                    DrawFbmSettings("A Channel", ref _alphaFbmSettings);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Save Settings", EditorStyles.boldLabel);
            DrawDirectoryField();
            _fileName = EditorGUILayout.TextField("File Name", _fileName);

            if (!_outputDirectory.Replace("\\", "/").StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_outputDirectory.Replace("\\", "/"), "Assets", StringComparison.OrdinalIgnoreCase))
            {
                EditorGUILayout.HelpBox("Path is outside Assets. Texture file will be saved, but not imported as a Unity asset.", MessageType.Warning);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate Noise Texture", GUILayout.Height(30)))
            {
                GenerateAndSaveTexture();
            }
        }

        private void DrawDirectoryField()
        {
            EditorGUILayout.BeginHorizontal();
            _outputDirectory = EditorGUILayout.TextField("Output Directory", _outputDirectory);
            if (GUILayout.Button("Select", GUILayout.Width(70)))
            {
                string selected = EditorUtility.OpenFolderPanel("Select Output Directory", GetProjectRoot(), string.Empty);
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    _outputDirectory = NormalizePath(selected);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void GenerateAndSaveTexture()
        {
            if (string.IsNullOrWhiteSpace(_fileName))
            {
                EditorUtility.DisplayDialog("Invalid File Name", "Please enter a file name.", "OK");
                return;
            }

            if (_noiseDimension == NoiseDimension.Noise2D)
            {
                GenerateAndSaveTexture2D();
                return;
            }

            if (_noise3DExportMode == Noise3DExportMode.Unwrapped2D)
            {
                GenerateAndSaveTexture3DUnwrapped2D();
                return;
            }

            GenerateAndSaveTexture3DAsset();
        }

        private void DrawFbmSettings(string channelLabel, ref FbmWorleySettings settings)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"FBM Composite ({channelLabel})", EditorStyles.boldLabel);
            settings.Octaves = Mathf.Clamp(EditorGUILayout.IntField("FBM Octaves", settings.Octaves), 1, 12);
            settings.BaseFrequencyScale = Mathf.Max(0.0001f, EditorGUILayout.FloatField("FBM Base Freq Scale", settings.BaseFrequencyScale));
            settings.WorleyFrequencyScale = Mathf.Max(0.0001f, EditorGUILayout.FloatField("Worley Freq Scale", settings.WorleyFrequencyScale));
            settings.Lacunarity = Mathf.Max(1.0001f, EditorGUILayout.FloatField("FBM Lacunarity", settings.Lacunarity));
            settings.Gain = Mathf.Clamp(EditorGUILayout.FloatField("FBM Gain", settings.Gain), 0.01f, 0.99f);
            settings = SanitizeFbmSettings(settings);
        }

        private static bool RequiresFbmSettings(NoiseType noiseType)
        {
            return noiseType == NoiseType.FbmWorley ||
                   noiseType == NoiseType.FbmOneMinusWorley ||
                   noiseType == NoiseType.SimplexFbmOneMinusWorley ||
                   noiseType == NoiseType.FbmSimplexOneMinusFbmWorley;
        }

        private static FbmWorleySettings SanitizeFbmSettings(FbmWorleySettings settings)
        {
            settings.Octaves = Mathf.Clamp(settings.Octaves, 1, 12);
            settings.BaseFrequencyScale = Mathf.Max(0.0001f, settings.BaseFrequencyScale);
            settings.WorleyFrequencyScale = Mathf.Max(0.0001f, settings.WorleyFrequencyScale);
            settings.Lacunarity = Mathf.Max(1.0001f, settings.Lacunarity);
            settings.Gain = Mathf.Clamp(settings.Gain, 0.01f, 0.99f);
            return settings;
        }

        private void GenerateAndSaveTexture2D()
        {
            Texture2D texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[_width * _height];

            for (int y = 0; y < _height; y++)
            {
                float v = _height == 1 ? 0f : (float)y / (_height - 1);
                for (int x = 0; x < _width; x++)
                {
                    float u = _width == 1 ? 0f : (float)x / (_width - 1);
                    pixels[y * _width + x] = SamplePixel(u, v, 0f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);

            string fileNameWithExtension = EnsurePngExtension(_fileName.Trim());
            string outputDirectory = ResolveOutputDirectory(_outputDirectory);
            Directory.CreateDirectory(outputDirectory);
            string fullPath = Path.Combine(outputDirectory, fileNameWithExtension);

            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            DestroyImmediate(texture);

            string relativeAssetPath = TryGetAssetsRelativePath(fullPath);
            if (!string.IsNullOrEmpty(relativeAssetPath))
            {
                AssetDatabase.ImportAsset(relativeAssetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
            }

            Debug.Log($"Noise texture generated: {fullPath}");
            EditorUtility.RevealInFinder(fullPath);
        }

        private void GenerateAndSaveTexture3DUnwrapped2D()
        {
            int unwrapWidth = _width * _depth;
            Texture2D texture = new Texture2D(unwrapWidth, _height, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[unwrapWidth * _height];
            for (int z = 0; z < _depth; z++)
            {
                float w = _depth == 1 ? 0f : (float)z / (_depth - 1);
                int sliceXOffset = z * _width;
                for (int y = 0; y < _height; y++)
                {
                    float v = _height == 1 ? 0f : (float)y / (_height - 1);
                    int rowOffset = y * unwrapWidth + sliceXOffset;
                    for (int x = 0; x < _width; x++)
                    {
                        float u = _width == 1 ? 0f : (float)x / (_width - 1);
                        pixels[rowOffset + x] = SamplePixel(u, v, w);
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);

            string fileNameWithExtension = EnsurePngExtension(_fileName.Trim());
            string outputDirectory = ResolveOutputDirectory(_outputDirectory);
            Directory.CreateDirectory(outputDirectory);
            string fullPath = Path.Combine(outputDirectory, fileNameWithExtension);

            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            DestroyImmediate(texture);

            string relativeAssetPath = TryGetAssetsRelativePath(fullPath);
            if (!string.IsNullOrEmpty(relativeAssetPath))
            {
                AssetDatabase.ImportAsset(relativeAssetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
            }

            Debug.Log($"Unwrapped 3D noise texture generated: {fullPath} ({_width}x{_height}x{_depth} -> {unwrapWidth}x{_height})");
            EditorUtility.RevealInFinder(fullPath);
        }

        private void GenerateAndSaveTexture3DAsset()
        {
            string fileNameWithExtension = EnsureAssetExtension(_fileName.Trim());
            string outputDirectory = ResolveOutputDirectory(_outputDirectory);
            Directory.CreateDirectory(outputDirectory);
            string fullPath = Path.Combine(outputDirectory, fileNameWithExtension);
            string relativeAssetPath = TryGetAssetsRelativePath(fullPath);

            if (string.IsNullOrEmpty(relativeAssetPath))
            {
                EditorUtility.DisplayDialog(
                    "Path Not Supported",
                    "Texture3D asset must be saved under the Unity Assets folder.",
                    "OK");
                return;
            }

            Texture3D texture = new Texture3D(_width, _height, _depth, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] voxels = new Color[_width * _height * _depth];
            for (int z = 0; z < _depth; z++)
            {
                float w = _depth == 1 ? 0f : (float)z / (_depth - 1);
                int zOffset = z * _width * _height;
                for (int y = 0; y < _height; y++)
                {
                    float v = _height == 1 ? 0f : (float)y / (_height - 1);
                    int yOffset = y * _width;
                    for (int x = 0; x < _width; x++)
                    {
                        float u = _width == 1 ? 0f : (float)x / (_width - 1);
                        voxels[zOffset + yOffset + x] = SamplePixel(u, v, w);
                    }
                }
            }

            texture.SetPixels(voxels);
            texture.Apply(false, false);

            if (AssetDatabase.LoadAssetAtPath<Texture3D>(relativeAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(relativeAssetPath);
            }

            AssetDatabase.CreateAsset(texture, relativeAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"3D noise asset generated: {relativeAssetPath} ({_width}x{_height}x{_depth})");
            EditorUtility.RevealInFinder(fullPath);
        }

        private Color SamplePixel(float u, float v, float w)
        {
            if (_outputMode == OutputMode.SingleChannel)
            {
                float value = SampleNoise(_singleChannelNoiseType, u, v, w, _singleFbmSettings);
                return new Color(value, value, value, 1f);
            }

            float r = SampleNoise(_redNoiseType, u, v, w, _redFbmSettings);
            float g = SampleNoise(_greenNoiseType, u, v, w, _greenFbmSettings);
            float b = SampleNoise(_blueNoiseType, u, v, w, _blueFbmSettings);
            float a = SampleNoise(_alphaNoiseType, u, v, w, _alphaFbmSettings);
            return new Color(r, g, b, a);
        }

        private float SampleNoise(NoiseType noiseType, float u, float v, float w, FbmWorleySettings fbmSettings)
        {
            float x = (u + _offset.x) * _frequency;
            float y = (v + _offset.y) * _frequency;
            float z = (w + _offset.z) * _frequency;

            if (noiseType == NoiseType.FbmWorley)
            {
                FbmWorleySettings settings = SanitizeFbmSettings(fbmSettings);
                if (_noiseDimension == NoiseDimension.Noise2D)
                {
                    return Mathf.Clamp01(SampleFbmWorley2D(x, y, settings));
                }

                return Mathf.Clamp01(SampleFbmWorley3D(x, y, z, settings));
            }

            if (noiseType == NoiseType.FbmOneMinusWorley)
            {
                FbmWorleySettings settings = SanitizeFbmSettings(fbmSettings);
                if (_noiseDimension == NoiseDimension.Noise2D)
                {
                    return Mathf.Clamp01(SampleFbmOneMinusWorley2D(x, y, settings));
                }

                return Mathf.Clamp01(SampleFbmOneMinusWorley3D(x, y, z, settings));
            }

            if (noiseType == NoiseType.SimplexFbmOneMinusWorley)
            {
                FbmWorleySettings settings = SanitizeFbmSettings(fbmSettings);
                if (_noiseDimension == NoiseDimension.Noise2D)
                {
                    return Mathf.Clamp01(SampleSimplexFbmOneMinusWorley2D(x, y, settings));
                }

                return Mathf.Clamp01(SampleSimplexFbmOneMinusWorley3D(x, y, z, settings));
            }

            if (noiseType == NoiseType.FbmSimplexOneMinusFbmWorley)
            {
                FbmWorleySettings settings = SanitizeFbmSettings(fbmSettings);
                if (_noiseDimension == NoiseDimension.Noise2D)
                {
                    return Mathf.Clamp01(SampleFbmSimplexOneMinusFbmWorley2D(x, y, settings));
                }

                return Mathf.Clamp01(SampleFbmSimplexOneMinusFbmWorley3D(x, y, z, settings));
            }

            NoiseProvider provider = NoiseProviders[noiseType];
            if (_noiseDimension == NoiseDimension.Noise2D)
            {
                return Mathf.Clamp01(provider.Sample2D(x, y));
            }

            return Mathf.Clamp01(provider.Sample3D(x, y, z));
        }

        private static float SamplePerlin2D(float x, float y)
        {
            return Mathf.PerlinNoise(x, y);
        }

        private static float SamplePerlin3D(float x, float y, float z)
        {
            // Unity has no built-in 3D Perlin, so we blend multiple 2D slices.
            float xy = Mathf.PerlinNoise(x, y);
            float yz = Mathf.PerlinNoise(y, z);
            float xz = Mathf.PerlinNoise(x, z);
            float yx = Mathf.PerlinNoise(y, x);
            float zy = Mathf.PerlinNoise(z, y);
            float zx = Mathf.PerlinNoise(z, x);
            return (xy + yz + xz + yx + zy + zx) / 6f;
        }

        private static float SampleWorley2D(float x, float y)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);

            float minDistSq = float.MaxValue;
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int cx = ix + ox;
                    int cy = iy + oy;
                    float fx = cx + Hash01(cx, cy, 0, 11);
                    float fy = cy + Hash01(cx, cy, 0, 17);
                    float dx = fx - x;
                    float dy = fy - y;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                    }
                }
            }

            float minDist = Mathf.Sqrt(minDistSq);
            float normalized = minDist / 1.41421356f;
            return Mathf.Clamp01(normalized);
        }

        private static float SampleWorley3D(float x, float y, float z)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            int iz = Mathf.FloorToInt(z);

            float minDistSq = float.MaxValue;
            for (int oz = -1; oz <= 1; oz++)
            {
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int cx = ix + ox;
                        int cy = iy + oy;
                        int cz = iz + oz;
                        float fx = cx + Hash01(cx, cy, cz, 23);
                        float fy = cy + Hash01(cx, cy, cz, 29);
                        float fz = cz + Hash01(cx, cy, cz, 31);
                        float dx = fx - x;
                        float dy = fy - y;
                        float dz = fz - z;
                        float distSq = dx * dx + dy * dy + dz * dz;
                        if (distSq < minDistSq)
                        {
                            minDistSq = distSq;
                        }
                    }
                }
            }

            float minDist = Mathf.Sqrt(minDistSq);
            float normalized = minDist / 1.73205081f;
            return Mathf.Clamp01(normalized);
        }

        private static float SampleOneMinusWorley2D(float x, float y)
        {
            return 1f - SampleWorley2D(x, y);
        }

        private static float SampleOneMinusWorley3D(float x, float y, float z)
        {
            return 1f - SampleWorley3D(x, y, z);
        }

        private static float SampleSimplex2D(float x, float y)
        {
            return Mathf.Clamp01(Simplex2D(x, y) * 0.5f + 0.5f);
        }

        private static float SampleSimplex3D(float x, float y, float z)
        {
            return Mathf.Clamp01(Simplex3D(x, y, z) * 0.5f + 0.5f);
        }

        private static float SamplePerlinOneMinusWorley2D(float x, float y)
        {
            float perlin = SamplePerlin2D(x, y);
            float oneMinusWorley = SampleOneMinusWorley2D(x, y);
            return Remap(perlin, oneMinusWorley, 1f, 0f, 1f);
        }

        private static float SamplePerlinOneMinusWorley3D(float x, float y, float z)
        {
            float perlin = SamplePerlin3D(x, y, z);
            float oneMinusWorley = SampleOneMinusWorley3D(x, y, z);
            return Remap(perlin, oneMinusWorley, 1f, 0f, 1f);
        }

        private static float SampleSimplexOneMinusWorley2D(float x, float y)
        {
            float simplex = SampleSimplex2D(x, y);
            float oneMinusWorley = SampleOneMinusWorley2D(x, y);
            return Remap(simplex, oneMinusWorley, 1f, 0f, 1f);
        }

        private static float SampleSimplexOneMinusWorley3D(float x, float y, float z)
        {
            float simplex = SampleSimplex3D(x, y, z);
            float oneMinusWorley = SampleOneMinusWorley3D(x, y, z);
            return Remap(simplex, oneMinusWorley, 1f, 0f, 1f);
        }

        private static float SampleCurl2D(float x, float y)
        {
            const float e = 0.01f;
            float dx = (SampleSimplex2D(x + e, y) - SampleSimplex2D(x - e, y)) / (2f * e);
            float dy = (SampleSimplex2D(x, y + e) - SampleSimplex2D(x, y - e)) / (2f * e);

            float magnitude = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(1f - Mathf.Exp(-0.5f * magnitude));
        }

        private static float SampleCurl3D(float x, float y, float z)
        {
            const float e = 0.01f;

            Func<float, float, float, float> n1 = SampleSimplex3D;
            Func<float, float, float, float> n2 = (a, b, c) => SampleSimplex3D(a + 19.1f, b + 33.4f, c + 47.2f);
            Func<float, float, float, float> n3 = (a, b, c) => SampleSimplex3D(a + 74.2f, b + 12.7f, c + 8.3f);

            float dn3dy = (n3(x, y + e, z) - n3(x, y - e, z)) / (2f * e);
            float dn2dz = (n2(x, y, z + e) - n2(x, y, z - e)) / (2f * e);
            float dn1dz = (n1(x, y, z + e) - n1(x, y, z - e)) / (2f * e);
            float dn3dx = (n3(x + e, y, z) - n3(x - e, y, z)) / (2f * e);
            float dn2dx = (n2(x + e, y, z) - n2(x - e, y, z)) / (2f * e);
            float dn1dy = (n1(x, y + e, z) - n1(x, y - e, z)) / (2f * e);

            float cx = dn3dy - dn2dz;
            float cy = dn1dz - dn3dx;
            float cz = dn2dx - dn1dy;
            float magnitude = Mathf.Sqrt(cx * cx + cy * cy + cz * cz);
            return Mathf.Clamp01(1f - Mathf.Exp(-0.35f * magnitude));
        }

        private static float SampleFbmWorley2D(float x, float y, FbmWorleySettings settings)
        {
            float fbm = SampleFbmSimplex2D(x * settings.BaseFrequencyScale, y * settings.BaseFrequencyScale, settings);
            float oneMinusWorley = SampleOneMinusWorley2D(x * settings.WorleyFrequencyScale, y * settings.WorleyFrequencyScale);
            return Remap(fbm, oneMinusWorley, 1f, 0f, 1f);
        }

        private static float SampleFbmWorley3D(float x, float y, float z, FbmWorleySettings settings)
        {
            float fbm = SampleFbmSimplex3D(
                x * settings.BaseFrequencyScale,
                y * settings.BaseFrequencyScale,
                z * settings.BaseFrequencyScale,
                settings);
            float oneMinusWorley = SampleOneMinusWorley3D(
                x * settings.WorleyFrequencyScale,
                y * settings.WorleyFrequencyScale,
                z * settings.WorleyFrequencyScale);
            return Remap(fbm, oneMinusWorley, 1f, 0f, 1f);
        }

        private static float SampleFbmOneMinusWorley2D(float x, float y, FbmWorleySettings settings)
        {
            return SampleFbmOneMinusWorley(
                x * settings.WorleyFrequencyScale,
                y * settings.WorleyFrequencyScale,
                settings);
        }

        private static float SampleFbmOneMinusWorley3D(float x, float y, float z, FbmWorleySettings settings)
        {
            return SampleFbmOneMinusWorley(
                x * settings.WorleyFrequencyScale,
                y * settings.WorleyFrequencyScale,
                z * settings.WorleyFrequencyScale,
                settings);
        }

        private static float SampleSimplexFbmOneMinusWorley2D(float x, float y, FbmWorleySettings settings)
        {
            float simplex = SampleSimplex2D(x * settings.BaseFrequencyScale, y * settings.BaseFrequencyScale);
            float fbmOneMinusWorley = SampleFbmOneMinusWorley(
                x * settings.WorleyFrequencyScale,
                y * settings.WorleyFrequencyScale,
                settings);
            return Remap(simplex,1- fbmOneMinusWorley, 1f, 0f, 1f);
        }

        private static float SampleSimplexFbmOneMinusWorley3D(float x, float y, float z, FbmWorleySettings settings)
        {
            float simplex = SampleSimplex3D(
                x * settings.BaseFrequencyScale,
                y * settings.BaseFrequencyScale,
                z * settings.BaseFrequencyScale);
            float fbmOneMinusWorley = SampleFbmOneMinusWorley(
                x * settings.WorleyFrequencyScale,
                y * settings.WorleyFrequencyScale,
                z * settings.WorleyFrequencyScale,
                settings);
            return Remap(simplex, fbmOneMinusWorley, 1f, 0f, 1f);
        }

        private static float SampleFbmSimplexOneMinusFbmWorley2D(float x, float y, FbmWorleySettings settings)
        {
            float fbmSimplex = SampleFbmSimplex2D(
                x * settings.BaseFrequencyScale,
                y * settings.BaseFrequencyScale,
                settings);
            float fbmWorley = SampleFbmWorleyRaw2D(
                x * settings.WorleyFrequencyScale,
                y * settings.WorleyFrequencyScale,
                settings);

            return Remap(fbmSimplex, fbmWorley, 1f, 0f, 1f);
        }

        private static float SampleFbmSimplexOneMinusFbmWorley3D(float x, float y, float z, FbmWorleySettings settings)
        {
            float fbmSimplex = SampleFbmSimplex3D(
                x * settings.BaseFrequencyScale,
                y * settings.BaseFrequencyScale,
                z * settings.BaseFrequencyScale,
                settings);
            float fbmWorley = SampleFbmWorleyRaw3D(
                x * settings.WorleyFrequencyScale,
                y * settings.WorleyFrequencyScale,
                z * settings.WorleyFrequencyScale,
                settings);
            float oneMinusFbmWorley = 1f - fbmWorley;
            return Remap(fbmSimplex, fbmWorley, 1f, 0f, 1f);
        }

        private static float SampleFbmOneMinusWorley(float x, float y, FbmWorleySettings settings)
        {
            float value = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float amplitudeSum = 0f;

            for (int i = 0; i < settings.Octaves; i++)
            {
                value += SampleOneMinusWorley2D(x * frequency, y * frequency) * amplitude;
                amplitudeSum += amplitude;
                amplitude *= settings.Gain;
                frequency *= settings.Lacunarity;
            }

            return amplitudeSum <= 0f ? 0f : value / amplitudeSum;
        }

        private static float SampleFbmWorleyRaw2D(float x, float y, FbmWorleySettings settings)
        {
            float value = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float amplitudeSum = 0f;

            for (int i = 0; i < settings.Octaves; i++)
            {
                value += SampleWorley2D(x * frequency, y * frequency) * amplitude;
                amplitudeSum += amplitude;
                amplitude *= settings.Gain;
                frequency *= settings.Lacunarity;
            }

            return amplitudeSum <= 0f ? 0f : value / amplitudeSum;
        }

        private static float SampleFbmWorleyRaw3D(float x, float y, float z, FbmWorleySettings settings)
        {
            float value = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float amplitudeSum = 0f;

            for (int i = 0; i < settings.Octaves; i++)
            {
                value += SampleWorley3D(x * frequency, y * frequency, z * frequency) * amplitude;
                amplitudeSum += amplitude;
                amplitude *= settings.Gain;
                frequency *= settings.Lacunarity;
            }

            return amplitudeSum <= 0f ? 0f : value / amplitudeSum;
        }

        private static float SampleFbmOneMinusWorley(float x, float y, float z, FbmWorleySettings settings)
        {
            float value = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float amplitudeSum = 0f;

            for (int i = 0; i < settings.Octaves; i++)
            {
                value += SampleOneMinusWorley3D(x * frequency, y * frequency, z * frequency) * amplitude;
                amplitudeSum += amplitude;
                amplitude *= settings.Gain;
                frequency *= settings.Lacunarity;
            }

            return amplitudeSum <= 0f ? 0f : value / amplitudeSum;
        }

        private static float SampleFbmSimplex2D(float x, float y, FbmWorleySettings settings)
        {
            float value = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float amplitudeSum = 0f;

            for (int i = 0; i < settings.Octaves; i++)
            {
                value += SampleSimplex2D(x * frequency, y * frequency) * amplitude;
                amplitudeSum += amplitude;
                amplitude *= settings.Gain;
                frequency *= settings.Lacunarity;
            }

            return amplitudeSum <= 0f ? 0f : value / amplitudeSum;
        }

        private static float SampleFbmSimplex3D(float x, float y, float z, FbmWorleySettings settings)
        {
            float value = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float amplitudeSum = 0f;

            for (int i = 0; i < settings.Octaves; i++)
            {
                value += SampleSimplex3D(x * frequency, y * frequency, z * frequency) * amplitude;
                amplitudeSum += amplitude;
                amplitude *= settings.Gain;
                frequency *= settings.Lacunarity;
            }

            return amplitudeSum <= 0f ? 0f : value / amplitudeSum;
        }

        private static float Remap(float value, float inMin, float inMax, float outMin, float outMax)
        {
            if (Mathf.Approximately(inMin, inMax))
            {
                return Mathf.Clamp01(outMin);
            }

            float t = Mathf.InverseLerp(inMin, inMax, value);
            return Mathf.Clamp01(Mathf.Lerp(outMin, outMax, t));
        }

        private static uint Hash4(int x, int y, int z, int w)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u;
                h ^= (uint)y * 668265263u;
                h ^= (uint)z * 2246822519u;
                h ^= (uint)w * 3266489917u;
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return h;
            }
        }

        private static float Hash01(int x, int y, int z, int w)
        {
            uint h = Hash4(x, y, z, w);
            return (h & 0x00FFFFFFu) / 16777215f;
        }

        private static readonly int[,] SimplexGrad3 = new int[,]
        {
            { 1, 1, 0 }, { -1, 1, 0 }, { 1, -1, 0 }, { -1, -1, 0 },
            { 1, 0, 1 }, { -1, 0, 1 }, { 1, 0, -1 }, { -1, 0, -1 },
            { 0, 1, 1 }, { 0, -1, 1 }, { 0, 1, -1 }, { 0, -1, -1 }
        };

        private static readonly int[] SimplexPerm = BuildSimplexPermTable();

        private static int[] BuildSimplexPermTable()
        {
            int[] p = new int[]
            {
                151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
                140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
                247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
                57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
                74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
                60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
                65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
                200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
                52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
                207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
                119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
                129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
                218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
                81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
                184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
                222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180
            };

            int[] perm = new int[512];
            for (int i = 0; i < 512; i++)
            {
                perm[i] = p[i & 255];
            }

            return perm;
        }

        private static float Simplex2D(float xin, float yin)
        {
            const float F2 = 0.3660254037844386f;
            const float G2 = 0.21132486540518713f;

            float s = (xin + yin) * F2;
            int i = Mathf.FloorToInt(xin + s);
            int j = Mathf.FloorToInt(yin + s);
            float t = (i + j) * G2;
            float x0 = xin - (i - t);
            float y0 = yin - (j - t);

            int i1;
            int j1;
            if (x0 > y0)
            {
                i1 = 1;
                j1 = 0;
            }
            else
            {
                i1 = 0;
                j1 = 1;
            }

            float x1 = x0 - i1 + G2;
            float y1 = y0 - j1 + G2;
            float x2 = x0 - 1f + 2f * G2;
            float y2 = y0 - 1f + 2f * G2;

            int ii = i & 255;
            int jj = j & 255;
            int gi0 = SimplexPerm[ii + SimplexPerm[jj]] % 12;
            int gi1 = SimplexPerm[ii + i1 + SimplexPerm[jj + j1]] % 12;
            int gi2 = SimplexPerm[ii + 1 + SimplexPerm[jj + 1]] % 12;

            float n0 = 0f;
            float n1 = 0f;
            float n2 = 0f;

            float t0 = 0.5f - x0 * x0 - y0 * y0;
            if (t0 >= 0f)
            {
                t0 *= t0;
                n0 = t0 * t0 * DotGrad(gi0, x0, y0, 0f);
            }

            float t1 = 0.5f - x1 * x1 - y1 * y1;
            if (t1 >= 0f)
            {
                t1 *= t1;
                n1 = t1 * t1 * DotGrad(gi1, x1, y1, 0f);
            }

            float t2 = 0.5f - x2 * x2 - y2 * y2;
            if (t2 >= 0f)
            {
                t2 *= t2;
                n2 = t2 * t2 * DotGrad(gi2, x2, y2, 0f);
            }

            return 70f * (n0 + n1 + n2);
        }

        private static float Simplex3D(float xin, float yin, float zin)
        {
            const float F3 = 1f / 3f;
            const float G3 = 1f / 6f;

            float s = (xin + yin + zin) * F3;
            int i = Mathf.FloorToInt(xin + s);
            int j = Mathf.FloorToInt(yin + s);
            int k = Mathf.FloorToInt(zin + s);

            float t = (i + j + k) * G3;
            float x0 = xin - (i - t);
            float y0 = yin - (j - t);
            float z0 = zin - (k - t);

            int i1;
            int j1;
            int k1;
            int i2;
            int j2;
            int k2;

            if (x0 >= y0)
            {
                if (y0 >= z0)
                {
                    i1 = 1; j1 = 0; k1 = 0;
                    i2 = 1; j2 = 1; k2 = 0;
                }
                else if (x0 >= z0)
                {
                    i1 = 1; j1 = 0; k1 = 0;
                    i2 = 1; j2 = 0; k2 = 1;
                }
                else
                {
                    i1 = 0; j1 = 0; k1 = 1;
                    i2 = 1; j2 = 0; k2 = 1;
                }
            }
            else
            {
                if (y0 < z0)
                {
                    i1 = 0; j1 = 0; k1 = 1;
                    i2 = 0; j2 = 1; k2 = 1;
                }
                else if (x0 < z0)
                {
                    i1 = 0; j1 = 1; k1 = 0;
                    i2 = 0; j2 = 1; k2 = 1;
                }
                else
                {
                    i1 = 0; j1 = 1; k1 = 0;
                    i2 = 1; j2 = 1; k2 = 0;
                }
            }

            float x1 = x0 - i1 + G3;
            float y1 = y0 - j1 + G3;
            float z1 = z0 - k1 + G3;
            float x2 = x0 - i2 + 2f * G3;
            float y2 = y0 - j2 + 2f * G3;
            float z2 = z0 - k2 + 2f * G3;
            float x3 = x0 - 1f + 3f * G3;
            float y3 = y0 - 1f + 3f * G3;
            float z3 = z0 - 1f + 3f * G3;

            int ii = i & 255;
            int jj = j & 255;
            int kk = k & 255;
            int gi0 = SimplexPerm[ii + SimplexPerm[jj + SimplexPerm[kk]]] % 12;
            int gi1 = SimplexPerm[ii + i1 + SimplexPerm[jj + j1 + SimplexPerm[kk + k1]]] % 12;
            int gi2 = SimplexPerm[ii + i2 + SimplexPerm[jj + j2 + SimplexPerm[kk + k2]]] % 12;
            int gi3 = SimplexPerm[ii + 1 + SimplexPerm[jj + 1 + SimplexPerm[kk + 1]]] % 12;

            float n0 = 0f;
            float n1 = 0f;
            float n2 = 0f;
            float n3 = 0f;

            float t0 = 0.6f - x0 * x0 - y0 * y0 - z0 * z0;
            if (t0 >= 0f)
            {
                t0 *= t0;
                n0 = t0 * t0 * DotGrad(gi0, x0, y0, z0);
            }

            float t1 = 0.6f - x1 * x1 - y1 * y1 - z1 * z1;
            if (t1 >= 0f)
            {
                t1 *= t1;
                n1 = t1 * t1 * DotGrad(gi1, x1, y1, z1);
            }

            float t2 = 0.6f - x2 * x2 - y2 * y2 - z2 * z2;
            if (t2 >= 0f)
            {
                t2 *= t2;
                n2 = t2 * t2 * DotGrad(gi2, x2, y2, z2);
            }

            float t3 = 0.6f - x3 * x3 - y3 * y3 - z3 * z3;
            if (t3 >= 0f)
            {
                t3 *= t3;
                n3 = t3 * t3 * DotGrad(gi3, x3, y3, z3);
            }

            return 32f * (n0 + n1 + n2 + n3);
        }

        private static float DotGrad(int gi, float x, float y, float z)
        {
            return SimplexGrad3[gi, 0] * x + SimplexGrad3[gi, 1] * y + SimplexGrad3[gi, 2] * z;
        }

        private static string EnsurePngExtension(string fileName)
        {
            return fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? fileName : $"{fileName}.png";
        }

        private static string EnsureAssetExtension(string fileName)
        {
            return fileName.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ? fileName : $"{fileName}.asset";
        }

        private static string ResolveOutputDirectory(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return Path.Combine(GetProjectRoot(), "Assets");
            }

            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.GetFullPath(Path.Combine(GetProjectRoot(), configuredPath));
        }

        private static string TryGetAssetsRelativePath(string fullPath)
        {
            string normalizedFullPath = NormalizePath(fullPath);
            string normalizedAssetsPath = NormalizePath(Application.dataPath);

            if (!normalizedFullPath.StartsWith(normalizedAssetsPath, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string suffix = normalizedFullPath.Substring(normalizedAssetsPath.Length).TrimStart('/');
            return string.IsNullOrEmpty(suffix) ? "Assets" : $"Assets/{suffix}";
        }

        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string NormalizePath(string path)
        {
            return path.Replace("\\", "/");
        }
    }
}
#endif
