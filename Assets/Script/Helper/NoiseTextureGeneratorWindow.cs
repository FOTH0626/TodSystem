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

        public enum NoiseType
        {
            Perlin
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

        // Add new noise variants by extending NoiseType and registering samplers here.
        private static readonly Dictionary<NoiseType, NoiseProvider> NoiseProviders = new Dictionary<NoiseType, NoiseProvider>
        {
            { NoiseType.Perlin, new NoiseProvider(SamplePerlin2D, SamplePerlin3D) }
        };

        private NoiseDimension _noiseDimension = NoiseDimension.Noise2D;
        private OutputMode _outputMode = OutputMode.SingleChannel;

        private int _width = 256;
        private int _height = 256;
        private float _frequency = 8f;
        private Vector3 _offset = Vector3.zero;

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
            _frequency = Mathf.Max(0.0001f, EditorGUILayout.FloatField("Frequency", _frequency));
            _offset = EditorGUILayout.Vector3Field("Offset", _offset);

            if (_noiseDimension == NoiseDimension.Noise3D)
            {
                EditorGUILayout.HelpBox("3D mode samples a slice using Offset.z as depth.", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);
            _outputMode = (OutputMode)EditorGUILayout.EnumPopup("Output Mode", _outputMode);

            if (_outputMode == OutputMode.SingleChannel)
            {
                _singleChannelNoiseType = (NoiseType)EditorGUILayout.EnumPopup("Noise Type", _singleChannelNoiseType);
            }
            else
            {
                _redNoiseType = (NoiseType)EditorGUILayout.EnumPopup("R Channel", _redNoiseType);
                _greenNoiseType = (NoiseType)EditorGUILayout.EnumPopup("G Channel", _greenNoiseType);
                _blueNoiseType = (NoiseType)EditorGUILayout.EnumPopup("B Channel", _blueNoiseType);
                _alphaNoiseType = (NoiseType)EditorGUILayout.EnumPopup("A Channel", _alphaNoiseType);
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
                    pixels[y * _width + x] = SamplePixel(u, v);
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

        private Color SamplePixel(float u, float v)
        {
            if (_outputMode == OutputMode.SingleChannel)
            {
                float value = SampleNoise(_singleChannelNoiseType, u, v);
                return new Color(value, value, value, 1f);
            }

            float r = SampleNoise(_redNoiseType, u, v);
            float g = SampleNoise(_greenNoiseType, u, v);
            float b = SampleNoise(_blueNoiseType, u, v);
            float a = SampleNoise(_alphaNoiseType, u, v);
            return new Color(r, g, b, a);
        }

        private float SampleNoise(NoiseType noiseType, float u, float v)
        {
            NoiseProvider provider = NoiseProviders[noiseType];

            float x = (u + _offset.x) * _frequency;
            float y = (v + _offset.y) * _frequency;
            float z = _offset.z * _frequency;

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

        private static string EnsurePngExtension(string fileName)
        {
            return fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? fileName : $"{fileName}.png";
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
