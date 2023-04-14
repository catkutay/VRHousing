﻿using UnityEngine;
using ArkioUnityPlugin.Loader;

namespace ArkioUnityPlugin
{
	public abstract class ImporterFactory : ScriptableObject
	{
		public abstract GLTFSceneImporter CreateSceneImporter(string gltfFileName, ImportOptions options);
	}

	public class DefaultImporterFactory : ImporterFactory
	{
		public override GLTFSceneImporter CreateSceneImporter(string gltfFileName, ImportOptions options)
		{
			return new GLTFSceneImporter(gltfFileName, options);
		}
	}
}
