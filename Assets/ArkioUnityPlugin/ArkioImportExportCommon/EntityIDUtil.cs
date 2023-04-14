using SysDia = System.Diagnostics;

namespace ArkioImportExportCommon
{
    // Methods related to entity id.
    // It's an id of objects being exported
    // to and from arkio and other software
    public static class EntityIDUtil
    {
        public static bool GetEntityIdFromNodeName(string name, out ulong id)
        {
            string firstPart;
            return GetEntityIdFromNodeName(name, out id, out firstPart);
        }

        // Get Arkio entity id from a gltf node name
        // that is in the form "XXXX_YYYYYYYYYY"
        // where the YYYYYYYYYY part is the id encoded
        // as a base 64 string
        // Also sets firstPart to the part of the string in front of the first _ character
        // returns true if successfull, else false
        public static bool GetEntityIdFromNodeName(string name, out ulong id, out string firstPart)
        {
            id = 0;
            firstPart = null;
            string[] parts = name.Split('_');

            if (parts.Length != 2)
            {
                return false;
            }

            firstPart = parts[0];

            string idStr = parts[1];
            return EntityIdToUInt64(idStr, out id);
        }

        // Get bit at specified index in byte
        public static byte GetBit(byte b, byte index)
        {
            int bi = (int)b;
            int mask = 1;
            mask = mask << index;
            return (bi & mask) == 0 ? (byte)0 : (byte)1;
        }

        // Mirror bits in a byte
        // Example: 10101110 becomes 01110101
        public static byte SwapBitOrder(byte b)
        {
            int result = 0;
            for (byte i = 0; i < 8; i++)
            {
                result |= ((int)GetBit(b, i)) << (7 - (int)i);
            }
            return (byte)result;
        }

        // Mirror each byte in an array of bytes
        // The positions of the bytes in the array is not mirrored,
        // just the bits in each byte.
        public static void SwapBitOrder(ref byte[] b)
        {
            int n = b.Length;
            for (int i = 0; i < n; i++)
            {
                b[i] = SwapBitOrder(b[i]);
            }
        }

        // Get a 10 characters base 64 string version of entity id
        // Only the lowest 60 bits of the 64 bits in the 64 bit integer are used
        public static string EntityIdToString(ulong id)
        {
            byte[] bytes = System.BitConverter.GetBytes(id);

            // Need to mirror bits so that the wrong bits won'g get cut off
            SwapBitOrder(ref bytes);

            string idString = System.Convert.ToBase64String(bytes);

            // We are only using a 60 bit integer as id and since each
            // character in a 64 base encoding represents 6 bits
            // we only need the first 10 characters
            idString = idString.Substring(0, 10);
            return idString;
        }

        // Convert a base 64 entity id string with 10 characters to a 64 bit integer.
        // Each character is 6 bits so only 60 bits are used and
        // therefore, the highest 4 bits of the 64 bit integer are always 0.
        // returns true if successful, else false
        public static bool EntityIdToUInt64(string entityId, out ulong value)
        {
            value = 0;

            try
            {
                // Add letter A which represents the bits 000000 to the end
                // so it will be 66 bits total as the entity id string is only 10 characters
                // representing 60 bits when using base 64 encoding
                string entityIdChars = string.Format("{0}A=", entityId);

                byte[] b = System.Convert.FromBase64String(entityIdChars);

                // Need to shap bit order so this will be correct because of these bytes
                SwapBitOrder(ref b);
                ulong id = 0;
                id |= (ulong)b[0];
                id |= (((ulong)b[1]) << 8);
                id |= (((ulong)b[2]) << 16);
                id |= (((ulong)b[3]) << 24);
                id |= (((ulong)b[4]) << 32);
                id |= (((ulong)b[5]) << 40);
                id |= (((ulong)b[6]) << 48);
                id |= (((ulong)b[7]) << 56);
                value = id;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

