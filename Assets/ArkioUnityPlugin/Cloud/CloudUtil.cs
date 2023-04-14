using System;
using System.IO;
using TinyJson;

namespace Arkio
{
    // Utility class with some useful methods
    public static class CloudUtil
    {
        // Throws exception if condition is false
        public static void Assert(bool condition)
        {
            Assert(condition, "");
        }

        // Throws exception if condition is false and shows message
        public static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                //TODO find another way to show an error message as this does not show
                // in the unity editor when this method gets called in an async method
                throw new Exception(String.Format("Assert failed. {0}", message));
            }
        }

        // Method for calculating the needed size of parts
        // when splitting an array into n smaller parts
        // totalLength : The length of the array
        // n : The number of parts
        // Example if totalLength is 10 and n is 3 then 4 should be returned
        // if totalLength is 9 and n is 3 then 3 should be returned
        public static int GetBytePartSize(int totalLength, int n)
        {
            if (totalLength < n) return 1;
            int partSize = totalLength / n;
            return (totalLength % n) == 0 ? partSize : partSize + 1;
        }

        // Get sub array in an array of a bytes
        // bytes : The original byte array
        // index : Index where to start
        // maxLength : Maximum length of the sub array.
        // In many cases the returned array will have this length
        // but in case maxLength would result in getting values out of the bounds
        // of the array the length of the returned array will be smaller
        public static byte[] GetSubByteArray(byte[] bytes, int index, int maxLength)
        {
            int byteLen = bytes.Length;
            if (index < byteLen)
            {
                int endIndex = index + maxLength - 1;
                int maxIndex = byteLen - 1;
                if (endIndex > maxIndex)
                {
                    endIndex = maxIndex;
                }
                byte[] output = new byte[endIndex - index + 1];
                int i2 = 0;
                for (int i = index; i <= endIndex; i++)
                {
                    output[i2] = bytes[i];
                    i2++;
                }
                return output;
            }
            else
            {
                // index not within array
                return null;
            }
        }
    }
}

