using System.IO;

namespace ArkioImportExportCommon
{
    /** Class with methods dealing with binary gltf files */
    public static class BinUtil
    {
        /** Block size of the binary buffer. */
        public static readonly uint bufferBlockSize = 4;

        /** Calculates the needed size of a buffer when it is divided into
         * blocks of fixed number of bytes.
         * Example if block size is 4 and the current size of the buffer is 6
         * then the needed size would need to be 8 because else the buffer
         * would not consist of whole blocks.
         * @param currentSize The current size of the buffer
         * @param blockSize The size of each block in bytes
         * @return Returns the new length that is needed for the buffer.
         */
        public static uint CalculateBufferSize(uint currentSize, uint blockSize)
        {
            return (currentSize + blockSize - 1) / blockSize * blockSize;
        }

        /** Calculate the needed size of a buffer and assume the block size
         * is equal to the bufferBlockSize constant */
        public static uint CalculateBufferSize(uint currentSize)
        {
            return CalculateBufferSize(currentSize, bufferBlockSize);
        }

         /** Pad a stream with bytes
         * @param stream The stream to add bytes to.
         * @param nrOfBytes The number of bytes to  add to the stream.
         * @param value The value of each byte that will be added.
         * 
         */
        public static void PadStreamWithBytes(Stream stream, uint nrOfBytes, byte value = (byte)0x00)
        {
            for (int i = 0; i < nrOfBytes; i++)
            {
                stream.WriteByte(value);
            }
        }

        /** Pad a stream with as many bytes are needed so it will
         * fit the block size specified in bufferBlockSize constant */
        public static void PadStreamWithNeededBytes(Stream stream, byte value = (byte)0x00)
        {
            uint streamPos = (uint)stream.Position;
            uint currentBufferLength = streamPos;
            uint neededLength = CalculateBufferSize(streamPos);
            uint padding = neededLength - currentBufferLength;
            PadStreamWithBytes(stream, padding, value);
        }

        /** Convenience function to copy from a stream to a binary writer, for
        * compatibility with pre-.NET 4.0.
        * Note: Does not set position/seek in either stream. After executing,
        * the input buffer's position should be the end of the stream.
        * 
        * @param input Stream to copy from.
        * @param output Stream to copy to.
        */
        public static void CopyStream(Stream input, BinaryWriter output)
        {
            byte[] buffer = new byte[8 * 1024];
            int length;
            while ((length = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, length);
            }
        }
    }
}

