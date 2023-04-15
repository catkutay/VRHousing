using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// a wrapper around a s3Client handle, constructed with Arkio's Cognito user credentials
public class AwsClient
{
    public delegate void FileUploadProgress(string hash, string userData, int position, int size);

    private RegionEndpoint RegionEndpoint = RegionEndpoint.USEast2;
    private const string accessKeyId = "AKIAZO3455NR3KKZHUHD";
    private const string secretAccessKey = "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";

    AmazonS3Client s3Client; // our S3 Client or null iff initialization failed

    public AwsClient(Action<string> onLog)
    {
        OnLog += onLog;
        Init();
    }

    // a Constructor! Passes Exceptions through.
    public AwsClient()
    {
        Init();
    }

    private void Init()
    {
        try
        {
            s3Client = new AmazonS3Client(accessKeyId, secretAccessKey, RegionEndpoint);
        }
        catch(Exception e)
        {
            LogError(String.Format("Failed to create S3Client. {0}", e.Message));
            throw e;
        }
    }

    // For writing out log messages
    public Action<string> OnLog;

    // Like OnLog, but for error messages
    public Action<string> OnLogError;

    // Call this to log a message
    private void Log(string msg)
    {
        if (OnLog != null)
        {
            OnLog.Invoke(msg);
        }
    }

    // Call this to log an error message
    private void LogError(string msg)
    {
        if (OnLogError != null)
            OnLogError.Invoke(msg);
    }

    // Get object from cloud
    // bucket : Bucket name
    // key : key of object
    // cancellationToken : Cancellation token for receiving notice of cancellation
    // Returns
    //   response : Object with response data
    //   httpStatusCode : A http status code for the request even in the case where response is null, so
    //   we can know why the request failed in the case response is null
    public async Task<(GetObjectResponse response, System.Net.HttpStatusCode? httpStatusCode)> GetObjectAsync(
        string bucket, string key, CancellationToken cancellationToken = default)
    {
       // Debug.LogFormat("Getting object {0}", key);

        GetObjectResponse response = null;
        System.Net.HttpStatusCode? httpStatusCode = null;

        try
        {
            GetObjectRequest request = new GetObjectRequest();
            request.BucketName = bucket;
            request.Key = key;

            response = await s3Client.GetObjectAsync(request, cancellationToken);
            httpStatusCode = response.HttpStatusCode;
        }
        catch (AmazonS3Exception e)
        {
            httpStatusCode = e.StatusCode;
        }
        catch (Exception e)
        {
            LogError(String.Format("Failed to get object. {0}", e.Message));
        }

        return (response, httpStatusCode);
    }

    private void OnProgress(object sender, WriteObjectProgressArgs e)
    {
        Log(e.PercentDone.ToString());
    }

    public async Task<PutObjectResponse> PutObjectAsync(string bucket, string key, string userData, byte[] buffer, FileUploadProgress onProgress = null)
    {
       // Debug.LogFormat("Putting object {0} size = {1}", key, UnitsUtility.ToByteFormatedString(buffer.Length));

        PutObjectResponse response = null;

        try
        {
            var stream = new MemoryStream(buffer);

            PutObjectRequest request = new PutObjectRequest();
            request.BucketName = bucket;
            request.Key = key;
            request.InputStream = stream;
            request.CannedACL = S3CannedACL.Private; // this needs to be private for a bucket which is not public;

            if (onProgress != null)
                request.StreamTransferProgress += (object sender, Amazon.Runtime.StreamTransferProgressArgs e) => { onProgress.Invoke(key, userData, (int)e.TransferredBytes, (int)e.TotalBytes); }; 

            response = await s3Client.PutObjectAsync(request);
        }
        catch (AmazonS3Exception e)
        {
            LogError(String.Format("Could not send object to cloud. {0}", e.Message));
        }
        catch (Exception e)
        {
            LogError(String.Format("Could not send object to cloud. {0}", e.Message));
        }

        return response;
    }

    // Read a stream
    // responseStream Stream to read
    // onProgress Method to call when some progress is done reading the stream
    // onProgress has tree arguments,
    //   int : nr of bytes already read
    //   int : total number of bytes in stream
    //   string : name or hash of object being read
    // objectHash : Name or hash of object being read
    public static async Task<byte[]> ReadStream(Stream responseStream, Action<int, int, string> onProgress = null, string objectHash = null)
    {
        byte[] buffer = new byte[16 * 1024];
        using (MemoryStream ms = new MemoryStream())
        {
            int read;
            while ((read = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, read);

                if (onProgress != null)
                    onProgress.Invoke((int)ms.Length, (int)responseStream.Length, objectHash);

                //await Task.Yield();
            }
            return ms.ToArray();
            /*
            int read;
            while ((read = responseStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, read);

                if (onProgress != null)
                    onProgress.Invoke((int)ms.Length, (int)responseStream.Length, objectHash);

                await Task.Yield();
            }
            return ms.ToArray();
            */
        }
    }
}

