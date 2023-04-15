using System.Collections;
using System.Collections.Generic;

namespace Arkio
{
    public class CloudUtilTest
    {
        public static void TestGetBytePartSize()
        {
            int partSize = CloudUtil.GetBytePartSize(10, 3);
            CloudUtil.Assert(partSize == 4);
            partSize = CloudUtil.GetBytePartSize(23, 4);
            CloudUtil.Assert(partSize == 6);
            partSize = CloudUtil.GetBytePartSize(100, 20);
            CloudUtil.Assert(partSize == 5);
            partSize = CloudUtil.GetBytePartSize(1, 20);
            CloudUtil.Assert(partSize == 1);
        }

        public static void TestGetSubByteArray()
        {
            byte[] bytes = new byte[]
            {
                0, 1, 2, 3, 4, 5, 6, 7, 8, 9
            };
            byte[] outBytes = CloudUtil.GetSubByteArray(bytes, 3, 2);

            CloudUtil.Assert(outBytes.Length == 2);
            CloudUtil.Assert(outBytes[0] == 3);
            CloudUtil.Assert(outBytes[1] == 4);

            outBytes = CloudUtil.GetSubByteArray(bytes, 10, 1);
            CloudUtil.Assert(outBytes == null);

            outBytes = CloudUtil.GetSubByteArray(bytes, 9, 1);
            CloudUtil.Assert(outBytes.Length == 1);
            CloudUtil.Assert(outBytes[0] == 9);

            outBytes = CloudUtil.GetSubByteArray(bytes, 6, 10);
            CloudUtil.Assert(outBytes.Length == 4);
            CloudUtil.Assert(outBytes[0] == 6);
            CloudUtil.Assert(outBytes[3] == 9);
        }
    }
}

