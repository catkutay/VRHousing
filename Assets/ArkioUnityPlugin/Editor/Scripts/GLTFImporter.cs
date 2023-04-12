#if UNITY_2017_1_OR_NEWER
using UnityEditor.Experimental.AssetImporters;
using UnityEditor;
using System.IO;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using Object = UnityEngine.Object;
using System.Collections;
using ArkioUnityPlugin.Loader;
using GLTF.Schema;
using GLTF;
using System.Threading.Tasks;

namespace ArkioUnityPlugin
{
    [ScriptedImporter(1, new[] { "glb" })]
    public class GLTFImporter : ScriptedImporter
    {
        [SerializeField] private bool _removeEmptyRootObjects = true;
        [SerializeField] private float _scaleFactor = 1.0f;
        [SerializeField] private int _maximumLod = 300;
        [SerializeField] private bool _readWriteEnabled = true;

        //TODO remove this and add a parameter for adding colliders to solids, possibly props but not e.g. sketches
        [SerializeField] private bool _generateColliders = false;
        [SerializeField] private bool _swapUvs = false;
        [SerializeField] private GLTFImporterNormals _importNormals = GLTFImporterNormals.Import;
        [SerializeField] private bool _importMaterials = true;
        [SerializeField] private bool _useJpgTextures = false;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string sceneName = null;
            GameObject gltfScene = null;
            UnityEngine.Mesh[] meshes = null;
            var meshHash = new HashSet<UnityEngine.Mesh>();

            GLTFSceneImporter gltfImporter;
            try
            {
                sceneName = Path.GetFileNameWithoutExtension(ctx.assetPath);
                gltfScene = CreateGLTFScene(ctx.assetPath, out gltfImporter);

                // Remove empty roots
                if (_removeEmptyRootObjects)
                {
                    var t = gltfScene.transform;
                    while (
                        gltfScene.transform.childCount == 1 &&
                        gltfScene.GetComponents<Component>().Length == 1)
                    {
                        var parent = gltfScene;
                        gltfScene = gltfScene.transform.GetChild(0).gameObject;
                        t = gltfScene.transform;
                        t.parent = null; // To keep transform information in the new parent
                        Object.DestroyImmediate(parent); // Get rid of the parent
                    }
                }

                // Ensure there are no hide flags present (will cause problems when saving)
                gltfScene.hideFlags &= ~(HideFlags.HideAndDontSave);
                foreach (Transform child in gltfScene.transform)
                {
                    child.gameObject.hideFlags &= ~(HideFlags.HideAndDontSave);
                }

                // Zero position
                gltfScene.transform.position = Vector3.zero;

                Debug.Log(String.Format("Importing model containing {0} objects.", gltfScene.transform.childCount));

                // Get meshes
                var meshNames = new List<string>();
                var meshFilters = gltfScene.GetComponentsInChildren<MeshFilter>();
                var vertexBuffer = new List<Vector3>();
                meshes = meshFilters.Select(mf =>
                {
                    var mesh = mf.sharedMesh;
                    vertexBuffer.Clear();
                    mesh.GetVertices(vertexBuffer);
                    for (var i = 0; i < vertexBuffer.Count; ++i)
                    {
                        vertexBuffer[i] *= _scaleFactor;
                    }
                    mesh.SetVertices(vertexBuffer);
                    if (_swapUvs)
                    {
                        var uv = mesh.uv;
                        var uv2 = mesh.uv2;
                        mesh.uv = uv2;
                        mesh.uv2 = uv2;
                    }
                    if (_importNormals == GLTFImporterNormals.None)
                    {
                        mesh.normals = new Vector3[0];
                    }
                    if (_importNormals == GLTFImporterNormals.Calculate && mesh.GetTopology(0) == MeshTopology.Triangles)
                    {
                        mesh.RecalculateNormals();
                    }
                    mesh.UploadMeshData(!_readWriteEnabled);

                    if (_generateColliders)
                    {
                        var collider = mf.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = mesh;
                    }

                    if (meshHash.Add(mesh))
                    {
                        var meshName = string.IsNullOrEmpty(mesh.name) ? mf.gameObject.name : mesh.name;
                        mesh.name = ObjectNames.GetUniqueName(meshNames.ToArray(), meshName);
                        meshNames.Add(mesh.name);
                    }

                    return mesh;
                }).ToArray();

                var renderers = gltfScene.GetComponentsInChildren<Renderer>();

                if (_importMaterials)
                {
                    // Get materials
                    var materialNames = new List<string>();
                    var materialHash = new HashSet<UnityEngine.Material>();
                    var materials = renderers.SelectMany(r =>
                    {
                        return r.sharedMaterials.Select(mat =>
                        {
                            if (materialHash.Add(mat))
                            {
                                var matName = string.IsNullOrEmpty(mat.name) ? mat.shader.name : mat.name;
                                if (matName == mat.shader.name)
                                {
                                    matName = matName.Substring(Mathf.Min(matName.LastIndexOf("/") + 1, matName.Length - 1));
                                }

                                // Ensure name is unique
                                matName = string.Format("{0} {1}", sceneName, ObjectNames.NicifyVariableName(matName));
                                matName = ObjectNames.GetUniqueName(materialNames.ToArray(), matName);

                                mat.name = matName;
                                materialNames.Add(matName);
                            }

                            return mat;
                        });
                    }).ToArray();

                    materials = materialHash.ToArray();

                    //Check what textures need to be imported
                    HashSet<Texture2D> texturesToImport = new HashSet<Texture2D>();

                    // Get textures
                    var textureNames = new List<string>();
                    var textureHash = new HashSet<Texture2D>();
                    var texMaterialMap = new Dictionary<Texture2D, List<TexMaterialMap>>();
                    var textures = materials.SelectMany(mat =>
                    {
                        var shader = mat.shader;
                        if (!shader) return Enumerable.Empty<Texture2D>();

                        var matTextures = new List<Texture2D>();
                        for (var i = 0; i < ShaderUtil.GetPropertyCount(shader); ++i)
                        {
                            if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                            {
                                var propertyName = ShaderUtil.GetPropertyName(shader, i);
                                var tex = mat.GetTexture(propertyName) as Texture2D;
                                if (tex)
                                {
                                    // Check if texture should be imported or if we should use a
                                    // texture that already exists in the unity project
                                    string originalPath = gltfImporter.GetOriginalPathOfTexture(tex);
                                    bool textureAlreadyInProject = false;
                                    Texture2D texInProject;
                                    if (originalPath != null && originalPath.Length > 0)
                                    {
                                        string toBeRemoved = "Assets/";
                                        int removeLen = toBeRemoved.Length;
                                        if (originalPath.Length > removeLen)
                                        {
                                            string fullPath = Path.Combine(
                                                Application.dataPath, originalPath.Substring(
                                                    removeLen, originalPath.Length - removeLen));
                                            if (File.Exists(fullPath))
                                            {
                                                texInProject = (Texture2D)AssetDatabase.LoadAssetAtPath(originalPath, typeof(Texture2D));
                                                if (texInProject != null)
                                                {
                                                    // If the file exist already in the project we should skip importing it.
                                                    // TODO check filesize and maybe also timestamp and compare with the file
                                                    // being imported.
                                                    textureAlreadyInProject = true;
                                                    mat.SetTexture(propertyName, texInProject);
                                                }
                                            }
                                        }
                                    }
                                    if (!textureAlreadyInProject)
                                    {
                                        texturesToImport.Add(tex);
                                    }

                                    if (textureHash.Add(tex))
                                    {
                                        var texName = tex.name;
                                        if (string.IsNullOrEmpty(texName))
                                        {
                                            if (propertyName.StartsWith("_")) texName = propertyName.Substring(Mathf.Min(1, propertyName.Length - 1));
                                        }

                                        // Ensure name is unique
                                        texName = string.Format("{0} {1}", sceneName, ObjectNames.NicifyVariableName(texName));
                                        texName = texName.Replace(' ', '_');
                                        //texName = ObjectNames.GetUniqueName(textureNames.ToArray(), texName);
                                        texName = MakeNameUnique(textureNames.ToArray(), texName);

                                        tex.name = texName;
                                        textureNames.Add(texName);
                                        matTextures.Add(tex);
                                    }

                                    List<TexMaterialMap> materialMaps;
                                    if (!texMaterialMap.TryGetValue(tex, out materialMaps))
                                    {
                                        materialMaps = new List<TexMaterialMap>();
                                        texMaterialMap.Add(tex, materialMaps);
                                    }

                                    materialMaps.Add(new TexMaterialMap(mat, propertyName, propertyName == "_BumpMap"));
                                }
                            }
                        }
                        return matTextures;
                    }).ToArray();

                    var folderName = Path.GetDirectoryName(ctx.assetPath);

                    List<Texture2D> importedTextures = new List<Texture2D>();

                    // Save textures as separate assets and rewrite refs
                    // TODO: Support for other texture types

                    if (textures.Length > 0)
                    {
                        var texturesRoot = string.Concat(folderName, "/", "Textures/");
                        if (!Directory.Exists(texturesRoot))
                        {
                            Directory.CreateDirectory(texturesRoot);
                        }

                        foreach (var tex in textures)
                        {
                            var ext = _useJpgTextures ? ".jpg" : ".png";
                            var texPath = string.Concat(texturesRoot, tex.name, ext);
                            if (!File.Exists(texPath) && texturesToImport.Contains(tex))
                            {
                                File.WriteAllBytes(texPath, _useJpgTextures ? tex.EncodeToJPG() : tex.EncodeToPNG());
                                AssetDatabase.ImportAsset(texPath);
                                importedTextures.Add(tex);
                            }
                        }
                    }

                    // Save materials as separate assets and rewrite refs
                    if (materials.Length > 0)
                    {
                        var materialRoot = string.Concat(folderName, "/", "Materials/");
                        Directory.CreateDirectory(materialRoot);

                        foreach (var mat in materials)
                        {
                            var materialPath = string.Concat(materialRoot, mat.name, ".mat");
                            var newMat = mat;
                            CopyOrNew(mat, materialPath, m =>
                            {
                                // Fix references
                                newMat = m;
                                foreach (var r in renderers)
                                {
                                    var sharedMaterials = r.sharedMaterials;
                                    for (var i = 0; i < sharedMaterials.Length; ++i)
                                    {
                                        var sharedMaterial = sharedMaterials[i];
                                        if (sharedMaterial.name == mat.name) sharedMaterials[i] = m;
                                    }
                                    sharedMaterials = sharedMaterials.Where(sm => sm).ToArray();
                                    r.sharedMaterials = sharedMaterials;
                                }
                            });
                            // Fix textures
                            // HACK: This needs to be a delayed call.
                            // Unity needs a frame to kick off the texture import so we can rewrite the ref
                            if (importedTextures.Count > 0)
                            {
                                EditorApplication.delayCall += () =>
                                {
                                    for (var i = 0; i < importedTextures.Count; ++i)
                                    {
                                        var tex = importedTextures[i];
                                        var texturesRoot = string.Concat(folderName, "/", "Textures/");
                                        var ext = _useJpgTextures ? ".jpg" : ".png";
                                        var texPath = string.Concat(texturesRoot, tex.name, ext);

                                        // Grab new imported texture
                                        var materialMaps = texMaterialMap[tex];
                                        var importer = (TextureImporter)TextureImporter.GetAtPath(texPath);
                                        var importedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                                        if (importer != null)
                                        {
                                            var isNormalMap = false;
                                            foreach (var materialMap in materialMaps)
                                            {
                                                if (materialMap.Material == mat)
                                                {
                                                    isNormalMap |= materialMap.IsNormalMap;
                                                    newMat.SetTexture(materialMap.Property, importedTex);
                                                }
                                            };

                                            if (isNormalMap)
                                            {
                                                // Try to auto-detect normal maps
                                                importer.textureType = TextureImporterType.NormalMap;
                                            }
                                            else if (importer.textureType == TextureImporterType.Sprite)
                                            {
                                                // Force disable sprite mode, even for 2D projects
                                                importer.textureType = TextureImporterType.Default;
                                            }

                                            importer.SaveAndReimport();
                                        }
                                        else
                                        {
                                            Debug.LogWarning("GLTFImporter: Unable to import texture from path reference");
                                        }
                                    }
                                };
                            }
                        }

                        Debug.Log(String.Format("Imported {0} materials.", materials.Length));
                    }
                }
                else
                {
                    var temp = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    temp.SetActive(false);
                    var defaultMat = new[] { temp.GetComponent<Renderer>().sharedMaterial };
                    DestroyImmediate(temp);

                    foreach (var rend in renderers)
                    {
                        rend.sharedMaterials = defaultMat;
                    }
                }
            }
            catch
            {
                if (gltfScene) DestroyImmediate(gltfScene);
                throw;
            }

#if UNITY_2017_3_OR_NEWER
			// Set main asset
			ctx.AddObjectToAsset("main asset", gltfScene);

			// Add meshes
			foreach (var mesh in meshHash)
			{
				ctx.AddObjectToAsset("mesh " + mesh.name, mesh);
			}

			ctx.SetMainObject(gltfScene);
#else
            // Set main asset
            ctx.SetMainAsset("main asset", gltfScene);

            // Add meshes
            foreach (var mesh in meshes)
            {
                ctx.AddSubAsset("mesh " + mesh.name, mesh);
            }
#endif
        }

        /** Adds incremental number to name if it is found in existingNames */
        public static string MakeNameUnique(string[] existingNames, string name)
        {
            string newName = name;
            int counter = 0;
            bool look = true;
            while(look)
            {
                look = false;
                foreach(string exi in existingNames)
                {
                    if(newName.Equals(exi))
                    {
                        counter++;
                        newName = string.Format("{0}({1})", name, counter);
                        look = true;
                        break;
                    }
                }
            }
            return newName;
        }

        private GameObject CreateGLTFScene(string projectFilePath, out GLTFSceneImporter importer)
        {
			var importOptions = new ImportOptions
			{
				DataLoader = new FileLoader(Path.GetDirectoryName(projectFilePath)),
			};
			using (var stream = File.OpenRead(projectFilePath))
			{
				GLTFRoot gLTFRoot;
				GLTFParser.ParseJson(stream, out gLTFRoot);
				stream.Position = 0; // Make sure the read position is changed back to the beginning of the file
				var loader = new GLTFSceneImporter(gLTFRoot, stream, importOptions);

                // Getting custom shader to use for other rendering
                // pipelines than the built in rendering pipiline in Unity
                string shaderName = null;
                // REMARK: This code does not work in Unity 2019.1 - 2019.2
                // Some modifications are needed in order to make it work in those versions
#if UNITY_2019_3_OR_NEWER
                if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
                {
                    Shader shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultShader;
                    if (shader != null)
                    {
                        shaderName = shader.name;
                    }
                }
#else
                UnityEngine.Experimental.Rendering.RenderPipelineAsset rendas = UnityEngine.Rendering.GraphicsSettings.renderPipelineAsset;
                if (rendas != null)
                {
                    Shader shader = rendas.GetDefaultShader();
                    if (shader != null)
                    {
                        shaderName = shader.name;
                    }
                }
#endif
                if (shaderName != null)
                {
                    loader.CustomShaderName = shaderName;
                }

                loader.MaximumLod = _maximumLod;
				loader.IsMultithreaded = true;

				loader.LoadSceneAsync().Wait();
                importer = loader;
				return loader.LastLoadedScene;
			}
        }

        private void CopyOrNew<T>(T asset, string assetPath, Action<T> replaceReferences) where T : Object
        {
            var existingAsset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existingAsset)
            {
                EditorUtility.CopySerialized(asset, existingAsset);
                replaceReferences(existingAsset);
                return;
            }

            AssetDatabase.CreateAsset(asset, assetPath);
        }

        private class TexMaterialMap
        {
            public UnityEngine.Material Material { get; set; }
            public string Property { get; set; }
            public bool IsNormalMap { get; set; }

            public TexMaterialMap(UnityEngine.Material material, string property, bool isNormalMap)
            {
                Material = material;
                Property = property;
                IsNormalMap = isNormalMap;
            }
        }
    }
}
#endif
