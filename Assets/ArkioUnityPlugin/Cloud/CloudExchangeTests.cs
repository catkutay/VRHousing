using UnityEngine;
using System.Text;
using System.Threading.Tasks;

public class CloudExchangeTests : MonoBehaviour
{
    Arkio.CloudExchangeLink cloudLink;

    public string TestCode = "123456";
    public string TestObjectName = "testObjectName";
    public string TestObjectContent = "this is a test content to encrypt";

    void Start()
    {
        cloudLink = new Arkio.CloudExchangeLink();
        cloudLink.OnLog += OnLog;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            _ = cloudLink.GenerateCodeAndLink();
        }

        if (Input.GetKeyDown(KeyCode.O))
        {
            _ = cloudLink.LinkWithExistingCode(TestCode, null);
        }

        /*
        if (Input.GetKeyDown(KeyCode.I))
        {
            system.AddToIndex(new List<CloudExchangeSystem.IndexFile.Entry>() { new CloudExchangeSystem.IndexFile.Entry() { Type = 0, Filename = "test" } });
        }
        */
        if (Input.GetKeyDown(KeyCode.M))
        {
            _ = IndexFileTest();
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            _ = PutObjectTest();
        }

        if (Input.GetKeyDown(KeyCode.N))
        {
            _ = GetObjectTest();
        }
    }

    private async Task IndexFileTest()
    {
        var indexFile = await cloudLink.DownloadIndex();
        Debug.Log(indexFile);
    }

    private async Task PutObjectTest()
    {
        string objectName = TestObjectName;
        byte[] objectContent = Encoding.UTF8.GetBytes(TestObjectContent);
        await cloudLink.PutObject(objectName, objectContent, 1, "testInfo",
            System.DateTime.Now, Arkio.CloudExchangeLink.IndexFile.Entry.FileInfo.FileType.ArkioExport, "");
    }

    async Task GetObjectTest()
    {
        var bytes = await cloudLink.GetObjectRaw(TestObjectName);
        Debug.Log(Encoding.UTF8.GetString(bytes));
    }

    void OnLog(string msg)
    {
        Debug.Log(msg);
    }
}



