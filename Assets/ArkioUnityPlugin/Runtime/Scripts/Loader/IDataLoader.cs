using System.IO;
using System.Threading.Tasks;

namespace ArkioUnityPlugin.Loader
{
	public interface IDataLoader
	{
		Task<Stream> LoadStreamAsync(string relativeFilePath);
	}
}
