using System.IO;

namespace ArkioUnityPlugin.Loader
{
	public interface IDataLoader2 : IDataLoader
	{
		Stream LoadStream(string relativeFilePath);
	}
}
