﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GLTF.Schema;
using UnityEngine;
using UnityEngine.Rendering;
using ArkioUnityPlugin.Extensions;
using CameraType = GLTF.Schema.CameraType;
using WrapMode = GLTF.Schema.WrapMode;
using UnityEditor;

namespace ArkioUnityPlugin
{
	public class ExportOptions
	{
		public GLTFSceneExporter.RetrieveTexturePathDelegate TexturePathRetriever = (texture) => texture.name;
		public bool ExportInactivePrimitives = true;
	}

	public class GLTFSceneExporter
	{
        // Maximum number of nodes to export
        public int maxNodes = 2000;

        // Set to true if node count in model
        // exceeds maximum node count
        private bool maxNodeCountExceeded = false;
        public bool MaxNodeCountExceeded
        {
            get { return maxNodeCountExceeded; }
        }

        // If true, everything in prefabs instances
        // will be combined into single nodes
        public bool combinePrefabs = true;

        // If true, metallic texture will be exported, else not.
        public bool exportMetallicTexture = false;

        // If true, normal map will be exported, else not
        public bool exportNormalMapTexture = false;

        // If true, emission texture will be exported, else not.
        public bool exportEmissionTexture = false;

        // If true, occlusion texture will be exported, else not.
        public bool exportOcclusionTexture = false;

        // If true, light map will be exported, else not.
        public bool exportLightmapTexture = false;

        // List of GameObjects to exclude from prefab combining
        public GameObject[] prefabCombineExcludeList;

        private byte[] EncodeTexture(Texture2D tex)
        {
            // JGP results in smaller exports while PNG is lossless
            return tex.EncodeToJPG();
            //return tex.EncodeToPNG();
        }

        // total triangle count of exported model
        // counts triangles for each node
        // even if more than one node use the same mesh
        private int totalTriangleCount = 0;
        public int TotalTriangleCount
        {
            get { return totalTriangleCount; }
        }

        // Triangle count for each mesh
        // Keys are mesh ids
        // Values are the triangle count for the mesh with that id
        Dictionary<int, int> meshTriangleCount = new Dictionary<int, int>();

        /** The number of nodes in the gltf file that was written */
        protected int nrOfNodesExported = 0;
        public int NrOfNodesExported
        {
            get { return nrOfNodesExported; }
        }

        /** Number of meshes written to gltf file. */
        protected int nrOfMeshesExported = 0;
        public int NrOfMeshesExported
        {
            get { return nrOfMeshesExported; }
        }

        // Tells which nodes should be exported or not
        Dictionary<Transform, bool> shouldBeExported;

        // Returns true if a transform should be exported or not
        // We may want to skip certain GameObjects, e.g. objects
        // that are not active or don't have a mesh
        private bool ShouldNodeBeExported(Transform t)
        {
            if (shouldBeExported.ContainsKey(t))
            {
                return shouldBeExported[t];
            }

            // We want to check if node contains any visible meshes
            if (t.gameObject.activeSelf)
            {
                if (ContainsValidRenderer(t.gameObject))
                {
                    shouldBeExported[t] = true;
                    return true;
                }
            }
            int childCount = t.childCount;
            for(int i = 0; i < childCount; i++)
            {
                Transform child = t.GetChild(i);
                bool sbe = ShouldNodeBeExported(child);
                if (sbe)
                {
                    shouldBeExported[t] = true;
                    return true;
                }
            }
            shouldBeExported[t] = false;
            return false;
        }

        public delegate string RetrieveTexturePathDelegate(Texture texture);

		private enum IMAGETYPE
		{
			RGB,
			RGBA,
			R,
			G,
			B,
			A,
			G_INVERT
		}

		private enum TextureMapType
		{
			Main,
			Bump,
			SpecGloss,
			Emission,
			MetallicGloss,
			Light,
			Occlusion
		}

		private struct ImageInfo
		{
			public Texture2D texture;
			public TextureMapType textureMapType;
		}

		private Transform[] _rootTransforms;
		private GLTFRoot _root;
		private BufferId _bufferId;
		private GLTFBuffer _buffer;
		private BinaryWriter _bufferWriter;
		private List<ImageInfo> _imageInfos;
		private List<Texture> _textures;
		private List<Material> _materials;
		private bool _shouldUseInternalBufferForImages;

		private ExportOptions _exportOptions;

        // Dictionary with some info about each GameObject that is exported
        private Dictionary<GameObject, ArkioEntityIDTable.ArkioObjectInfo> _entityInfo;

		private Material _metalGlossChannelSwapMaterial;
		private Material _normalChannelMaterial;

		private const uint MagicGLTF = 0x46546C67;
		private const uint Version = 2;
		private const uint MagicJson = 0x4E4F534A;
		private const uint MagicBin = 0x004E4942;
		private const int GLTFHeaderSize = 12;
		private const int SectionHeaderSize = 8;

        // Mesh key that can be used as a unique identifier
        // for a mesh that has certain materials assigned
        // to it's submeshes
		protected struct MeshKey
		{
            // The Unity mesh
			public Mesh Mesh;

            // String containing info on materials assigned to the mesh in the scene
            public string Materials;

            // Create a MeshKey from a mesh and an array of materials
            public static MeshKey Create(Mesh me, Material[] mats)
            {
                MeshKey mk = new MeshKey();
                mk.Mesh = me;

                StringBuilder matStr = new StringBuilder();
                matStr.Append("");
                if (mats != null)
                {
                    if (mats.Length > 0)
                    {
                        foreach (Material mat in mats)
                        {
                            matStr.Append(AssetDatabase.GetAssetPath(mat));
                            matStr.Append(";");
                        }
                    }
                }

                mk.Materials = matStr.ToString();

                return mk;
            }
        }

        // Mapping from mesh key to gltf mesh id
        // So we can know if a particular mesh with certain materials has
        // already been extracted from the scene
		private readonly Dictionary<MeshKey, MeshId> exportedMeshes = new Dictionary<MeshKey, MeshId>();

		// Settings
		public static bool ExportNames = true;
        public static bool ExportFullPath = false;
		public static bool RequireExtensions = false;

        /// <summary>
        /// Create a GLTFExporter that exports out a transform
        /// </summary>
        /// <param name="rootTransforms">Root transform of object to export</param>
        /// <param name="entityInfo">Dictionary with mapping from GameObject to an entity info.</param
        [Obsolete("Please switch to GLTFSceneExporter(Transform[] rootTransforms, ExportOptions options).  This constructor is deprecated and will be removed in a future release.")]
		public GLTFSceneExporter(Transform[] rootTransforms,
            Dictionary<GameObject, ArkioEntityIDTable.ArkioObjectInfo> entityInfo,
            RetrieveTexturePathDelegate texturePathRetriever)
			: this(rootTransforms, entityInfo, new ExportOptions { TexturePathRetriever = texturePathRetriever })
		{
		}

        /// <summary>
        /// Create a GLTFExporter that exports out a transform
        /// </summary>
        /// <param name="rootTransforms">Root transform of object to export</param>
        /// <param name="entityInfo">Dictionary with mapping from GameObject to an entity info.</param>
        public GLTFSceneExporter(Transform[] rootTransforms, Dictionary<GameObject, ArkioEntityIDTable.ArkioObjectInfo> entityInfo, ExportOptions options)
		{
            _entityInfo = entityInfo;

			_exportOptions = options;

			var metalGlossChannelSwapShader = Resources.Load("MetalGlossChannelSwap", typeof(Shader)) as Shader;
			_metalGlossChannelSwapMaterial = new Material(metalGlossChannelSwapShader);

			var normalChannelShader = Resources.Load("NormalChannel", typeof(Shader)) as Shader;
			_normalChannelMaterial = new Material(normalChannelShader);

			_rootTransforms = rootTransforms;
			_root = new GLTFRoot
			{
				Accessors = new List<Accessor>(),
				Asset = new Asset
				{
					Version = "2.0"
				},
				Buffers = new List<GLTFBuffer>(),
				BufferViews = new List<BufferView>(),
				Cameras = new List<GLTFCamera>(),
				Images = new List<GLTFImage>(),
				Materials = new List<GLTFMaterial>(),
				Meshes = new List<GLTFMesh>(),
				Nodes = new List<Node>(),
				Samplers = new List<Sampler>(),
				Scenes = new List<GLTFScene>(),
				Textures = new List<GLTFTexture>()
			};

			_imageInfos = new List<ImageInfo>();
			_materials = new List<Material>();
			_textures = new List<Texture>();

			_buffer = new GLTFBuffer();
			_bufferId = new BufferId
			{
				Id = _root.Buffers.Count,
				Root = _root
			};
			_root.Buffers.Add(_buffer);
		}

		/// <summary>
		/// Gets the root object of the exported GLTF
		/// </summary>
		/// <returns>Root parsed GLTF Json</returns>
		public GLTFRoot GetRoot()
		{
			return _root;
		}

		/// <summary>
		/// Writes a binary GLB file with filename at path.
		/// </summary>
		/// <param name="path">File path for saving the binary file</param>
		/// <param name="fileName">The name of the GLTF file</param>
		public void SaveGLB(string path, string fileName)
		{
			_shouldUseInternalBufferForImages = false;
			string fullPath = Path.Combine(path, Path.ChangeExtension(fileName, "glb"));
			
			using (FileStream glbFile = new FileStream(fullPath, FileMode.Create))
			{
				SaveGLBToStream(glbFile, fileName);
			}

			if (!_shouldUseInternalBufferForImages)
			{
				ExportImages(path);
			}
		}

		/// <summary>
		/// In-memory GLB creation helper. Useful for platforms where no filesystem is available (e.g. WebGL).
		/// </summary>
		/// <param name="sceneName"></param>
		/// <returns></returns>
		public byte[] SaveGLBToByteArray(string sceneName)
		{
			using (var stream = new MemoryStream())
			{
				SaveGLBToStream(stream, sceneName);
				return stream.ToArray();
			}
		}

		/// <summary>
		/// Writes a binary GLB file into a stream (memory stream, filestream, ...)
		/// </summary>
		/// <param name="path">File path for saving the binary file</param>
		/// <param name="fileName">The name of the GLTF file</param>
		public void SaveGLBToStream(Stream stream, string sceneName)
		{
			Stream binStream = new MemoryStream();
			Stream jsonStream = new MemoryStream();

			_bufferWriter = new BinaryWriter(binStream);

			TextWriter jsonWriter = new StreamWriter(jsonStream, Encoding.ASCII);

			_root.Scene = ExportScene(sceneName, _rootTransforms);

			_buffer.ByteLength = CalculateAlignment((uint)_bufferWriter.BaseStream.Length, 4);


            this.nrOfMeshesExported = _root.Meshes.Count;
            this.nrOfNodesExported = _root.Nodes.Count;
			_root.Serialize(jsonWriter, true);

			_bufferWriter.Flush();
			jsonWriter.Flush();

			// align to 4-byte boundary to comply with spec.
			AlignToBoundary(jsonStream);
			AlignToBoundary(binStream, 0x00);

			int glbLength = (int)(GLTFHeaderSize + SectionHeaderSize +
				jsonStream.Length + SectionHeaderSize + binStream.Length);

			BinaryWriter writer = new BinaryWriter(stream);

			// write header
			writer.Write(MagicGLTF);
			writer.Write(Version);
			writer.Write(glbLength);

			// write JSON chunk header.
			writer.Write((int)jsonStream.Length);
			writer.Write(MagicJson);

			jsonStream.Position = 0;
			CopyStream(jsonStream, writer);

			writer.Write((int)binStream.Length);
			writer.Write(MagicBin);

			binStream.Position = 0;
			CopyStream(binStream, writer);

			writer.Flush();
		}

		/// <summary>
		/// Convenience function to copy from a stream to a binary writer, for
		/// compatibility with pre-.NET 4.0.
		/// Note: Does not set position/seek in either stream. After executing,
		/// the input buffer's position should be the end of the stream.
		/// </summary>
		/// <param name="input">Stream to copy from</param>
		/// <param name="output">Stream to copy to.</param>
		private static void CopyStream(Stream input, BinaryWriter output)
		{
			byte[] buffer = new byte[8 * 1024];
			int length;
			while ((length = input.Read(buffer, 0, buffer.Length)) > 0)
			{
				output.Write(buffer, 0, length);
			}
		}

		/// <summary>
		/// Pads a stream with additional bytes.
		/// </summary>
		/// <param name="stream">The stream to be modified.</param>
		/// <param name="pad">The padding byte to append. Defaults to ASCII
		/// space (' ').</param>
		/// <param name="boundary">The boundary to align with, in bytes.
		/// </param>
		private static void AlignToBoundary(Stream stream, byte pad = (byte)' ', uint boundary = 4)
		{
			uint currentLength = (uint)stream.Length;
			uint newLength = CalculateAlignment(currentLength, boundary);
			for (int i = 0; i < newLength - currentLength; i++)
			{
				stream.WriteByte(pad);
			}
		}

		/// <summary>
		/// Calculates the number of bytes of padding required to align the
		/// size of a buffer with some multiple of byteAllignment.
		/// </summary>
		/// <param name="currentSize">The current size of the buffer.</param>
		/// <param name="byteAlignment">The number of bytes to align with.</param>
		/// <returns></returns>
		public static uint CalculateAlignment(uint currentSize, uint byteAlignment)
		{
			return (currentSize + byteAlignment - 1) / byteAlignment * byteAlignment;
		}


		/// <summary>
		/// Specifies the path and filename for the GLTF Json and binary
		/// </summary>
		/// <param name="path">File path for saving the GLTF and binary files</param>
		/// <param name="fileName">The name of the GLTF file</param>
		public void SaveGLTFandBin(string path, string fileName)
		{
			_shouldUseInternalBufferForImages = false;
			var binFile = File.Create(Path.Combine(path, fileName + ".bin"));
			_bufferWriter = new BinaryWriter(binFile);

			_root.Scene = ExportScene(fileName, _rootTransforms);
			AlignToBoundary(_bufferWriter.BaseStream, 0x00);
			_buffer.Uri = fileName + ".bin";
			_buffer.ByteLength = CalculateAlignment((uint)_bufferWriter.BaseStream.Length, 4);

			var gltfFile = File.CreateText(Path.Combine(path, fileName + ".gltf"));

            this.nrOfMeshesExported = _root.Meshes.Count;
            this.nrOfNodesExported = _root.Nodes.Count;
            _root.Serialize(gltfFile);

#if WINDOWS_UWP
			gltfFile.Dispose();
			binFile.Dispose();
#else
			gltfFile.Close();
			binFile.Close();
#endif
			ExportImages(path);

		}

		private void ExportImages(string outputPath)
		{
			for (int t = 0; t < _imageInfos.Count; ++t)
			{
				var image = _imageInfos[t].texture;
				int height = image.height;
				int width = image.width;

				switch (_imageInfos[t].textureMapType)
				{
					case TextureMapType.MetallicGloss:
						ExportMetallicGlossTexture(image, outputPath);
						break;
					case TextureMapType.Bump:
						ExportNormalTexture(image, outputPath);
						break;
					default:
						ExportTexture(image, outputPath);
						break;
				}
			}
		}

		/// <summary>
		/// This converts Unity's metallic-gloss texture representation into GLTF's metallic-roughness specifications.
		/// Unity's metallic-gloss A channel (glossiness) is inverted and goes into GLTF's metallic-roughness G channel (roughness).
		/// Unity's metallic-gloss R channel (metallic) goes into GLTF's metallic-roughess B channel.
		/// </summary>
		/// <param name="texture">Unity's metallic-gloss texture to be exported</param>
		/// <param name="outputPath">The location to export the texture</param>
		private void ExportMetallicGlossTexture(Texture2D texture, string outputPath)
		{
			var destRenderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

			Graphics.Blit(texture, destRenderTexture, _metalGlossChannelSwapMaterial);

			var exportTexture = new Texture2D(texture.width, texture.height);
			exportTexture.ReadPixels(new Rect(0, 0, destRenderTexture.width, destRenderTexture.height), 0, 0);
			exportTexture.Apply();

			var finalFilenamePath = ConstructImageFilenamePath(texture, outputPath);
			File.WriteAllBytes(finalFilenamePath, EncodeTexture(exportTexture));

			RenderTexture.ReleaseTemporary(destRenderTexture);
			if (Application.isEditor)
			{
				GameObject.DestroyImmediate(exportTexture);
			}
			else
			{
				GameObject.Destroy(exportTexture);
			}
		}

		/// <summary>
		/// This export's the normal texture. If a texture is marked as a normal map, the values are stored in the A and G channel.
		/// To output the correct normal texture, the A channel is put into the R channel.
		/// </summary>
		/// <param name="texture">Unity's normal texture to be exported</param>
		/// <param name="outputPath">The location to export the texture</param>
		private void ExportNormalTexture(Texture2D texture, string outputPath)
		{
			var destRenderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

			Graphics.Blit(texture, destRenderTexture, _normalChannelMaterial);

			var exportTexture = new Texture2D(texture.width, texture.height);
			exportTexture.ReadPixels(new Rect(0, 0, destRenderTexture.width, destRenderTexture.height), 0, 0);
			exportTexture.Apply();

			var finalFilenamePath = ConstructImageFilenamePath(texture, outputPath);
			File.WriteAllBytes(finalFilenamePath, EncodeTexture(exportTexture));

			RenderTexture.ReleaseTemporary(destRenderTexture);
			if (Application.isEditor)
			{
				GameObject.DestroyImmediate(exportTexture);
			}
			else
			{
				GameObject.Destroy(exportTexture);
			}
		}

		private void ExportTexture(Texture2D texture, string outputPath)
		{
			var destRenderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

			Graphics.Blit(texture, destRenderTexture);

			var exportTexture = new Texture2D(texture.width, texture.height);
			exportTexture.ReadPixels(new Rect(0, 0, destRenderTexture.width, destRenderTexture.height), 0, 0);
			exportTexture.Apply();

			var finalFilenamePath = ConstructImageFilenamePath(texture, outputPath);
			File.WriteAllBytes(finalFilenamePath, EncodeTexture(exportTexture));

			RenderTexture.ReleaseTemporary(destRenderTexture);
			if (Application.isEditor)
			{
				GameObject.DestroyImmediate(exportTexture);
			}
			else
			{
				GameObject.Destroy(exportTexture);
			}
		}

		private string ConstructImageFilenamePath(Texture2D texture, string outputPath)
		{
			var imagePath = _exportOptions.TexturePathRetriever(texture);
			if (string.IsNullOrEmpty(imagePath))
			{
				imagePath = Path.Combine(outputPath, texture.name);
			}

			var filenamePath = Path.Combine(outputPath, imagePath);
			if (!ExportFullPath)
			{
				filenamePath = outputPath + "/" + texture.name;
			}
			var file = new FileInfo(filenamePath);
			file.Directory.Create();
			return Path.ChangeExtension(filenamePath, ".png");
		}

		private SceneId ExportScene(string name, Transform[] rootObjTransforms)
		{
            shouldBeExported = new Dictionary<Transform, bool>();
			var scene = new GLTFScene();

			if (ExportNames)
			{
				scene.Name = name;
			}

			scene.Nodes = new List<NodeId>(rootObjTransforms.Length);
            foreach (var transform in rootObjTransforms)
            {
                if (_root.Nodes.Count < maxNodes)
                {
                    if (ShouldNodeBeExported(transform))
                    {
                        scene.Nodes.Add(ExportNode(transform));
                    }
                }
                else
                {
                    maxNodeCountExceeded = true;
                    break;
                }
			}

			_root.Scenes.Add(scene);

			return new SceneId
			{
				Id = _root.Scenes.Count - 1,
				Root = _root
			};
		}

        // Get all children under a GameObject
        public List<GameObject> GetAllChildrenRecursively(GameObject go)
        {
            List<GameObject> children = new List<GameObject>();
            int childCount = go.transform.childCount;
            for(int i = 0; i < childCount; i++)
            {
                GameObject child = go.transform.GetChild(i).gameObject;
                children.Add(child);

                List<GameObject> innerChildren = GetAllChildrenRecursively(child);
                children.AddRange(innerChildren);
            }
            return children;
        }

        // Check if game object has a visible mesh
        public static bool HasVisibleMesh(GameObject go)
        {
            if (go.activeInHierarchy)
            {
                if (ContainsValidRenderer(go))
                {
                    return true;
                }
            }
            return false;
        }

        // Export a node
        // nodeTransform : The transform of the GameObject to export as a node
        private NodeId ExportNode(Transform nodeTransform)
        {
            // Deciding if this node and its children
            // should all be merged into one node
            // If true, only one Node will be created containg all the meshes
            // of inner GameObjects (child transforms)
            bool mergeChildren = false;
            if (combinePrefabs)
            {
                // If it is a prefab root then combine everything under it into one node
                mergeChildren = PrefabUtility.IsAnyPrefabInstanceRoot(nodeTransform.gameObject);
                if (mergeChildren)
                {
                    if (prefabCombineExcludeList != null)
                    {
                        // Check if it is in the exclude list
                        foreach (GameObject exl in prefabCombineExcludeList)
                        {
                            if (nodeTransform.gameObject.GetInstanceID() == exl.GetInstanceID())
                            {
                                mergeChildren = false;
                                break;
                            }
                        }
                    }
                }
            }

            var node = new Node();

			if (ExportNames)
			{
                GameObject go = nodeTransform.gameObject;
                Debug.Assert(_entityInfo.ContainsKey(go), "entity info should contain GameObject being exported!");
                ArkioEntityIDTable.ArkioObjectInfo objInfo = _entityInfo[go];
                node.Name = ArkioIEUtil.CreateNameForObject(objInfo);
			}

            // Check if there is a terrain on the GameObject
            Terrain terr = nodeTransform.GetComponent<Terrain>();
            if (terr != null)
            {
                Debug.LogWarning(@"WARNING - Terrain needs to be converted to mesh filter in order to show in Arkio");
            }

			//export camera attached to node
			Camera unityCamera = nodeTransform.GetComponent<Camera>();
			if (unityCamera != null)
			{
				node.Camera = ExportCamera(unityCamera);
			}

			node.SetUnityTransform(nodeTransform);

			var id = new NodeId
			{
				Id = _root.Nodes.Count,
				Root = _root
			};
			_root.Nodes.Add(node);

			// children that are primitives get put in a mesh
			GameObject[] primitives, nonPrimitives;
            if (mergeChildren)
            {
                // If mergeChildren is true, we want to combine everything 
                // in this GameObject and everything under it into one mesh

                // Get all children
                List<GameObject> nodeObjects = GetAllChildrenRecursively(nodeTransform.gameObject);

                // Also include this object
                nodeObjects.Add(nodeTransform.gameObject);

                // Find the ones the should be exported
                List<GameObject> toExport = new List<GameObject>();
                foreach(GameObject obj in nodeObjects)
                {
                    if (HasVisibleMesh(obj))
                    {
                        toExport.Add(obj);
                    }
                }
                primitives = toExport.ToArray();
                nonPrimitives = new GameObject[0];
            }
            else
            {
			    FilterPrimitives(nodeTransform, out primitives, out nonPrimitives);
            }

			if (primitives.Length > 0)
			{
				node.Mesh = ExportMesh(nodeTransform, primitives);

                // Get triangle count of the nodes mesh and
                // add to totalTriangleCount
                // If more than one node use the same mesh
                // then we count the tringles of that mesh
                // more than once
                int triCount = meshTriangleCount[node.Mesh.Id];
                totalTriangleCount += triCount;
			}

			// children that are not primitives get added as child nodes
			if (nonPrimitives.Length > 0)
			{
				node.Children = new List<NodeId>(nonPrimitives.Length);
				foreach (var child in nonPrimitives)
				{
                    if (_root.Nodes.Count < maxNodes)
                    {
                        if (ShouldNodeBeExported(child.transform))
                        {
                            node.Children.Add(ExportNode(child.transform));
                        }
                    }
                    else
                    {
                        maxNodeCountExceeded = true;
                        break;
                    }
				}
			}

			return id;
		}

		private CameraId ExportCamera(Camera unityCamera)
		{
			GLTFCamera camera = new GLTFCamera();
			//name
			camera.Name = unityCamera.name;

			//type
			bool isOrthographic = unityCamera.orthographic;
			camera.Type = isOrthographic ? CameraType.orthographic : CameraType.perspective;
			Matrix4x4 matrix = unityCamera.projectionMatrix;

			//matrix properties: compute the fields from the projection matrix
			if (isOrthographic)
			{
				CameraOrthographic ortho = new CameraOrthographic();

				ortho.XMag = 1 / matrix[0, 0];
				ortho.YMag = 1 / matrix[1, 1];

				float farClip = (matrix[2, 3] / matrix[2, 2]) - (1 / matrix[2, 2]);
				float nearClip = farClip + (2 / matrix[2, 2]);
				ortho.ZFar = farClip;
				ortho.ZNear = nearClip;

				camera.Orthographic = ortho;
			}
			else
			{
				CameraPerspective perspective = new CameraPerspective();
				float fov = 2 * Mathf.Atan(1 / matrix[1, 1]);
				float aspectRatio = matrix[1, 1] / matrix[0, 0];
				perspective.YFov = fov;
				perspective.AspectRatio = aspectRatio;

				if (matrix[2, 2] == -1)
				{
					//infinite projection matrix
					float nearClip = matrix[2, 3] * -0.5f;
					perspective.ZNear = nearClip;
				}
				else
				{
					//finite projection matrix
					float farClip = matrix[2, 3] / (matrix[2, 2] + 1);
					float nearClip = farClip * (matrix[2, 2] + 1) / (matrix[2, 2] - 1);
					perspective.ZFar = farClip;
					perspective.ZNear = nearClip;
				}
				camera.Perspective = perspective;
			}

			var id = new CameraId
			{
				Id = _root.Cameras.Count,
				Root = _root
			};

			_root.Cameras.Add(camera);

			return id;
		}

		private static bool ContainsValidRenderer (GameObject gameObject)
		{
			if (!gameObject.activeInHierarchy)
            {
				/* If the game object is not active we consider the
				 * renderer not be valid */
				return false;
            }

			MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
			MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
			if (meshFilter != null && meshRenderer != null)
            {
				return meshRenderer.enabled;
            }

			SkinnedMeshRenderer skinnedRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();
			if (skinnedRenderer != null)
            {
				return skinnedRenderer.enabled;
            }

			return false;
		}

		private void FilterPrimitives(Transform transform, out GameObject[] primitives, out GameObject[] nonPrimitives)
		{
			var childCount = transform.childCount;
			var prims = new List<GameObject>(childCount + 1);
			var nonPrims = new List<GameObject>(childCount);

			// add another primitive if the root object also has a mesh
			if (transform.gameObject.activeSelf)
			{
				if (ContainsValidRenderer(transform.gameObject))
				{
					prims.Add(transform.gameObject);
				}
			}
			for (var i = 0; i < childCount; i++)
			{
				var go = transform.GetChild(i).gameObject;
				if (IsPrimitive(go))
					prims.Add(go);
				else
					nonPrims.Add(go);
			}

			primitives = prims.ToArray();
			nonPrimitives = nonPrims.ToArray();
		}

		private static bool IsPrimitive(GameObject gameObject)
		{
			/*
			 * Primitives have the following properties:
			 * - have no children
			 * - have no non-default local transform properties
			 * - have MeshFilter and MeshRenderer components OR has SkinnedMeshRenderer component
			 */
			return gameObject.transform.childCount == 0
				&& gameObject.transform.localPosition == Vector3.zero
				&& gameObject.transform.localRotation == Quaternion.identity
				&& gameObject.transform.localScale == Vector3.one
				&& ContainsValidRenderer(gameObject);
		}

        // Export a mesh
        // nodeTransform: Transform of the GameObject holding the mesh, 
        // In some cases it may be a prefab root GameObject containing multiple child GameObjects
        // and then all the meshes in those will be collected and put into one mesh.
        // primitives: GameObjects holding meshes to be exported
		private MeshId ExportMesh(Transform nodeTransform, GameObject[] primitives)
		{
            // Set to true if this is a single GameObject
            // being exported
            bool singleGameObject = false;

            // If it is a single GameObject then create a key for the mesh
            // so we can check if the mesh has already been extracted
            // or if we want to use it later, for another GameObject
            // that uses the same mesh
            MeshKey key = new MeshKey();
            if (primitives.Length == 1)
            {
                GameObject prim = primitives[0];
                if (nodeTransform.gameObject.GetInstanceID() == prim.GetInstanceID())
                {
                    singleGameObject = true;
                    var smr = prim.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null)
                    {
                        key = MeshKey.Create(smr.sharedMesh, smr.sharedMaterials);
                    }
                    else
                    {
                        var filter = prim.GetComponent<MeshFilter>();
                        var renderer = prim.GetComponent<MeshRenderer>();
                        key = MeshKey.Create(filter.sharedMesh, renderer.sharedMaterials);
                    }

                    // Mesh instancing works only for meshes
                    // for single GameObjects. Later we might
                    // want also add instancing for prefabs
                    if (exportedMeshes.ContainsKey(key))
                    {
                        return exportedMeshes[key];
                    }
                }
            }

			// if not, create new mesh and return its id
			var mesh = new GLTFMesh();

			if (ExportNames)
			{
				mesh.Name = nodeTransform.name;
			}

            // Triangle count for this mesh
            int triCount = 0;

			mesh.Primitives = new List<MeshPrimitive>(primitives.Length);
			foreach (var prim in primitives)
			{
                // Triangle count per primitive
                int primTriCount;

                Matrix4x4? mat = null;
                if (nodeTransform.GetInstanceID() != prim.transform.GetInstanceID())
                {
                    // The matrix is only needed if prim is not the main transform
                    // like root of prefab instance
                    mat = nodeTransform.worldToLocalMatrix * prim.transform.localToWorldMatrix;

                    if (((Matrix4x4)mat).isIdentity)
                    {
                        // We don't need to do any transformations if it's an identity matrix
                        mat = null;
                    }
                }

                MeshPrimitive[] meshPrimitives = ExportPrimitive(prim, mesh, mat, out primTriCount);
				if (meshPrimitives != null)
				{
					mesh.Primitives.AddRange(meshPrimitives);
                    triCount += primTriCount;
				}
			}
			
			var id = new MeshId
			{
				Id = _root.Meshes.Count,
				Root = _root
			};
			_root.Meshes.Add(mesh);

            if (singleGameObject)
            {
                exportedMeshes[key] = id;
            }

            meshTriangleCount[id.Id] = triCount;

			return id;
		}

        // Create a gltf primitive for export
        // a mesh *might* decode to multiple prims if there are submeshes
        // gameObject The Unity GameObject to extracta primitive from
        // mesh The gltf mesh that primitives belong to
        // mat A transformation matrix to apply to the mesh geometry. Can be null.
        // If it's null, then the geometry won't be transformed.
        // triangleCount: Number of triangles in this primitive
        private MeshPrimitive[] ExportPrimitive(GameObject gameObject, GLTFMesh mesh, Matrix4x4? mat, out int triangleCount)
		{
            triangleCount = 0;

			Mesh meshObj = null;
			SkinnedMeshRenderer smr = null;
			var filter = gameObject.GetComponent<MeshFilter>();
			if (filter != null)
			{
				meshObj = filter.sharedMesh;
			}
			else
			{
				smr = gameObject.GetComponent<SkinnedMeshRenderer>();
				meshObj = smr.sharedMesh;
			}
			if (meshObj == null)
			{
				Debug.LogError(string.Format("MeshFilter.sharedMesh on gameobject:{0} is missing , skipping", gameObject.name));
				return null;
			}

			var renderer = gameObject.GetComponent<MeshRenderer>();
			var materialsObj = renderer != null ? renderer.sharedMaterials : smr.sharedMaterials;

			var prims = new MeshPrimitive[meshObj.subMeshCount];

            // Commenting this out because after adding support for 
            // combining prefabs. In that case the same mesh might
            // appear in two different prefabs. The final mesh might
            // be different and because of that we do want to
            // get this mesh even if it has been retrieved before
            /*
			MeshPrimitive[] primVariations;
			if (_meshToPrims.TryGetValue(meshObj, out primVariations)
				&& meshObj.subMeshCount == primVariations.Length)
			{
				for (var i = 0; i < primVariations.Length; i++)
				{
					prims[i] = new MeshPrimitive(primVariations[i], _root)
					{
						Material = ExportMaterial(materialsObj[i])
					};
				}

				return prims;
			}
            */

			AccessorId aPosition = null, aNormal = null, aTangent = null,
				aTexcoord0 = null, aTexcoord1 = null, aColor0 = null;

            if (mat != null)
            {
                Matrix4x4 matrix = (Matrix4x4)mat;

                Vector3[] transVert = new Vector3[meshObj.vertices.Length];
                int index = 0;
                foreach (Vector3 v in meshObj.vertices)
                {
                    transVert[index] = matrix.MultiplyPoint(v);
                    index++;
                }

                Vector3[] transNorm = new Vector3[meshObj.normals.Length];
                index = 0;
                foreach (Vector3 n in meshObj.normals)
                {
                    transNorm[index] = matrix.MultiplyVector(n);
                    index++;
                }

                Vector4[] transTang = new Vector4[meshObj.tangents.Length];
                index = 0;
                foreach (Vector4 t in meshObj.tangents)
                {
                    transTang[index] = matrix.MultiplyVector(t);
                    index++;
                }

                aPosition = ExportAccessor(SchemaExtensions.ConvertVector3CoordinateSpaceAndCopy(transVert, SchemaExtensions.CoordinateSpaceConversionScale));

                if (transNorm.Length != 0)
                    aNormal = ExportAccessor(SchemaExtensions.ConvertVector3CoordinateSpaceAndCopy(transNorm, SchemaExtensions.CoordinateSpaceConversionScale));

                if (transTang.Length != 0)
                    aTangent = ExportAccessor(SchemaExtensions.ConvertVector4CoordinateSpaceAndCopy(transTang, SchemaExtensions.TangentSpaceConversionScale));
            }
            else
            {
                aPosition = ExportAccessor(SchemaExtensions.ConvertVector3CoordinateSpaceAndCopy(meshObj.vertices, SchemaExtensions.CoordinateSpaceConversionScale));

                if (meshObj.normals.Length != 0)
                    aNormal = ExportAccessor(SchemaExtensions.ConvertVector3CoordinateSpaceAndCopy(meshObj.normals, SchemaExtensions.CoordinateSpaceConversionScale));

                if (meshObj.tangents.Length != 0)
                    aTangent = ExportAccessor(SchemaExtensions.ConvertVector4CoordinateSpaceAndCopy(meshObj.tangents, SchemaExtensions.TangentSpaceConversionScale));
            }

			if (meshObj.uv.Length != 0)
				aTexcoord0 = ExportAccessor(SchemaExtensions.FlipTexCoordArrayVAndCopy(meshObj.uv));

			if (meshObj.uv2.Length != 0)
				aTexcoord1 = ExportAccessor(SchemaExtensions.FlipTexCoordArrayVAndCopy(meshObj.uv2));

			if (meshObj.colors.Length != 0)
				aColor0 = ExportAccessor(meshObj.colors);

			MaterialId lastMaterialId = null;

			for (var submesh = 0; submesh < meshObj.subMeshCount; submesh++)
			{
				var primitive = new MeshPrimitive();

				var topology = meshObj.GetTopology(submesh);
				var indices = meshObj.GetIndices(submesh);
				if (topology == MeshTopology.Triangles) SchemaExtensions.FlipTriangleFaces(indices);

                triangleCount += indices.Length / 3;

				primitive.Mode = GetDrawMode(topology);
				primitive.Indices = ExportAccessor(indices, true);

				primitive.Attributes = new Dictionary<string, AccessorId>();
				primitive.Attributes.Add(SemanticProperties.POSITION, aPosition);

				if (aNormal != null)
					primitive.Attributes.Add(SemanticProperties.NORMAL, aNormal);
				if (aTangent != null)
					primitive.Attributes.Add(SemanticProperties.TANGENT, aTangent);
				if (aTexcoord0 != null)
					primitive.Attributes.Add(SemanticProperties.TEXCOORD_0, aTexcoord0);
				if (aTexcoord1 != null)
					primitive.Attributes.Add(SemanticProperties.TEXCOORD_1, aTexcoord1);
				if (aColor0 != null)
					primitive.Attributes.Add(SemanticProperties.COLOR_0, aColor0);

				if (submesh < materialsObj.Length)
				{
					primitive.Material = ExportMaterial(materialsObj[submesh]);
					lastMaterialId = primitive.Material;
				}
				else
				{
					primitive.Material = lastMaterialId;
				}

				ExportBlendShapes(smr, meshObj, primitive, mesh);

				prims[submesh] = primitive;
			}

			//_meshToPrims[meshObj] = prims;

			return prims;
		}

		private MaterialId ExportMaterial(Material materialObj)
		{
			MaterialId id = GetMaterialId(_root, materialObj);
			if (id != null)
			{
				return id;
			}

			var material = new GLTFMaterial();

            if (materialObj == null)
            {
                // Create default material
                material.Name = "Default";
                material.PbrMetallicRoughness = new PbrMetallicRoughness();
                material.PbrMetallicRoughness.BaseColorFactor = new GLTF.Math.Color(1, 1, 1, 1);
            }
            else
            {

                if (ExportNames)
                {
                    material.Name = materialObj.name;
                }

                if (materialObj.HasProperty("_Cutoff"))
                {
                    material.AlphaCutoff = materialObj.GetFloat("_Cutoff");
                }

                switch (materialObj.GetTag("RenderType", false, ""))
                {
                    case "TransparentCutout":
                        material.AlphaMode = AlphaMode.MASK;
                        break;
                    case "Transparent":
                        material.AlphaMode = AlphaMode.BLEND;
                        break;
                    default:
                        material.AlphaMode = AlphaMode.OPAQUE;
                        break;
                }

                material.DoubleSided = materialObj.HasProperty("_Cull") &&
                    materialObj.GetInt("_Cull") == (float)CullMode.Off;

                if (materialObj.IsKeywordEnabled("_EMISSION"))
                {
                    if (materialObj.HasProperty("_EmissionColor"))
                    {
                        material.EmissiveFactor = materialObj.GetColor("_EmissionColor").ToNumericsColorRaw();
                    }

                    if (exportEmissionTexture)
                    {
                        if (materialObj.HasProperty("_EmissionMap"))
                        {
                            var emissionTex = materialObj.GetTexture("_EmissionMap");

                            if (emissionTex != null)
                            {
                                if (emissionTex is Texture2D)
                                {
                                    material.EmissiveTexture = ExportTextureInfo(emissionTex, TextureMapType.Emission);

                                    ExportTextureTransform(material.EmissiveTexture, materialObj, "_EmissionMap");
                                }
                                else
                                {
                                    Debug.LogErrorFormat("Can't export a {0} emissive texture in material {1}", emissionTex.GetType(), materialObj.name);
                                }
                            }
                        }
                    }
                }
                if (exportNormalMapTexture)
                {

                    if (materialObj.HasProperty("_BumpMap") && materialObj.IsKeywordEnabled("_NORMALMAP"))
                    {
                        var normalTex = materialObj.GetTexture("_BumpMap");

                        if (normalTex != null)
                        {
                            if (normalTex is Texture2D)
                            {
                                material.NormalTexture = ExportNormalTextureInfo(normalTex, TextureMapType.Bump, materialObj);
                                ExportTextureTransform(material.NormalTexture, materialObj, "_BumpMap");
                            }
                            else
                            {
                                Debug.LogErrorFormat("Can't export a {0} normal texture in material {1}", normalTex.GetType(), materialObj.name);
                            }
                        }
                    }
                }

                if (exportOcclusionTexture)
                {
                    if (materialObj.HasProperty("_OcclusionMap"))
                    {
                        var occTex = materialObj.GetTexture("_OcclusionMap");
                        if (occTex != null)
                        {
                            if (occTex is Texture2D)
                            {
                                material.OcclusionTexture = ExportOcclusionTextureInfo(occTex, TextureMapType.Occlusion, materialObj);
                                ExportTextureTransform(material.OcclusionTexture, materialObj, "_OcclusionMap");
                            }
                            else
                            {
                                Debug.LogErrorFormat("Can't export a {0} occlusion texture in material {1}", occTex.GetType(), materialObj.name);
                            }
                        }
                    }
                }

                if (IsPBRMetallicRoughness(materialObj) || materialObj.HasProperty("_Color") || materialObj.HasProperty("_MainTex"))
                {
                    material.PbrMetallicRoughness = ExportPBRMetallicRoughness(materialObj);
                }
                else if (IsCommonConstant(materialObj))
                {
                    material.CommonConstant = ExportCommonConstant(materialObj);
                }
            }

			_materials.Add(materialObj);

			id = new MaterialId
			{
				Id = _root.Materials.Count,
				Root = _root
			};
			_root.Materials.Add(material);

			return id;
		}

		// Blend Shapes / Morph Targets
		// Adopted from Gary Hsu (bghgary)
		// https://github.com/bghgary/glTF-Tools-for-Unity/blob/master/UnityProject/Assets/Gltf/Editor/Exporter.cs
		private void ExportBlendShapes(SkinnedMeshRenderer smr, Mesh meshObj, MeshPrimitive primitive, GLTFMesh mesh)
		{
			if (smr != null && meshObj.blendShapeCount > 0)
			{
				List<Dictionary<string, AccessorId>> targets = new List<Dictionary<string, AccessorId>>(meshObj.blendShapeCount);
				List<Double> weights = new List<double>(meshObj.blendShapeCount);
				List<string> targetNames = new List<string>(meshObj.blendShapeCount);

				for (int blendShapeIndex = 0; blendShapeIndex < meshObj.blendShapeCount; blendShapeIndex++)
				{

					targetNames.Add(meshObj.GetBlendShapeName(blendShapeIndex));
					// As described above, a blend shape can have multiple frames.  Given that glTF only supports a single frame
					// per blend shape, we'll always use the final frame (the one that would be for when 100% weight is applied).
					int frameIndex = meshObj.GetBlendShapeFrameCount(blendShapeIndex) - 1;

					var deltaVertices = new Vector3[meshObj.vertexCount];
					var deltaNormals = new Vector3[meshObj.vertexCount];
					var deltaTangents = new Vector3[meshObj.vertexCount];
					meshObj.GetBlendShapeFrameVertices(blendShapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);

					targets.Add(new Dictionary<string, AccessorId>
						{
							{ SemanticProperties.POSITION, ExportAccessor(SchemaExtensions.ConvertVector3CoordinateSpaceAndCopy( deltaVertices, SchemaExtensions.CoordinateSpaceConversionScale)) },
							{ SemanticProperties.NORMAL, ExportAccessor(SchemaExtensions.ConvertVector3CoordinateSpaceAndCopy(deltaNormals,SchemaExtensions.CoordinateSpaceConversionScale))},
							{ SemanticProperties.TANGENT, ExportAccessor(SchemaExtensions.ConvertVector3CoordinateSpaceAndCopy(deltaTangents, SchemaExtensions.CoordinateSpaceConversionScale)) },
						});

					// We need to get the weight from the SkinnedMeshRenderer because this represents the currently
					// defined weight by the user to apply to this blend shape.  If we instead got the value from
					// the unityMesh, it would be a _per frame_ weight, and for a single-frame blend shape, that would
					// always be 100.  A blend shape might have more than one frame if a user wanted to more tightly
					// control how a blend shape will be animated during weight changes (e.g. maybe they want changes
					// between 0-50% to be really minor, but between 50-100 to be extreme, hence they'd have two frames
					// where the first frame would have a weight of 50 (meaning any weight between 0-50 should be relative
					// to the values in this frame) and then any weight between 50-100 would be relevant to the weights in
					// the second frame.  See Post 20 for more info:
					// https://forum.unity3d.com/threads/is-there-some-method-to-add-blendshape-in-editor.298002/#post-2015679
					weights.Add(smr.GetBlendShapeWeight(blendShapeIndex) / 100);
				}

				mesh.Weights = weights;
				primitive.Targets = targets;
				primitive.TargetNames = targetNames;
			}
		}

		private bool IsPBRMetallicRoughness(Material material)
		{
			return material.HasProperty("_Metallic") && material.HasProperty("_MetallicGlossMap");
		}

		private bool IsCommonConstant(Material material)
		{
			return material.HasProperty("_AmbientFactor") &&
			material.HasProperty("_LightMap") &&
			material.HasProperty("_LightFactor");
		}

		private void ExportTextureTransform(TextureInfo def, Material mat, string texName)
		{
			Vector2 offset = mat.GetTextureOffset(texName);
			Vector2 scale = mat.GetTextureScale(texName);

			if (offset == Vector2.zero && scale == Vector2.one) return;

			if (_root.ExtensionsUsed == null)
			{
				_root.ExtensionsUsed = new List<string>(
					new[] { ExtTextureTransformExtensionFactory.EXTENSION_NAME }
				);
			}
			else if (!_root.ExtensionsUsed.Contains(ExtTextureTransformExtensionFactory.EXTENSION_NAME))
			{
				_root.ExtensionsUsed.Add(ExtTextureTransformExtensionFactory.EXTENSION_NAME);
			}

			if (RequireExtensions)
			{
				if (_root.ExtensionsRequired == null)
				{
					_root.ExtensionsRequired = new List<string>(
						new[] { ExtTextureTransformExtensionFactory.EXTENSION_NAME }
					);
				}
				else if (!_root.ExtensionsRequired.Contains(ExtTextureTransformExtensionFactory.EXTENSION_NAME))
				{
					_root.ExtensionsRequired.Add(ExtTextureTransformExtensionFactory.EXTENSION_NAME);
				}
			}

			if (def.Extensions == null)
				def.Extensions = new Dictionary<string, IExtension>();

			def.Extensions[ExtTextureTransformExtensionFactory.EXTENSION_NAME] = new ExtTextureTransformExtension(
				new GLTF.Math.Vector2(offset.x, -offset.y),
				0, // TODO: support rotation
				new GLTF.Math.Vector2(scale.x, scale.y),
				0 // TODO: support UV channels
			);
		}

		private NormalTextureInfo ExportNormalTextureInfo(
			Texture texture,
			TextureMapType textureMapType,
			Material material)
		{
			var info = new NormalTextureInfo();

			info.Index = ExportTexture(texture, textureMapType);

			if (material.HasProperty("_BumpScale"))
			{
				info.Scale = material.GetFloat("_BumpScale");
			}

			return info;
		}

		private OcclusionTextureInfo ExportOcclusionTextureInfo(
			Texture texture,
			TextureMapType textureMapType,
			Material material)
		{
			var info = new OcclusionTextureInfo();

			info.Index = ExportTexture(texture, textureMapType);

			if (material.HasProperty("_OcclusionStrength"))
			{
				info.Strength = material.GetFloat("_OcclusionStrength");
			}

			return info;
		}

		private PbrMetallicRoughness ExportPBRMetallicRoughness(Material material)
		{
			var pbr = new PbrMetallicRoughness();

			if (material.HasProperty("_Color"))
			{
				pbr.BaseColorFactor = material.GetColor("_Color").ToNumericsColorRaw();
			}

			if (material.HasProperty("_MainTex"))
			{
				var mainTex = material.GetTexture("_MainTex");

				if (mainTex != null)
				{
					if(mainTex is Texture2D)
					{
						pbr.BaseColorTexture = ExportTextureInfo(mainTex, TextureMapType.Main);
						ExportTextureTransform(pbr.BaseColorTexture, material, "_MainTex");
					}
					else
					{
						Debug.LogErrorFormat("Can't export a {0} base texture in material {1}", mainTex.GetType(), material.name);
					}
				}
			}

			if (material.HasProperty("_Metallic"))
			{
				var metallicGlossMap = material.GetTexture("_MetallicGlossMap");
				pbr.MetallicFactor = (metallicGlossMap != null) ? 1.0 : material.GetFloat("_Metallic");
			}

			if (material.HasProperty("_Glossiness"))
			{
				var metallicGlossMap = material.GetTexture("_MetallicGlossMap");
				pbr.RoughnessFactor = (metallicGlossMap != null) ? 1.0 : 1.0 - material.GetFloat("_Glossiness");
			}

            if (exportMetallicTexture)
            {
                if (material.HasProperty("_MetallicGlossMap"))
                {
                    var mrTex = material.GetTexture("_MetallicGlossMap");

                    if (mrTex != null)
                    {
                        if (mrTex is Texture2D)
                        {
                            pbr.MetallicRoughnessTexture = ExportTextureInfo(mrTex, TextureMapType.MetallicGloss);
                            ExportTextureTransform(pbr.MetallicRoughnessTexture, material, "_MetallicGlossMap");
                        }
                        else
                        {
                            Debug.LogErrorFormat("Can't export a {0} metallic smoothness texture in material {1}", mrTex.GetType(), material.name);
                        }
                    }
                }
                else if (material.HasProperty("_SpecGlossMap"))
                {
                    var mgTex = material.GetTexture("_SpecGlossMap");

                    if (mgTex != null)
                    {
                        if (mgTex is Texture2D)
                        {
                            pbr.MetallicRoughnessTexture = ExportTextureInfo(mgTex, TextureMapType.SpecGloss);
                            ExportTextureTransform(pbr.MetallicRoughnessTexture, material, "_SpecGlossMap");
                        }
                        else
                        {
                            Debug.LogErrorFormat("Can't export a {0} metallic roughness texture in material {1}", mgTex.GetType(), material.name);
                        }
                    }
                }
            }

			return pbr;
		}

		private MaterialCommonConstant ExportCommonConstant(Material materialObj)
		{
			if (_root.ExtensionsUsed == null)
			{
				_root.ExtensionsUsed = new List<string>(new[] { "KHR_materials_common" });
			}
			else if (!_root.ExtensionsUsed.Contains("KHR_materials_common"))
			{
				_root.ExtensionsUsed.Add("KHR_materials_common");
			}

			if (RequireExtensions)
			{
				if (_root.ExtensionsRequired == null)
				{
					_root.ExtensionsRequired = new List<string>(new[] { "KHR_materials_common" });
				}
				else if (!_root.ExtensionsRequired.Contains("KHR_materials_common"))
				{
					_root.ExtensionsRequired.Add("KHR_materials_common");
				}
			}

			var constant = new MaterialCommonConstant();

			if (materialObj.HasProperty("_AmbientFactor"))
			{
				constant.AmbientFactor = materialObj.GetColor("_AmbientFactor").ToNumericsColorRaw();
			}

            if (exportLightmapTexture)
            {
                if (materialObj.HasProperty("_LightMap"))
                {
                    var lmTex = materialObj.GetTexture("_LightMap");

                    if (lmTex != null)
                    {
                        constant.LightmapTexture = ExportTextureInfo(lmTex, TextureMapType.Light);
                        ExportTextureTransform(constant.LightmapTexture, materialObj, "_LightMap");
                    }
                }
            }

			if (materialObj.HasProperty("_LightFactor"))
			{
				constant.LightmapFactor = materialObj.GetColor("_LightFactor").ToNumericsColorRaw();
			}

			return constant;
		}

		private TextureInfo ExportTextureInfo(Texture texture, TextureMapType textureMapType)
		{
			var info = new TextureInfo();

			info.Index = ExportTexture(texture, textureMapType);

			return info;
		}

		private TextureId ExportTexture(Texture textureObj, TextureMapType textureMapType)
		{
			TextureId id = GetTextureId(_root, textureObj);
			if (id != null)
			{
				return id;
			}

			var texture = new GLTFTexture();

			//If texture name not set give it a unique name using count
			if (textureObj.name == "")
			{
				textureObj.name = (_root.Textures.Count + 1).ToString();
			}

            // Removing this because we are going to put the full path
            // into the Name field
            /*
			if (ExportNames)
			{
				texture.Name = textureObj.name;
			}
            */
            var imagePath = _exportOptions.TexturePathRetriever(textureObj);
            texture.Name = imagePath;

            if (_shouldUseInternalBufferForImages)
		    	{
				texture.Source = ExportImageInternalBuffer(textureObj, textureMapType);
		    	}
		    	else
		    	{
				texture.Source = ExportImage(textureObj, textureMapType);
		    	}
			texture.Sampler = ExportSampler(textureObj);

			_textures.Add(textureObj);

			id = new TextureId
			{
				Id = _root.Textures.Count,
				Root = _root
			};

			_root.Textures.Add(texture);

			return id;
		}

		private ImageId ExportImage(Texture texture, TextureMapType texturMapType)
		{
			ImageId id = GetImageId(_root, texture);
			if (id != null)
			{
				return id;
			}

			var image = new GLTFImage();

			if (ExportNames)
			{
				image.Name = texture.name;
			}

			_imageInfos.Add(new ImageInfo
			{
				texture = texture as Texture2D,
				textureMapType = texturMapType
			});

			var imagePath = _exportOptions.TexturePathRetriever(texture);
			if (string.IsNullOrEmpty(imagePath))
			{
				imagePath = texture.name;
			}

			var filenamePath = Path.ChangeExtension(imagePath, ".png");
			if (!ExportFullPath)
			{
				filenamePath = Path.ChangeExtension(texture.name, ".png");
			}

            //image.Uri = Uri.EscapeUriString(filenamePath);
            /** Not sure if it is better to use EscapeUriString for uris in gltf files.
             * Skipping it seems to solve problems in some applications
             * */
            image.Uri = filenamePath;

            id = new ImageId
			{
				Id = _root.Images.Count,
				Root = _root
			};

			_root.Images.Add(image);

			return id;
		}

		private ImageId ExportImageInternalBuffer(UnityEngine.Texture texture, TextureMapType texturMapType)
		{

		    if (texture == null)
		    {
			throw new Exception("texture can not be NULL.");
		    }

		    var image = new GLTFImage();
		    image.MimeType = "image/png";

		    var byteOffset = _bufferWriter.BaseStream.Position;

		    {//
			var destRenderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
			GL.sRGBWrite = true;
			switch (texturMapType)
			{
			    case TextureMapType.MetallicGloss:
				Graphics.Blit(texture, destRenderTexture, _metalGlossChannelSwapMaterial);
				break;
			    case TextureMapType.Bump:
				Graphics.Blit(texture, destRenderTexture, _normalChannelMaterial);
				break;
			    default:
				Graphics.Blit(texture, destRenderTexture);
				break;
			}

			var exportTexture = new Texture2D(texture.width, texture.height, TextureFormat.ARGB32, false);
			exportTexture.ReadPixels(new Rect(0, 0, destRenderTexture.width, destRenderTexture.height), 0, 0);
			exportTexture.Apply();

            var pngImageData = EncodeTexture(exportTexture);
			_bufferWriter.Write(pngImageData);

			RenderTexture.ReleaseTemporary(destRenderTexture);

			GL.sRGBWrite = false;
			if (Application.isEditor)
			{
			    UnityEngine.Object.DestroyImmediate(exportTexture);
			}
			else
			{
			    UnityEngine.Object.Destroy(exportTexture);
			}
		    }

		    var byteLength = _bufferWriter.BaseStream.Position - byteOffset;

		    byteLength = AppendToBufferMultiplyOf4(byteOffset, byteLength);

		    image.BufferView = ExportBufferView((uint)byteOffset, (uint)byteLength);


		    var id = new ImageId
		    {
			Id = _root.Images.Count,
			Root = _root
		    };
		    _root.Images.Add(image);

		    return id;
		}
		private SamplerId ExportSampler(Texture texture)
		{
			var samplerId = GetSamplerId(_root, texture);
			if (samplerId != null)
				return samplerId;

			var sampler = new Sampler();

			switch (texture.wrapMode)
			{
				case TextureWrapMode.Clamp:
					sampler.WrapS = WrapMode.ClampToEdge;
					sampler.WrapT = WrapMode.ClampToEdge;
					break;
				case TextureWrapMode.Repeat:
					sampler.WrapS = WrapMode.Repeat;
					sampler.WrapT = WrapMode.Repeat;
					break;
				case TextureWrapMode.Mirror:
					sampler.WrapS = WrapMode.MirroredRepeat;
					sampler.WrapT = WrapMode.MirroredRepeat;
					break;
				default:
					Debug.LogWarning("Unsupported Texture.wrapMode: " + texture.wrapMode);
					sampler.WrapS = WrapMode.Repeat;
					sampler.WrapT = WrapMode.Repeat;
					break;
			}

			switch (texture.filterMode)
			{
				case FilterMode.Point:
					sampler.MinFilter = MinFilterMode.NearestMipmapNearest;
					sampler.MagFilter = MagFilterMode.Nearest;
					break;
				case FilterMode.Bilinear:
					sampler.MinFilter = MinFilterMode.LinearMipmapNearest;
					sampler.MagFilter = MagFilterMode.Linear;
					break;
				case FilterMode.Trilinear:
					sampler.MinFilter = MinFilterMode.LinearMipmapLinear;
					sampler.MagFilter = MagFilterMode.Linear;
					break;
				default:
					Debug.LogWarning("Unsupported Texture.filterMode: " + texture.filterMode);
					sampler.MinFilter = MinFilterMode.LinearMipmapLinear;
					sampler.MagFilter = MagFilterMode.Linear;
					break;
			}

			samplerId = new SamplerId
			{
				Id = _root.Samplers.Count,
				Root = _root
			};

			_root.Samplers.Add(sampler);

			return samplerId;
		}

		private AccessorId ExportAccessor(int[] arr, bool isIndices = false)
		{
			uint count = (uint)arr.Length;

			if (count == 0)
			{
				throw new Exception("Accessors can not have a count of 0.");
			}

			var accessor = new Accessor();
			accessor.Count = count;
			accessor.Type = GLTFAccessorAttributeType.SCALAR;

			int min = arr[0];
			int max = arr[0];

			for (var i = 1; i < count; i++)
			{
				var cur = arr[i];

				if (cur < min)
				{
					min = cur;
				}
				if (cur > max)
				{
					max = cur;
				}
			}

			AlignToBoundary(_bufferWriter.BaseStream, 0x00);
			uint byteOffset = CalculateAlignment((uint)_bufferWriter.BaseStream.Position, 4);

			if (max <= byte.MaxValue && min >= byte.MinValue)
			{
				accessor.ComponentType = GLTFComponentType.UnsignedByte;

				foreach (var v in arr)
				{
					_bufferWriter.Write((byte)v);
				}
			}
			else if (max <= sbyte.MaxValue && min >= sbyte.MinValue && !isIndices)
			{
				accessor.ComponentType = GLTFComponentType.Byte;

				foreach (var v in arr)
				{
					_bufferWriter.Write((sbyte)v);
				}
			}
			else if (max <= short.MaxValue && min >= short.MinValue && !isIndices)
			{
				accessor.ComponentType = GLTFComponentType.Short;

				foreach (var v in arr)
				{
					_bufferWriter.Write((short)v);
				}
			}
			else if (max <= ushort.MaxValue && min >= ushort.MinValue)
			{
				accessor.ComponentType = GLTFComponentType.UnsignedShort;

				foreach (var v in arr)
				{
					_bufferWriter.Write((ushort)v);
				}
			}
			else if (min >= uint.MinValue)
			{
				accessor.ComponentType = GLTFComponentType.UnsignedInt;

				foreach (var v in arr)
				{
					_bufferWriter.Write((uint)v);
				}
			}
			else
			{
				accessor.ComponentType = GLTFComponentType.Float;

				foreach (var v in arr)
				{
					_bufferWriter.Write((float)v);
				}
			}

			accessor.Min = new List<double> { min };
			accessor.Max = new List<double> { max };

			uint byteLength = CalculateAlignment((uint)_bufferWriter.BaseStream.Position - byteOffset, 4);

			accessor.BufferView = ExportBufferView(byteOffset, byteLength);

			var id = new AccessorId
			{
				Id = _root.Accessors.Count,
				Root = _root
			};
			_root.Accessors.Add(accessor);

			return id;
		}

		private long AppendToBufferMultiplyOf4(long byteOffset, long byteLength)
		{
		    var moduloOffset = byteLength % 4;
		    if (moduloOffset > 0)
		    {
			for (int i = 0; i < (4 - moduloOffset); i++)
			{
			    _bufferWriter.Write((byte)0x00);
			}
			byteLength = _bufferWriter.BaseStream.Position - byteOffset;
		    }

		    return byteLength;
		}

		private AccessorId ExportAccessor(Vector2[] arr)
		{
			uint count = (uint)arr.Length;

			if (count == 0)
			{
				throw new Exception("Accessors can not have a count of 0.");
			}

			var accessor = new Accessor();
			accessor.ComponentType = GLTFComponentType.Float;
			accessor.Count = count;
			accessor.Type = GLTFAccessorAttributeType.VEC2;

			float minX = arr[0].x;
			float minY = arr[0].y;
			float maxX = arr[0].x;
			float maxY = arr[0].y;

			for (var i = 1; i < count; i++)
			{
				var cur = arr[i];

				if (cur.x < minX)
				{
					minX = cur.x;
				}
				if (cur.y < minY)
				{
					minY = cur.y;
				}
				if (cur.x > maxX)
				{
					maxX = cur.x;
				}
				if (cur.y > maxY)
				{
					maxY = cur.y;
				}
			}

			accessor.Min = new List<double> { minX, minY };
			accessor.Max = new List<double> { maxX, maxY };

			AlignToBoundary(_bufferWriter.BaseStream, 0x00);
			uint byteOffset = CalculateAlignment((uint)_bufferWriter.BaseStream.Position, 4);

			foreach (var vec in arr)
			{
				_bufferWriter.Write(vec.x);
				_bufferWriter.Write(vec.y);
			}

			uint byteLength = CalculateAlignment((uint)_bufferWriter.BaseStream.Position - byteOffset, 4);

			accessor.BufferView = ExportBufferView(byteOffset, byteLength);

			var id = new AccessorId
			{
				Id = _root.Accessors.Count,
				Root = _root
			};
			_root.Accessors.Add(accessor);

			return id;
		}

		private AccessorId ExportAccessor(Vector3[] arr)
		{
			uint count = (uint)arr.Length;

			if (count == 0)
			{
				throw new Exception("Accessors can not have a count of 0.");
			}

			var accessor = new Accessor();
			accessor.ComponentType = GLTFComponentType.Float;
			accessor.Count = count;
			accessor.Type = GLTFAccessorAttributeType.VEC3;

			float minX = arr[0].x;
			float minY = arr[0].y;
			float minZ = arr[0].z;
			float maxX = arr[0].x;
			float maxY = arr[0].y;
			float maxZ = arr[0].z;

			for (var i = 1; i < count; i++)
			{
				var cur = arr[i];

				if (cur.x < minX)
				{
					minX = cur.x;
				}
				if (cur.y < minY)
				{
					minY = cur.y;
				}
				if (cur.z < minZ)
				{
					minZ = cur.z;
				}
				if (cur.x > maxX)
				{
					maxX = cur.x;
				}
				if (cur.y > maxY)
				{
					maxY = cur.y;
				}
				if (cur.z > maxZ)
				{
					maxZ = cur.z;
				}
			}

			accessor.Min = new List<double> { minX, minY, minZ };
			accessor.Max = new List<double> { maxX, maxY, maxZ };

			AlignToBoundary(_bufferWriter.BaseStream, 0x00);
			uint byteOffset = CalculateAlignment((uint)_bufferWriter.BaseStream.Position, 4);

			foreach (var vec in arr)
			{
				_bufferWriter.Write(vec.x);
				_bufferWriter.Write(vec.y);
				_bufferWriter.Write(vec.z);
			}

			uint byteLength = CalculateAlignment((uint)_bufferWriter.BaseStream.Position - byteOffset, 4);

			accessor.BufferView = ExportBufferView(byteOffset, byteLength);

			var id = new AccessorId
			{
				Id = _root.Accessors.Count,
				Root = _root
			};
			_root.Accessors.Add(accessor);

			return id;
		}

		private AccessorId ExportAccessor(Vector4[] arr)
		{
			uint count = (uint)arr.Length;

			if (count == 0)
			{
				throw new Exception("Accessors can not have a count of 0.");
			}

			var accessor = new Accessor();
			accessor.ComponentType = GLTFComponentType.Float;
			accessor.Count = count;
			accessor.Type = GLTFAccessorAttributeType.VEC4;

			float minX = arr[0].x;
			float minY = arr[0].y;
			float minZ = arr[0].z;
			float minW = arr[0].w;
			float maxX = arr[0].x;
			float maxY = arr[0].y;
			float maxZ = arr[0].z;
			float maxW = arr[0].w;

			for (var i = 1; i < count; i++)
			{
				var cur = arr[i];

				if (cur.x < minX)
				{
					minX = cur.x;
				}
				if (cur.y < minY)
				{
					minY = cur.y;
				}
				if (cur.z < minZ)
				{
					minZ = cur.z;
				}
				if (cur.w < minW)
				{
					minW = cur.w;
				}
				if (cur.x > maxX)
				{
					maxX = cur.x;
				}
				if (cur.y > maxY)
				{
					maxY = cur.y;
				}
				if (cur.z > maxZ)
				{
					maxZ = cur.z;
				}
				if (cur.w > maxW)
				{
					maxW = cur.w;
				}
			}

			accessor.Min = new List<double> { minX, minY, minZ, minW };
			accessor.Max = new List<double> { maxX, maxY, maxZ, maxW };

			AlignToBoundary(_bufferWriter.BaseStream, 0x00);
			uint byteOffset = CalculateAlignment((uint)_bufferWriter.BaseStream.Position, 4);

			foreach (var vec in arr)
			{
				_bufferWriter.Write(vec.x);
				_bufferWriter.Write(vec.y);
				_bufferWriter.Write(vec.z);
				_bufferWriter.Write(vec.w);
			}

			uint byteLength = CalculateAlignment((uint)_bufferWriter.BaseStream.Position - byteOffset, 4);

			accessor.BufferView = ExportBufferView(byteOffset, byteLength);

			var id = new AccessorId
			{
				Id = _root.Accessors.Count,
				Root = _root
			};
			_root.Accessors.Add(accessor);

			return id;
		}

		private AccessorId ExportAccessor(Color[] arr)
		{
			uint count = (uint)arr.Length;

			if (count == 0)
			{
				throw new Exception("Accessors can not have a count of 0.");
			}

			var accessor = new Accessor();
			accessor.ComponentType = GLTFComponentType.Float;
			accessor.Count = count;
			accessor.Type = GLTFAccessorAttributeType.VEC4;

			float minR = arr[0].r;
			float minG = arr[0].g;
			float minB = arr[0].b;
			float minA = arr[0].a;
			float maxR = arr[0].r;
			float maxG = arr[0].g;
			float maxB = arr[0].b;
			float maxA = arr[0].a;

			for (var i = 1; i < count; i++)
			{
				var cur = arr[i];

				if (cur.r < minR)
				{
					minR = cur.r;
				}
				if (cur.g < minG)
				{
					minG = cur.g;
				}
				if (cur.b < minB)
				{
					minB = cur.b;
				}
				if (cur.a < minA)
				{
					minA = cur.a;
				}
				if (cur.r > maxR)
				{
					maxR = cur.r;
				}
				if (cur.g > maxG)
				{
					maxG = cur.g;
				}
				if (cur.b > maxB)
				{
					maxB = cur.b;
				}
				if (cur.a > maxA)
				{
					maxA = cur.a;
				}
			}

			accessor.Min = new List<double> { minR, minG, minB, minA };
			accessor.Max = new List<double> { maxR, maxG, maxB, maxA };

			AlignToBoundary(_bufferWriter.BaseStream, 0x00);
			uint byteOffset = CalculateAlignment((uint)_bufferWriter.BaseStream.Position, 4);

			foreach (var color in arr)
			{
				_bufferWriter.Write(color.r);
				_bufferWriter.Write(color.g);
				_bufferWriter.Write(color.b);
				_bufferWriter.Write(color.a);
			}

			uint byteLength = CalculateAlignment((uint)_bufferWriter.BaseStream.Position - byteOffset, 4);

			accessor.BufferView = ExportBufferView(byteOffset, byteLength);

			var id = new AccessorId
			{
				Id = _root.Accessors.Count,
				Root = _root
			};
			_root.Accessors.Add(accessor);

			return id;
		}

		private BufferViewId ExportBufferView(uint byteOffset, uint byteLength)
		{
			var bufferView = new BufferView
			{
				Buffer = _bufferId,
				ByteOffset = byteOffset,
				ByteLength = byteLength
			};

			var id = new BufferViewId
			{
				Id = _root.BufferViews.Count,
				Root = _root
			};

			_root.BufferViews.Add(bufferView);

			return id;
		}

		public MaterialId GetMaterialId(GLTFRoot root, Material materialObj)
		{
			for (var i = 0; i < _materials.Count; i++)
			{
				if (_materials[i] == materialObj)
				{
					return new MaterialId
					{
						Id = i,
						Root = root
					};
				}
			}

			return null;
		}

		public TextureId GetTextureId(GLTFRoot root, Texture textureObj)
		{
			for (var i = 0; i < _textures.Count; i++)
			{
				if (_textures[i] == textureObj)
				{
					return new TextureId
					{
						Id = i,
						Root = root
					};
				}
			}

			return null;
		}

		public ImageId GetImageId(GLTFRoot root, Texture imageObj)
		{
			for (var i = 0; i < _imageInfos.Count; i++)
			{
				if (_imageInfos[i].texture == imageObj)
				{
					return new ImageId
					{
						Id = i,
						Root = root
					};
				}
			}

			return null;
		}

		public SamplerId GetSamplerId(GLTFRoot root, Texture textureObj)
		{
			for (var i = 0; i < root.Samplers.Count; i++)
			{
				bool filterIsNearest = root.Samplers[i].MinFilter == MinFilterMode.Nearest
					|| root.Samplers[i].MinFilter == MinFilterMode.NearestMipmapNearest
					|| root.Samplers[i].MinFilter == MinFilterMode.NearestMipmapLinear;

				bool filterIsLinear = root.Samplers[i].MinFilter == MinFilterMode.Linear
					|| root.Samplers[i].MinFilter == MinFilterMode.LinearMipmapNearest;

				bool filterMatched = textureObj.filterMode == FilterMode.Point && filterIsNearest
					|| textureObj.filterMode == FilterMode.Bilinear && filterIsLinear
					|| textureObj.filterMode == FilterMode.Trilinear && root.Samplers[i].MinFilter == MinFilterMode.LinearMipmapLinear;

				bool wrapMatched = textureObj.wrapMode == TextureWrapMode.Clamp && root.Samplers[i].WrapS == WrapMode.ClampToEdge
					|| textureObj.wrapMode == TextureWrapMode.Repeat && root.Samplers[i].WrapS == WrapMode.Repeat
					|| textureObj.wrapMode == TextureWrapMode.Mirror && root.Samplers[i].WrapS == WrapMode.MirroredRepeat;

				if (filterMatched && wrapMatched)
				{
					return new SamplerId
					{
						Id = i,
						Root = root
					};
				}
			}

			return null;
		}

		protected static DrawMode GetDrawMode(MeshTopology topology)
		{
			switch (topology)
			{
				case MeshTopology.Points: return DrawMode.Points;
				case MeshTopology.Lines: return DrawMode.Lines;
				case MeshTopology.LineStrip: return DrawMode.LineStrip;
				case MeshTopology.Triangles: return DrawMode.Triangles;
			}

			throw new Exception("glTF does not support Unity mesh topology: " + topology);
		}
	}
}