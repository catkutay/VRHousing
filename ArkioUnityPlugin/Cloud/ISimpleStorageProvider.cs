// Interface for classes that implement storing
// of values with keys

// The purpose of this is to provide an abstract way
// of storing key value pairs because we will sometimes
// be using PlayerPrefs and sometimes EditorPrefs.
// In the future, if we start using this in other
// software like Revit or Rhino, we might want to
// modify this in various ways

namespace Arkio
{
    public interface IStorageProvider
    {
        // Get a string from the storage
        // key : The key of the value to get
        string GetString(string key);

        // Set string with a specific key and value
        void SetString(string key, string value);

        // Check if key exists
        bool HasKey(string key);
    }
}

